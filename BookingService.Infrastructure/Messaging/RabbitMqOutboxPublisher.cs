using RabbitMQ.Client;

namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// Publishes outbox messages to a durable topic exchange in RabbitMQ, with the message type as routing key.
/// Messages are persistent and published with confirmations: <see cref="PublishAsync"/> returns only once the
/// broker accepted the message and routed it to at least one queue (<c>mandatory</c>), so a message nobody can
/// receive yet stays pending in the outbox instead of being dropped.
/// One connection and channel are shared and opened lazily; a failure closes them and the next call reopens them.
/// </summary>
public sealed class RabbitMqOutboxPublisher : IOutboxPublisher, IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqOutboxPublisher(string connectionString, RabbitMqOptions options)
    {
        _factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            ClientProvidedName = "booking-service-outbox-relay"
        };
        _options = options;
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await OpenChannelAsync(cancellationToken);
            var properties = new BasicProperties
            {
                MessageId = message.Id.ToString(),
                Type = message.Type,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(new DateTimeOffset(
                    DateTime.SpecifyKind(message.OccurredAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds())
            };

            await channel.BasicPublishAsync(
                _options.Exchange,
                routingKey: message.Type,
                mandatory: true,
                basicProperties: properties,
                body: OutboxEnvelope.Serialize(message),
                cancellationToken: cancellationToken);
        }
        catch
        {
            await CloseAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _gate.Dispose();
    }

    private async Task<IChannel> OpenChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await CloseAsync();
        _connection = await _factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await _channel.ExchangeDeclareAsync(
            _options.Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        return _channel;
    }

    private async Task CloseAsync()
    {
        try
        {
            if (_channel is not null)
            {
                await _channel.DisposeAsync();
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
        }
        catch
        {
            // Already broken: the next publication opens a new connection.
        }
        finally
        {
            _channel = null;
            _connection = null;
        }
    }
}
