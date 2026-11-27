using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class OutboxRelayTests
{
    private readonly DbContextOptions<BookingDbContext> _options;

    public OutboxRelayTests(PostgresFixture postgres)
    {
        _options = postgres.Options;

        // The relay reads every pending row, so each test starts from an empty outbox.
        using var db = new BookingDbContext(_options);
        db.OutboxMessages.ExecuteDelete();
    }

    [Fact]
    public async Task Pending_messages_are_published_oldest_first_and_marked()
    {
        var older = await AddMessageAsync(DateTime.UtcNow.AddSeconds(-2));
        var newer = await AddMessageAsync(DateTime.UtcNow.AddSeconds(-1));
        var publisher = new FakePublisher();

        var published = await Relay(publisher).RelayPendingAsync(CancellationToken.None);

        Assert.Equal(2, published);
        Assert.Equal([older, newer], publisher.Published);
        Assert.All(await MessagesAsync(), m => Assert.NotNull(m.PublishedAtUtc));

        // Already published: nothing is sent again.
        Assert.Equal(0, await Relay(publisher).RelayPendingAsync(CancellationToken.None));
        Assert.Equal(2, publisher.Published.Count);
    }

    [Fact]
    public async Task A_failed_publication_stays_pending_and_ends_the_cycle()
    {
        var failing = await AddMessageAsync(DateTime.UtcNow.AddSeconds(-2));
        var next = await AddMessageAsync(DateTime.UtcNow.AddSeconds(-1));
        var publisher = new FakePublisher { FailingMessageId = failing };

        Assert.Equal(0, await Relay(publisher).RelayPendingAsync(CancellationToken.None));

        var messages = await MessagesAsync();
        Assert.All(messages, m => Assert.Null(m.PublishedAtUtc));
        var failed = messages.Single(m => m.Id == failing);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal("Broker unavailable.", failed.LastError);
        Assert.DoesNotContain(next, publisher.Published);

        // Next cycle, with the broker back: both are published and the error is cleared.
        publisher.FailingMessageId = null;
        Assert.Equal(2, await Relay(publisher).RelayPendingAsync(CancellationToken.None));
        Assert.All(await MessagesAsync(), m =>
        {
            Assert.NotNull(m.PublishedAtUtc);
            Assert.Null(m.LastError);
        });
    }

    [Fact]
    public async Task A_publication_that_times_out_stays_pending()
    {
        var hanging = await AddMessageAsync(DateTime.UtcNow);
        var publisher = new FakePublisher { HangingMessageId = hanging };

        var relay = Relay(publisher, new OutboxOptions { PublishTimeout = TimeSpan.FromMilliseconds(200) });

        Assert.Equal(0, await relay.RelayPendingAsync(CancellationToken.None));
        var message = Assert.Single(await MessagesAsync());
        Assert.Null(message.PublishedAtUtc);
        Assert.Equal(1, message.Attempts);
        Assert.Contains("timed out", message.LastError);
    }

    [Fact]
    public async Task A_batch_locked_by_another_relay_is_skipped()
    {
        await AddMessageAsync(DateTime.UtcNow);

        // Another instance is publishing this row right now.
        await using var other = new BookingDbContext(_options);
        await using var lockingTransaction = await other.Database.BeginTransactionAsync();
        await other.OutboxMessages
            .FromSql($"""SELECT * FROM outbox_messages FOR UPDATE""")
            .ToListAsync();

        var publisher = new FakePublisher();
        Assert.Equal(0, await Relay(publisher).RelayPendingAsync(CancellationToken.None));
        Assert.Empty(publisher.Published);
    }

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

    private async Task<Guid> AddMessageAsync(DateTime occurredAtUtc)
    {
        await using var db = new BookingDbContext(_options);
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = PostgresTicketSoldOutbox.TicketSoldType,
            OccurredAtUtc = occurredAtUtc,
            Payload = """{"ticketCode":"TK-test"}"""
        };
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private async Task<List<OutboxMessage>> MessagesAsync()
    {
        await using var db = new BookingDbContext(_options);
        return await db.OutboxMessages.AsNoTracking().ToListAsync();
    }

    private sealed class FakePublisher : IOutboxPublisher
    {
        public List<Guid> Published { get; } = [];

        public Guid? FailingMessageId { get; set; }

        public Guid? HangingMessageId { get; init; }

        public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            if (message.Id == HangingMessageId)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (message.Id == FailingMessageId)
            {
                throw new InvalidOperationException("Broker unavailable.");
            }

            Published.Add(message.Id);
        }
    }
}
