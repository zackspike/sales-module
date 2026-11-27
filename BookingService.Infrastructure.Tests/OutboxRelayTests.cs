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

    [Fact]
    public async Task An_empty_outbox_publishes_nothing()
    {
        var publisher = new FakePublisher();

        Assert.Equal(0, await Relay(publisher).RelayPendingAsync(CancellationToken.None));
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task A_batch_never_exceeds_its_size_and_keeps_the_order_across_cycles()
    {
        var start = DateTime.UtcNow.AddMinutes(-1);
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await AddMessageAsync(start.AddSeconds(i)));
        }

        var publisher = new FakePublisher();
        var relay = Relay(publisher, new OutboxOptions { BatchSize = 2 });

        Assert.Equal(2, await relay.RelayPendingAsync(CancellationToken.None));
        Assert.Equal(2, await relay.RelayPendingAsync(CancellationToken.None));
        Assert.Equal(1, await relay.RelayPendingAsync(CancellationToken.None));
        Assert.Equal(0, await relay.RelayPendingAsync(CancellationToken.None));
        Assert.Equal(ids, publisher.Published);
    }

    [Fact]
    public async Task Stopping_mid_batch_keeps_what_was_already_published()
    {
        var published = await AddMessageAsync(DateTime.UtcNow.AddSeconds(-2));
        var interrupted = await AddMessageAsync(DateTime.UtcNow.AddSeconds(-1));
        using var stopping = new CancellationTokenSource();
        var publisher = new FakePublisher { HangingMessageId = interrupted };
        publisher.OnPublished = _ => stopping.Cancel();

        await Relay(publisher).RelayPendingAsync(stopping.Token);

        var messages = await MessagesAsync();
        Assert.NotNull(messages.Single(m => m.Id == published).PublishedAtUtc);

        // The host stopping is not a failed attempt: the message is simply left for the next run.
        var pending = messages.Single(m => m.Id == interrupted);
        Assert.Null(pending.PublishedAtUtc);
        Assert.Equal(0, pending.Attempts);
        Assert.Null(pending.LastError);
    }

    [Fact]
    public async Task Attempts_accumulate_and_a_long_error_is_truncated()
    {
        var id = await AddMessageAsync(DateTime.UtcNow);
        var publisher = new FakePublisher { FailingMessageId = id, Error = new string('x', 5000) };
        var relay = Relay(publisher);

        await relay.RelayPendingAsync(CancellationToken.None);
        await relay.RelayPendingAsync(CancellationToken.None);
        await relay.RelayPendingAsync(CancellationToken.None);

        var message = Assert.Single(await MessagesAsync());
        Assert.Equal(3, message.Attempts);
        Assert.Equal(1000, message.LastError!.Length);
    }

    [Fact]
    public async Task An_already_published_message_is_never_sent_again()
    {
        await using (var db = new BookingDbContext(_options))
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = PostgresTicketSoldOutbox.TicketSoldType,
                OccurredAtUtc = DateTime.UtcNow,
                Payload = "{}",
                PublishedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var publisher = new FakePublisher();

        Assert.Equal(0, await Relay(publisher).RelayPendingAsync(CancellationToken.None));
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task The_hosted_relay_publishes_on_its_own_survives_a_failed_cycle_and_stops_cleanly()
    {
        var id = await AddMessageAsync(DateTime.UtcNow);
        var publisher = new FakePublisher();
        var contexts = 0;
        var services = new ServiceCollection()
            .AddScoped(_ => Interlocked.Increment(ref contexts) == 1
                ? throw new InvalidOperationException("Database unreachable.")
                : new BookingDbContext(_options))
            .BuildServiceProvider();
        var relay = new OutboxRelay(
            services.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            new OutboxOptions { PollInterval = TimeSpan.FromMilliseconds(50) },
            NullLogger<OutboxRelay>.Instance);

        await relay.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!publisher.Published.Contains(id) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await relay.StopAsync(CancellationToken.None);

        Assert.Contains(id, publisher.Published);
        Assert.True(contexts >= 2, "The relay should retry after the failed cycle.");
        Assert.NotNull(Assert.Single(await MessagesAsync()).PublishedAtUtc);
        Assert.True(relay.ExecuteTask!.IsCompletedSuccessfully);
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

        public string Error { get; init; } = "Broker unavailable.";

        public Action<Guid>? OnPublished { get; set; }

        public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            if (message.Id == HangingMessageId)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (message.Id == FailingMessageId)
            {
                throw new InvalidOperationException(Error);
            }

            lock (Published)
            {
                Published.Add(message.Id);
            }

            OnPublished?.Invoke(message.Id);
        }
    }
}
