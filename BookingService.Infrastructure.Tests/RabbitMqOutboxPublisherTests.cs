using System.Text.Json;
using BookingService.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Testcontainers.RabbitMq;

namespace BookingService.Infrastructure.Tests;

/// <summary>
/// Starts a disposable RabbitMQ container (Docker is required) shared by the tests of this class.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private const ushort ManagementPort = 15672;

    // Same image as docker-compose.yml; the management API lets tests drop the publisher's connection.
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithPortBinding(ManagementPort, true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Uri ManagementUri => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(ManagementPort)}/");

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public sealed class RabbitMqOutboxPublisherTests : IClassFixture<RabbitMqFixture>
{
    private readonly RabbitMqFixture _rabbitMq;

    public RabbitMqOutboxPublisherTests(RabbitMqFixture rabbitMq)
    {
        _rabbitMq = rabbitMq;
    }

    [Fact]
    public async Task A_ticketSold_message_reaches_a_bound_queue_in_the_contract_envelope()
    {
        var options = new RabbitMqOptions { Exchange = $"booking.events.{Guid.NewGuid():N}" };
        await using var connection = await new ConnectionFactory { Uri = new Uri(_rabbitMq.ConnectionString) }
            .CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        var queue = (await channel.QueueDeclareAsync()).QueueName;
        await channel.QueueBindAsync(queue, options.Exchange, PostgresTicketSoldOutbox.TicketSoldType);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = PostgresTicketSoldOutbox.TicketSoldType,
            OccurredAtUtc = new DateTime(2026, 11, 16, 8, 7, 30, DateTimeKind.Utc),
            Payload = """{"purchaseId":"3f0b6a1e-8c1d-4c55-9b39-0d6c1b0f8a21","ticketCode":"TK-9f1c"}"""
        };

        await using (var publisher = new RabbitMqOutboxPublisher(_rabbitMq.ConnectionString, options))
        {
            await publisher.PublishAsync(message, CancellationToken.None);
        }

        var delivered = await channel.BasicGetAsync(queue, autoAck: true);
        Assert.NotNull(delivered);
        Assert.Equal(message.Id.ToString(), delivered.BasicProperties.MessageId);
        Assert.Equal("ticketSold", delivered.BasicProperties.Type);
        Assert.Equal("application/json", delivered.BasicProperties.ContentType);
        Assert.Equal(DeliveryModes.Persistent, delivered.BasicProperties.DeliveryMode);

        var envelope = JsonDocument.Parse(delivered.Body).RootElement;
        Assert.Equal(message.Id, envelope.GetProperty("messageId").GetGuid());
        Assert.Equal("ticketSold", envelope.GetProperty("type").GetString());
        Assert.Equal(message.OccurredAtUtc, envelope.GetProperty("occurredAt").GetDateTime().ToUniversalTime());
        Assert.Equal("TK-9f1c", envelope.GetProperty("data").GetProperty("ticketCode").GetString());
    }

    [Fact]
    public async Task A_message_no_queue_can_receive_is_not_reported_as_published()
    {
        var options = new RabbitMqOptions { Exchange = $"booking.events.{Guid.NewGuid():N}" };
        await using var publisher = new RabbitMqOutboxPublisher(_rabbitMq.ConnectionString, options);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = PostgresTicketSoldOutbox.TicketSoldType,
            OccurredAtUtc = DateTime.UtcNow,
            Payload = "{}"
        };

        // mandatory: the broker returns the message instead of dropping it, so the relay keeps it pending.
        await Assert.ThrowsAsync<PublishReturnException>(() => publisher.PublishAsync(message, CancellationToken.None));
    }

    [Fact]
    public async Task An_unreachable_broker_fails_the_publication()
    {
        await using var publisher = new RabbitMqOutboxPublisher(
            "amqp://guest:guest@127.0.0.1:1/",
            new RabbitMqOptions());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<BrokerUnreachableException>(() =>
            publisher.PublishAsync(Message(), timeout.Token));
    }

    [Fact]
    public async Task Concurrent_publications_on_one_publisher_all_arrive()
    {
        var (options, channel, queue, connection) = await BoundQueueAsync();
        await using (connection)
        await using (channel)
        {
            var messages = Enumerable.Range(0, 20).Select(_ => Message()).ToList();
            await using (var publisher = new RabbitMqOutboxPublisher(_rabbitMq.ConnectionString, options))
            {
                await Task.WhenAll(messages.Select(m => publisher.PublishAsync(m, CancellationToken.None)));
            }

            var received = new List<string>();
            while (await channel.BasicGetAsync(queue, autoAck: true) is { } delivered)
            {
                received.Add(delivered.BasicProperties.MessageId!);
            }

            Assert.Equal(messages.Select(m => m.Id.ToString()).Order(), received.Order());
        }
    }

    [Fact]
    public async Task A_dropped_connection_is_reopened_by_the_next_publication()
    {
        var (options, channel, queue, connection) = await BoundQueueAsync();
        await using (connection)
        await using (channel)
        {
            await using var publisher = new RabbitMqOutboxPublisher(_rabbitMq.ConnectionString, options);
            await publisher.PublishAsync(Message(), CancellationToken.None);

            await CloseConnectionAsync("booking-service-outbox-relay");

            // As the relay does: a failed attempt is retried in a later cycle.
            var failures = 0;
            var delivered = false;
            for (var attempt = 0; attempt < 3 && !delivered; attempt++)
            {
                try
                {
                    await publisher.PublishAsync(Message(), CancellationToken.None);
                    delivered = true;
                }
                catch (Exception)
                {
                    failures++;
                }
            }

            Assert.True(delivered);
            Assert.True(failures <= 1, $"Expected at most one failed attempt, got {failures}.");
            Assert.Equal(2u, (await channel.QueueDeclarePassiveAsync(queue)).MessageCount);
        }
    }

    private static OutboxMessage Message() => new()
    {
        Id = Guid.NewGuid(),
        Type = PostgresTicketSoldOutbox.TicketSoldType,
        OccurredAtUtc = DateTime.UtcNow,
        Payload = "{}"
    };

    private async Task<(RabbitMqOptions Options, IChannel Channel, string Queue, IConnection Connection)> BoundQueueAsync()
    {
        var options = new RabbitMqOptions { Exchange = $"booking.events.{Guid.NewGuid():N}" };
        var connection = await new ConnectionFactory { Uri = new Uri(_rabbitMq.ConnectionString) }.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        var queue = (await channel.QueueDeclareAsync()).QueueName;
        await channel.QueueBindAsync(queue, options.Exchange, PostgresTicketSoldOutbox.TicketSoldType);
        return (options, channel, queue, connection);
    }

    /// <summary>Force-closes the broker connection with the given client name through the management API.</summary>
    private async Task CloseConnectionAsync(string clientName)
    {
        var userInfo = Uri.UnescapeDataString(new Uri(_rabbitMq.ConnectionString).UserInfo);
        using var http = new HttpClient { BaseAddress = _rabbitMq.ManagementUri };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(userInfo)));

        // The management API lists a new connection after a short delay.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var connections = JsonDocument.Parse(await http.GetStringAsync("api/connections")).RootElement;
            var name = connections.EnumerateArray()
                .Where(c => c.TryGetProperty("client_properties", out var properties)
                    && properties.TryGetProperty("connection_name", out var connectionName)
                    && connectionName.GetString() == clientName)
                .Select(c => c.GetProperty("name").GetString())
                .FirstOrDefault();

            if (name is not null)
            {
                (await http.DeleteAsync($"api/connections/{Uri.EscapeDataString(name)}")).EnsureSuccessStatusCode();
                await Task.Delay(500);
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Connection {clientName} never showed up in the management API.");
    }
}
