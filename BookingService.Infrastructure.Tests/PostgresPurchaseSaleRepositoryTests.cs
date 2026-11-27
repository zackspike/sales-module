using System.Text.Json;
using BookingService.Application.Abstractions;
using BookingService.Domain.Events;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PostgresPurchaseSaleRepositoryTests : IDisposable
{
    private static readonly PaymentCaptureResult Captured = new(PaymentCallStatus.Succeeded);

    private readonly DbContextOptions<BookingDbContext> _options;
    private readonly List<Guid> _seededEventIds = [];
    private readonly List<Guid> _purchaseIds = [];

    public PostgresPurchaseSaleRepositoryTests(PostgresFixture postgres)
    {
        _options = postgres.Options;
    }

    [Fact]
    public async Task A_captured_sale_sells_every_held_seat_and_leaves_one_outbox_message_per_ticket()
    {
        var (eventId, seats) = SeedEvent(2);
        var purchase = Purchase(eventId, seats);

        var outcome = await SellAsync(Guid.NewGuid(), purchase, _ => Task.FromResult(Captured));

        Assert.Equal(PurchaseSaleStatus.Sold, outcome.Status);
        Assert.Equal(seats.Order(), outcome.Sales.Select(s => s.Seat.Id).Order());
        Assert.Equal(seats.Order(), (await SoldSeatsAsync(eventId)).Order());

        var messages = await OutboxMessagesOfAsync(purchase.Id);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Equal("ticketSold", m.Type));
        Assert.All(messages, m => Assert.Null(m.PublishedAtUtc));

        var data = JsonDocument.Parse(messages[0].Payload).RootElement;
        var sale = outcome.Sales.Single(s => s.Ticket.TicketCode == data.GetProperty("ticketCode").GetString());
        Assert.Equal(purchase.Id, data.GetProperty("purchaseId").GetGuid());
        Assert.Equal(eventId, data.GetProperty("eventId").GetGuid());
        Assert.Equal(sale.Seat.Id, data.GetProperty("eventSeatId").GetGuid());
        Assert.Equal("Jane Doe", data.GetProperty("holderName").GetString());
        Assert.Equal("jane.doe@example.com", data.GetProperty("holderEmail").GetString());
        Assert.True(data.TryGetProperty("soldAt", out _));
    }

    [Theory]
    [InlineData(PaymentCallStatus.Unavailable)]
    [InlineData(PaymentCallStatus.TimedOut)]
    [InlineData(PaymentCallStatus.NotAuthorized)]
    public async Task A_capture_that_fails_rolls_back_the_sale_and_the_outbox(PaymentCallStatus status)
    {
        var (eventId, seats) = SeedEvent(2);
        var purchase = Purchase(eventId, seats);
        var key = Guid.NewGuid();

        var outcome = await SellAsync(key, purchase, _ => Task.FromResult(new PaymentCaptureResult(status)));

        Assert.Equal(PurchaseSaleStatus.NotCaptured, outcome.Status);
        Assert.Equal(status, outcome.CaptureStatus);
        Assert.Empty(await SoldSeatsAsync(eventId));
        Assert.Empty(await OutboxMessagesOfAsync(purchase.Id));

        // The key is free again: the same "Pagar" can be retried.
        await using var db = new BookingDbContext(_options);
        Assert.Null(await new PostgresPurchaseSaleRepository(db).FindByIdempotencyKeyAsync(key, CancellationToken.None));
        Assert.Equal(PurchaseSaleStatus.Sold, (await SellAsync(key, purchase, _ => Task.FromResult(Captured))).Status);
    }

    [Fact]
    public async Task A_capture_that_throws_rolls_back_and_propagates()
    {
        var (eventId, seats) = SeedEvent(1);
        var purchase = Purchase(eventId, seats);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            SellAsync(Guid.NewGuid(), purchase, _ => throw new HttpRequestException("Payment is down.")));

        Assert.Empty(await SoldSeatsAsync(eventId));
        Assert.Empty(await OutboxMessagesOfAsync(purchase.Id));
    }

    [Fact]
    public async Task The_commit_does_not_depend_on_the_request_once_the_payment_is_captured()
    {
        var (eventId, seats) = SeedEvent(1);
        var purchase = Purchase(eventId, seats);
        using var request = new CancellationTokenSource();

        var outcome = await SellAsync(Guid.NewGuid(), purchase, _ =>
        {
            // The fan closed the tab while Payment was capturing.
            request.Cancel();
            return Task.FromResult(Captured);
        }, request.Token);

        Assert.Equal(PurchaseSaleStatus.Sold, outcome.Status);
        Assert.Single(await SoldSeatsAsync(eventId));
        Assert.Single(await OutboxMessagesOfAsync(purchase.Id));
    }

    [Fact]
    public async Task The_same_key_replays_the_sale_without_capturing_again()
    {
        var (eventId, seats) = SeedEvent(2);
        var purchase = Purchase(eventId, seats);
        var key = Guid.NewGuid();
        var first = await SellAsync(key, purchase, _ => Task.FromResult(Captured));

        var captures = 0;
        var replay = await SellAsync(key, purchase, _ =>
        {
            captures++;
            return Task.FromResult(Captured);
        });

        Assert.Equal(PurchaseSaleStatus.Replayed, replay.Status);
        Assert.Equal(0, captures);
        Assert.Equal(
            first.Sales.Select(s => s.Ticket.TicketCode).Order(),
            replay.Sales.Select(s => s.Ticket.TicketCode).Order());
        Assert.Equal(2, (await OutboxMessagesOfAsync(purchase.Id)).Count);

        await using var db = new BookingDbContext(_options);
        var paid = await new PostgresPurchaseSaleRepository(db).FindByIdempotencyKeyAsync(key, CancellationToken.None);
        Assert.NotNull(paid);
        Assert.Equal(purchase.Id, paid.PurchaseId);
        Assert.All(paid.Sales, s => Assert.Equal(EventSeatStatus.Sold, s.Seat.Status));
        Assert.Equal(seats.Order(), paid.Sales.Select(s => s.Seat.Id).Order());
    }

    [Fact]
    public async Task A_key_used_by_another_purchase_is_a_conflict()
    {
        var (eventId, seats) = SeedEvent(2);
        var key = Guid.NewGuid();
        await SellAsync(key, Purchase(eventId, seats[..1]), _ => Task.FromResult(Captured));

        var other = Purchase(eventId, seats[1..]);
        var outcome = await SellAsync(key, other, _ => Task.FromResult(Captured));

        Assert.Equal(PurchaseSaleStatus.IdempotencyKeyConflict, outcome.Status);
        Assert.Single(await SoldSeatsAsync(eventId));
    }

    [Fact]
    public async Task Every_missing_or_sold_seat_is_reported_and_nothing_is_sold()
    {
        var (eventId, seats) = SeedEvent(3);
        await SellAsync(Guid.NewGuid(), Purchase(eventId, seats[..1]), _ => Task.FromResult(Captured));
        var (_, otherEventSeats) = SeedEvent(1);

        // seats[0] is sold, seats[1] is free, and the last one belongs to another event.
        var purchase = Purchase(eventId, [seats[0], seats[1], otherEventSeats[0]]);
        var captures = 0;
        var outcome = await SellAsync(Guid.NewGuid(), purchase, _ =>
        {
            captures++;
            return Task.FromResult(Captured);
        });

        Assert.Equal(PurchaseSaleStatus.SeatsUnavailable, outcome.Status);
        Assert.Equal(0, captures);
        Assert.Equal(
            [
                new FailedEventSeat(seats[0], SeatSaleFailure.AlreadySold),
                new FailedEventSeat(otherEventSeats[0], SeatSaleFailure.NotFound)
            ],
            outcome.FailedSeats);
        Assert.Equal([seats[0]], await SoldSeatsAsync(eventId));
        Assert.Empty(await OutboxMessagesOfAsync(purchase.Id));
    }

    [Fact]
    public async Task Concurrent_purchases_of_the_same_seat_sell_it_once()
    {
        var (eventId, seats) = SeedEvent(1);
        var purchases = Enumerable.Range(0, 10).Select(_ => Purchase(eventId, seats)).ToList();

        var outcomes = await Task.WhenAll(purchases.Select(purchase =>
            SellAsync(Guid.NewGuid(), purchase, async _ =>
            {
                await Task.Delay(50);
                return Captured;
            })));

        Assert.Single(outcomes, o => o.Status == PurchaseSaleStatus.Sold);
        Assert.All(
            outcomes.Where(o => o.Status != PurchaseSaleStatus.Sold),
            o =>
            {
                Assert.Equal(PurchaseSaleStatus.SeatsUnavailable, o.Status);
                Assert.Equal([new FailedEventSeat(seats[0], SeatSaleFailure.AlreadySold)], o.FailedSeats);
            });

        var messages = new List<OutboxMessage>();
        foreach (var purchase in purchases)
        {
            messages.AddRange(await OutboxMessagesOfAsync(purchase.Id));
        }

        Assert.Single(messages);
    }

    [Fact]
    public async Task Concurrent_retries_with_the_same_key_sell_and_capture_once()
    {
        var (eventId, seats) = SeedEvent(2);
        var purchase = Purchase(eventId, seats);
        var key = Guid.NewGuid();
        var captures = 0;

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            SellAsync(key, purchase, async _ =>
            {
                Interlocked.Increment(ref captures);
                await Task.Delay(50);
                return Captured;
            })));

        Assert.Equal(1, captures);
        Assert.Single(outcomes, o => o.Status == PurchaseSaleStatus.Sold);
        Assert.Equal(4, outcomes.Count(o => o.Status == PurchaseSaleStatus.Replayed));
        Assert.Equal(2, (await OutboxMessagesOfAsync(purchase.Id)).Count);
    }

    [Fact]
    public async Task A_purchase_without_held_seats_is_rejected_before_anything_is_stored_or_captured()
    {
        var (eventId, _) = SeedEvent(1);
        var purchase = Purchase(eventId, []);
        var key = Guid.NewGuid();
        var captures = 0;

        await Assert.ThrowsAsync<ArgumentException>(() => SellAsync(key, purchase, _ =>
        {
            captures++;
            return Task.FromResult(Captured);
        }));

        Assert.Equal(0, captures);
        await using var db = new BookingDbContext(_options);
        Assert.Null(await new PostgresPurchaseSaleRepository(db).FindByIdempotencyKeyAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task A_sell_rule_that_throws_rolls_back_and_frees_the_key()
    {
        var (eventId, seats) = SeedEvent(2);
        var purchase = Purchase(eventId, seats);
        var key = Guid.NewGuid();

        await using (var db = new BookingDbContext(_options))
        {
            // The session ran out between the handler's check and the sale.
            var expiredAt = purchase.ExpiresAtUtc;
            await Assert.ThrowsAsync<PurchaseExpiredException>(() =>
                new PostgresPurchaseSaleRepository(db).SellOnceAsync(
                    key,
                    purchase,
                    seat => seat.Sell(purchase, expiredAt),
                    (_, _) => Task.FromResult(Captured),
                    CancellationToken.None));
        }

        Assert.Empty(await SoldSeatsAsync(eventId));
        Assert.Empty(await OutboxMessagesOfAsync(purchase.Id));
        Assert.Equal(PurchaseSaleStatus.Sold, (await SellAsync(key, purchase, _ => Task.FromResult(Captured))).Status);
    }

    [Fact]
    public async Task A_key_whose_capture_failed_can_be_used_by_another_purchase()
    {
        var (eventId, seats) = SeedEvent(2);
        var key = Guid.NewGuid();
        var failed = await SellAsync(
            key,
            Purchase(eventId, seats[..1]),
            _ => Task.FromResult(new PaymentCaptureResult(PaymentCallStatus.Unavailable)));

        var other = Purchase(eventId, seats[1..]);
        var outcome = await SellAsync(key, other, _ => Task.FromResult(Captured));

        Assert.Equal(PurchaseSaleStatus.NotCaptured, failed.Status);
        Assert.Equal(PurchaseSaleStatus.Sold, outcome.Status);
        Assert.Equal([seats[1]], await SoldSeatsAsync(eventId));
    }

    [Fact]
    public async Task Each_key_of_a_purchase_that_pays_twice_replays_only_its_own_tickets()
    {
        var (eventId, seats) = SeedEvent(2);
        var purchase = Purchase(eventId, seats[..1]);
        var firstKey = Guid.NewGuid();
        var first = await SellAsync(firstKey, purchase, _ => Task.FromResult(Captured));

        // Decision 22: the paid seat leaves the session, which then holds and pays another one.
        purchase.Release(seats[0]);
        purchase.Hold(eventId, seats[1], maxSeats: 10, DateTime.UtcNow);
        var secondKey = Guid.NewGuid();
        var second = await SellAsync(secondKey, purchase, _ => Task.FromResult(Captured));

        Assert.Equal(PurchaseSaleStatus.Sold, second.Status);
        await using var db = new BookingDbContext(_options);
        var repository = new PostgresPurchaseSaleRepository(db);
        var firstReplay = await repository.FindByIdempotencyKeyAsync(firstKey, CancellationToken.None);
        var secondReplay = await repository.FindByIdempotencyKeyAsync(secondKey, CancellationToken.None);

        Assert.Equal([first.Sales[0].Ticket.TicketCode], firstReplay!.Sales.Select(s => s.Ticket.TicketCode));
        Assert.Equal([second.Sales[0].Ticket.TicketCode], secondReplay!.Sales.Select(s => s.Ticket.TicketCode));
        Assert.Equal(2, (await OutboxMessagesOfAsync(purchase.Id)).Count);
    }

    [Fact]
    public async Task Concurrent_purchases_with_overlapping_seats_never_split_a_purchase()
    {
        var (eventId, seats) = SeedEvent(3);
        var a = Purchase(eventId, [seats[0], seats[1]]);
        var b = Purchase(eventId, [seats[1], seats[2]]);

        var outcomes = await Task.WhenAll(new[] { a, b }.Select(purchase =>
            SellAsync(Guid.NewGuid(), purchase, async _ =>
            {
                await Task.Delay(100);
                return Captured;
            })));

        var winner = Assert.Single(outcomes, o => o.Status == PurchaseSaleStatus.Sold);
        var loser = Assert.Single(outcomes, o => o.Status == PurchaseSaleStatus.SeatsUnavailable);
        Assert.Equal([new FailedEventSeat(seats[1], SeatSaleFailure.AlreadySold)], loser.FailedSeats);

        // All or nothing: only the winner's two seats are sold, never the loser's free seat.
        var sold = await SoldSeatsAsync(eventId);
        Assert.Equal(winner.Sales.Select(s => s.Seat.Id).Order(), sold.Order());
        Assert.Equal(2, sold.Count);
    }

    [Fact]
    public async Task Holder_names_with_quotes_accents_and_emoji_survive_the_jsonb_payload()
    {
        const string name = "José \"Pepe\" Ñúñez \\ 🎟️";
        const string email = "josé.o'neil+tickets@example.com";
        var (eventId, seats) = SeedEvent(1);
        var purchase = PurchaseSession.Start(eventId, name, email, DateTime.UtcNow, TimeSpan.FromMinutes(10));
        purchase.Hold(eventId, seats[0], maxSeats: 10, DateTime.UtcNow);
        _purchaseIds.Add(purchase.Id);

        await SellAsync(Guid.NewGuid(), purchase, _ => Task.FromResult(Captured));

        var data = JsonDocument.Parse(Assert.Single(await OutboxMessagesOfAsync(purchase.Id)).Payload).RootElement;
        Assert.Equal(name, data.GetProperty("holderName").GetString());
        Assert.Equal(email, data.GetProperty("holderEmail").GetString());
    }

    [Fact]
    public async Task A_sale_time_without_kind_is_stored_and_serialized_as_utc()
    {
        var soldAt = new DateTime(2026, 11, 16, 8, 7, 30, DateTimeKind.Unspecified);
        var purchaseId = Guid.NewGuid();
        _purchaseIds.Add(purchaseId);

        await using (var db = new BookingDbContext(_options))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await new PostgresTicketSoldOutbox(db).AddAsync(
                [new TicketSoldEvent(purchaseId, "TK-kind", Guid.NewGuid(), Guid.NewGuid(), "Jane", "j@example.com", soldAt)],
                CancellationToken.None);
            await transaction.CommitAsync();
        }

        var message = Assert.Single(await OutboxMessagesOfAsync(purchaseId));
        Assert.Equal(DateTimeKind.Utc, message.OccurredAtUtc.Kind);
        Assert.Equal(DateTime.SpecifyKind(soldAt, DateTimeKind.Utc), message.OccurredAtUtc);
        Assert.Equal(
            "2026-11-16T08:07:30Z",
            JsonDocument.Parse(message.Payload).RootElement.GetProperty("soldAt").GetString());
    }

    [Fact]
    public async Task An_empty_batch_of_messages_writes_nothing()
    {
        await using var db = new BookingDbContext(_options);
        var before = await db.OutboxMessages.CountAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        await new PostgresTicketSoldOutbox(db).AddAsync([], CancellationToken.None);
        await transaction.CommitAsync();

        Assert.Equal(before, await db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task The_same_context_stays_usable_after_a_rollback()
    {
        var (eventId, seats) = SeedEvent(1);
        var purchase = Purchase(eventId, seats);
        var key = Guid.NewGuid();
        await using var db = new BookingDbContext(_options);
        var repository = new PostgresPurchaseSaleRepository(db);
        var nowUtc = DateTime.UtcNow;

        var failed = await repository.SellOnceAsync(
            key,
            purchase,
            seat => seat.Sell(purchase, nowUtc),
            (_, _) => Task.FromResult(new PaymentCaptureResult(PaymentCallStatus.TimedOut)),
            CancellationToken.None);
        var retried = await repository.SellOnceAsync(
            key,
            purchase,
            seat => seat.Sell(purchase, nowUtc),
            (_, _) => Task.FromResult(Captured),
            CancellationToken.None);

        Assert.Equal(PurchaseSaleStatus.NotCaptured, failed.Status);
        Assert.Equal(PurchaseSaleStatus.Sold, retried.Status);
        Assert.Equal(PurchaseSaleStatus.Replayed, (await repository.SellOnceAsync(
            key,
            purchase,
            seat => seat.Sell(purchase, nowUtc),
            (_, _) => Task.FromResult(Captured),
            CancellationToken.None)).Status);
    }

    [Fact]
    public async Task The_outbox_refuses_to_write_outside_a_transaction()
    {
        await using var db = new BookingDbContext(_options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostgresTicketSoldOutbox(db).AddAsync(
                [new TicketSoldEvent(Guid.NewGuid(), "TK-x", Guid.NewGuid(), Guid.NewGuid(), "Jane", "j@example.com", DateTime.UtcNow)],
                CancellationToken.None));
    }

    /// <summary>
    /// Runs the sale the way <c>ConfirmPurchaseHandler</c> does: the capture callback writes the outbox first and
    /// then calls <paramref name="capture"/>, all through one context.
    /// </summary>
    private async Task<PurchaseSaleOutcome> SellAsync(
        Guid key,
        PurchaseSession purchase,
        Func<CancellationToken, Task<PaymentCaptureResult>> capture,
        CancellationToken cancellationToken = default)
    {
        await using var db = new BookingDbContext(_options);
        var outbox = new PostgresTicketSoldOutbox(db);
        var nowUtc = DateTime.UtcNow;

        return await new PostgresPurchaseSaleRepository(db).SellOnceAsync(
            key,
            purchase,
            seat => seat.Sell(purchase, nowUtc),
            async (sales, transactionCancellationToken) =>
            {
                await outbox.AddAsync(
                    sales.Select(sale => new TicketSoldEvent(
                        purchase.Id,
                        sale.Ticket.TicketCode,
                        sale.Seat.EventId,
                        sale.Seat.Id,
                        sale.Ticket.HolderName,
                        sale.Ticket.HolderEmail,
                        sale.Ticket.SoldAtUtc)).ToList(),
                    transactionCancellationToken);
                return await capture(CancellationToken.None);
            },
            cancellationToken);
    }

    /// <summary>
    /// A purchase of <paramref name="eventId"/> holding <paramref name="seats"/>. The domain does not check that a
    /// seat id belongs to the event (the use case does), so a seat of another event can be held to test NotFound.
    /// </summary>
    private PurchaseSession Purchase(Guid eventId, IEnumerable<Guid> seats)
    {
        var nowUtc = DateTime.UtcNow;
        var purchase = PurchaseSession.Start(eventId, "Jane Doe", "jane.doe@example.com", nowUtc, TimeSpan.FromMinutes(10));
        foreach (var seat in seats)
        {
            purchase.Hold(eventId, seat, maxSeats: 10, nowUtc);
        }

        _purchaseIds.Add(purchase.Id);
        return purchase;
    }

    private async Task<List<Guid>> SoldSeatsAsync(Guid eventId)
    {
        await using var db = new BookingDbContext(_options);
        return await db.EventSeats
            .Where(s => s.EventId == eventId && s.Status == EventSeatStatus.Sold)
            .Select(s => s.Id)
            .ToListAsync();
    }

    private async Task<List<OutboxMessage>> OutboxMessagesOfAsync(Guid purchaseId)
    {
        await using var db = new BookingDbContext(_options);
        var all = await db.OutboxMessages.AsNoTracking().ToListAsync();
        return all.Where(m => m.Payload.Contains(purchaseId.ToString())).ToList();
    }

    private (Guid EventId, Guid[] Seats) SeedEvent(int totalSeats)
    {
        var @event = new Event(
            Guid.NewGuid(),
            "Outbox test",
            Guid.NewGuid(),
            "Test Artist",
            Guid.NewGuid(),
            "Test Venue",
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(1));

        var venueSeats = InMemoryVenueSeatCatalog.DefaultVenueSeats.Take(totalSeats).ToList();
        var eventSeats = EventInventoryFactory.GenerateEventSeats(@event.Id, venueSeats);

        using var db = new BookingDbContext(_options);
        db.Events.Add(@event);
        db.EventSeats.AddRange(eventSeats);
        db.SaveChanges();
        _seededEventIds.Add(@event.Id);

        return (@event.Id, eventSeats.Select(s => s.Id).ToArray());
    }

    // Removes only the rows this test created, children first because the foreign keys are Restrict.
    public void Dispose()
    {
        using var db = new BookingDbContext(_options);
        var purchaseIds = _purchaseIds.Select(id => id.ToString()).ToList();
        db.OutboxMessages.RemoveRange(
            db.OutboxMessages.AsEnumerable().Where(m => purchaseIds.Any(m.Payload.Contains)));
        db.SaveChanges();

        db.Tickets
            .Where(t => db.EventSeats.Any(s => s.Id == t.EventSeatId && _seededEventIds.Contains(s.EventId)))
            .ExecuteDelete();
        db.PurchaseSales.Where(s => _purchaseIds.Contains(s.PurchaseId)).ExecuteDelete();
        db.EventSeats.Where(s => _seededEventIds.Contains(s.EventId)).ExecuteDelete();
        db.Events.Where(e => _seededEventIds.Contains(e.Id)).ExecuteDelete();
    }
}
