using System.Text.Json;
using BookingService.Application.Abstractions;
using BookingService.Application.Purchases;
using BookingService.Application.Purchases.Commands;
using BookingService.Infrastructure.Caching;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Persistence;
using BookingService.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace BookingService.Infrastructure.Tests;

/// <summary>
/// From "Pagar" to the consumer's queue with every real piece Booking owns: handler, PostgreSQL sale and outbox,
/// relay and RabbitMQ publisher. Requires Docker (PostgreSQL and RabbitMQ containers).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TicketSoldPipelineTests : IClassFixture<RabbitMqFixture>, IAsyncLifetime
{
    private readonly DbContextOptions<BookingDbContext> _options;
    private readonly RabbitMqFixture _rabbitMq;
    private readonly PostgresTestData _data;
    private readonly CopyingPurchaseSessionStore _sessions = new();
    private readonly RabbitMqOptions _exchange = new() { Exchange = $"booking.events.{Guid.NewGuid():N}" };
    private IConnection _connection = null!;
    private IChannel _channel = null!;
    private string _queue = null!;

    public TicketSoldPipelineTests(PostgresFixture postgres, RabbitMqFixture rabbitMq)
    {
        _options = postgres.Options;
        _rabbitMq = rabbitMq;
        _data = new PostgresTestData(_options);

        // The relay reads every pending row, so each test starts from an empty outbox.
        using var db = new BookingDbContext(_options);
        db.OutboxMessages.ExecuteDelete();
    }

    public async Task InitializeAsync()
    {
        _connection = await new ConnectionFactory { Uri = new Uri(_rabbitMq.ConnectionString) }.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();
        await _channel.ExchangeDeclareAsync(_exchange.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        _queue = (await _channel.QueueDeclareAsync()).QueueName;
    }

    public async Task DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
        _data.Dispose();
    }

    [Fact]
    public async Task A_paid_purchase_reaches_the_queue_once_per_ticket_with_the_ticket_data()
    {
        await BindAsync();
        var (eventId, seats) = _data.SeedEvent(3);
        var purchase = await SessionAsync(eventId, seats);
        var result = await PayAsync(purchase.Id, PaymentCallStatus.Succeeded);
        Assert.Equal(ConfirmPurchaseStatus.Purchased, result.Status);

        await using var publisher = Publisher();
        Assert.Equal(3, await Relay(publisher).RelayPendingAsync(CancellationToken.None));

        var delivered = await DrainAsync();
        Assert.Equal(3, delivered.Count);
        var outboxIds = (await PendingOrPublishedAsync()).Select(m => m.Id.ToString()).Order();
        Assert.Equal(outboxIds, delivered.Select(d => d.MessageId).Order());

        foreach (var ticket in result.Purchase!.Tickets)
        {
            var data = delivered.Single(d => d.Data.GetProperty("ticketCode").GetString() == ticket.TicketCode).Data;
            Assert.Equal(purchase.Id, data.GetProperty("purchaseId").GetGuid());
            Assert.Equal(ticket.EventId, data.GetProperty("eventId").GetGuid());
            Assert.Equal(ticket.EventSeatId, data.GetProperty("eventSeatId").GetGuid());
            Assert.Equal(ticket.HolderName, data.GetProperty("holderName").GetString());
            Assert.Equal(ticket.HolderEmail, data.GetProperty("holderEmail").GetString());
            Assert.Equal(ticket.SoldAtUtc, data.GetProperty("soldAt").GetDateTime().ToUniversalTime(), TimeSpan.FromMilliseconds(1));
        }
    }

    [Fact]
    public async Task A_failed_capture_publishes_nothing()
    {
        await BindAsync();
        var (eventId, seats) = _data.SeedEvent(2);
        var purchase = await SessionAsync(eventId, seats);
        Assert.Equal(ConfirmPurchaseStatus.PaymentUnavailable, (await PayAsync(purchase.Id, PaymentCallStatus.Unavailable)).Status);

        await using var publisher = Publisher();
        Assert.Equal(0, await Relay(publisher).RelayPendingAsync(CancellationToken.None));
        Assert.Empty(await DrainAsync());
    }

    [Fact]
    public async Task Two_relays_running_together_deliver_every_message_exactly_once()
    {
        await BindAsync();
        await AddMessagesAsync(50);
        await using var first = Publisher();
        await using var second = Publisher();
        var options = new OutboxOptions { BatchSize = 7 };
        var relays = new[] { Relay(first, options), Relay(second, options) };

        int published;
        do
        {
            var counts = await Task.WhenAll(relays.Select(r => r.RelayPendingAsync(CancellationToken.None)));
            published = counts.Sum();
        }
        while (published > 0);

        var delivered = await DrainAsync();
        Assert.Equal(50, delivered.Count);
        Assert.Equal(50, delivered.Select(d => d.MessageId).Distinct().Count());
        Assert.All(await PendingOrPublishedAsync(), m => Assert.NotNull(m.PublishedAtUtc));
    }

    [Fact]
    public async Task Messages_wait_until_a_consumer_binds_its_queue()
    {
        await AddMessagesAsync(3);
        await using var publisher = Publisher();
        var relay = Relay(publisher);

        Assert.Equal(0, await relay.RelayPendingAsync(CancellationToken.None));
        var waiting = await PendingOrPublishedAsync();
        Assert.All(waiting, m => Assert.Null(m.PublishedAtUtc));
        Assert.Contains("NO_ROUTE", waiting.Single(m => m.Attempts == 1).LastError);

        await BindAsync();
        Assert.Equal(3, await relay.RelayPendingAsync(CancellationToken.None));
        Assert.Equal(3, (await DrainAsync()).Count);
    }

    private Task BindAsync() =>
        _channel.QueueBindAsync(_queue, _exchange.Exchange, PostgresTicketSoldOutbox.TicketSoldType);

    private RabbitMqOutboxPublisher Publisher() => new(_rabbitMq.ConnectionString, _exchange);

    private OutboxRelay Relay(IOutboxPublisher publisher, OutboxOptions? options = null)
    {
        var services = new ServiceCollection()
            .AddScoped(_ => new BookingDbContext(_options))
            .BuildServiceProvider();

        return new OutboxRelay(
            services.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            options ?? new OutboxOptions(),
            NullLogger<OutboxRelay>.Instance);
    }

    private async Task<Domain.Purchases.PurchaseSession> SessionAsync(Guid eventId, IEnumerable<Guid> seats)
    {
        var purchase = _data.Purchase(eventId, seats);
        await _sessions.SaveAsync(purchase, CancellationToken.None);
        return purchase;
    }

    private async Task<ConfirmPurchaseResult> PayAsync(Guid purchaseId, PaymentCallStatus capture)
    {
        await using var db = new BookingDbContext(_options);
        var handler = new ConfirmPurchaseHandler(
            _sessions,
            new PostgresPurchaseSaleRepository(db),
            new InMemoryVenueSeatCatalog(),
            new InMemorySeatLockStore(),
            new NoCache(),
            new PostgresTicketSoldOutbox(db),
            new StubPaymentGateway { CaptureStatus = capture },
            new PaymentOptions(),
            NullLogger<ConfirmPurchaseHandler>.Instance);

        return await handler.HandleAsync(new ConfirmPurchaseCommand(purchaseId, Guid.NewGuid()), CancellationToken.None);
    }

    private async Task AddMessagesAsync(int count)
    {
        var start = DateTime.UtcNow.AddMinutes(-1);
        await using var db = new BookingDbContext(_options);
        for (var i = 0; i < count; i++)
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = PostgresTicketSoldOutbox.TicketSoldType,
                OccurredAtUtc = start.AddMilliseconds(i),
                Payload = $$"""{"ticketCode":"TK-{{i}}"}"""
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<List<OutboxMessage>> PendingOrPublishedAsync()
    {
        await using var db = new BookingDbContext(_options);
        return await db.OutboxMessages.AsNoTracking().ToListAsync();
    }

    private async Task<List<(string MessageId, JsonElement Data)>> DrainAsync()
    {
        var delivered = new List<(string, JsonElement)>();
        while (await _channel.BasicGetAsync(_queue, autoAck: true) is { } message)
        {
            var envelope = JsonDocument.Parse(message.Body).RootElement;
            Assert.Equal(message.BasicProperties.MessageId, envelope.GetProperty("messageId").GetString());
            delivered.Add((message.BasicProperties.MessageId!, envelope.GetProperty("data").Clone()));
        }

        return delivered;
    }
}
