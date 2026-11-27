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
    // Same image as docker-compose.yml.
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

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
}
