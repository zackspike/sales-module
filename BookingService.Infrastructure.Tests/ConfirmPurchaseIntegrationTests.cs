using System.Text.Json;
using BookingService.Application.Abstractions;
using BookingService.Application.Purchases;
using BookingService.Application.Purchases.Commands;
using BookingService.Infrastructure.Caching;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Persistence;
using BookingService.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Infrastructure.Tests;

/// <summary>
/// "Pagar" end to end below the Api: the real <see cref="ConfirmPurchaseHandler"/> on the real PostgreSQL sale
/// repository and outbox. Only the session store and Payment are doubles (T4 and T5 implement them).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConfirmPurchaseIntegrationTests : IDisposable
{
    private readonly DbContextOptions<BookingDbContext> _options;
    private readonly PostgresTestData _data;
    private readonly CopyingPurchaseSessionStore _sessions = new();
    private readonly StubPaymentGateway _payment = new();

    public ConfirmPurchaseIntegrationTests(PostgresFixture postgres)
    {
        _options = postgres.Options;
        _data = new PostgresTestData(_options);
    }

    [Fact]
    public async Task A_paid_purchase_issues_findable_tickets_one_outbox_message_each_and_frees_the_session()
    {
        var (eventId, seats) = _data.SeedEvent(2);
        var purchase = await SessionAsync(eventId, seats);

        var result = await PayAsync(purchase.Id, Guid.NewGuid());

        Assert.Equal(ConfirmPurchaseStatus.Purchased, result.Status);
        var tickets = result.Purchase!.Tickets;
        Assert.Equal(seats.Order(), tickets.Select(t => t.EventSeatId).Order());
        Assert.All(tickets, t => Assert.False(string.IsNullOrWhiteSpace(t.SeatNumber)));
        Assert.Equal(1, _payment.Captures);

        // GET /tickets/{code} reads through the MVP-02 repository: tickets of the new flow must be visible to it.
        await using var db = new BookingDbContext(_options);
        foreach (var ticket in tickets)
        {
            var sale = await new PostgresEventSeatRepository(db).GetSaleByTicketCodeAsync(ticket.TicketCode, CancellationToken.None);
            Assert.NotNull(sale);
            Assert.Equal(ticket.EventSeatId, sale.Seat.Id);
        }

        var payloads = (await OutboxOfAsync(purchase.Id)).Select(m => JsonDocument.Parse(m.Payload).RootElement).ToList();
        Assert.Equal(
            tickets.Select(t => t.TicketCode).Order(),
            payloads.Select(p => p.GetProperty("ticketCode").GetString()!).Order());

        var session = await _sessions.GetAsync(purchase.Id, CancellationToken.None);
        Assert.Empty(session!.HeldEventSeatIds);
        Assert.Null(session.PaymentToken);
    }

    [Theory]
    [InlineData(PaymentCallStatus.Unavailable, ConfirmPurchaseStatus.PaymentUnavailable)]
    [InlineData(PaymentCallStatus.TimedOut, ConfirmPurchaseStatus.PaymentTimeout)]
    [InlineData(PaymentCallStatus.NotAuthorized, ConfirmPurchaseStatus.PaymentNotAuthorized)]
    public async Task A_failed_capture_sells_nothing_and_keeps_the_session(
        PaymentCallStatus capture,
        ConfirmPurchaseStatus expected)
    {
        var (eventId, seats) = _data.SeedEvent(2);
        var purchase = await SessionAsync(eventId, seats);
        _payment.CaptureStatus = capture;

        var result = await PayAsync(purchase.Id, Guid.NewGuid());

        Assert.Equal(expected, result.Status);
        Assert.Empty(await _data.SoldSeatsAsync(eventId));
        Assert.Empty(await OutboxOfAsync(purchase.Id));
        var session = await _sessions.GetAsync(purchase.Id, CancellationToken.None);
        Assert.Equal(seats.Order(), session!.HeldEventSeatIds.Order());
        Assert.Equal("pay_tok_test", session.PaymentToken);
    }

    [Fact]
    public async Task A_retry_after_a_failed_capture_with_the_same_key_sells_once()
    {
        var (eventId, seats) = _data.SeedEvent(1);
        var purchase = await SessionAsync(eventId, seats);
        var key = Guid.NewGuid();
        _payment.CaptureStatus = PaymentCallStatus.TimedOut;
        Assert.Equal(ConfirmPurchaseStatus.PaymentTimeout, (await PayAsync(purchase.Id, key)).Status);

        _payment.CaptureStatus = PaymentCallStatus.Succeeded;
        var retry = await PayAsync(purchase.Id, key);

        Assert.Equal(ConfirmPurchaseStatus.Purchased, retry.Status);
        Assert.Single(await _data.SoldSeatsAsync(eventId));
        Assert.Single(await OutboxOfAsync(purchase.Id));
    }

    [Fact]
    public async Task A_replay_returns_the_same_tickets_without_charging_again()
    {
        var (eventId, seats) = _data.SeedEvent(2);
        var purchase = await SessionAsync(eventId, seats);
        var key = Guid.NewGuid();
        var first = await PayAsync(purchase.Id, key);

        var replay = await PayAsync(purchase.Id, key);

        Assert.Equal(ConfirmPurchaseStatus.Replayed, replay.Status);
        Assert.Equal(1, _payment.Captures);
        var original = first.Purchase!.Tickets.OrderBy(t => t.TicketCode).ToList();
        var replayed = replay.Purchase!.Tickets.OrderBy(t => t.TicketCode).ToList();
        Assert.Equal(original.Count, replayed.Count);
        for (var i = 0; i < original.Count; i++)
        {
            // Known gap outside T6: the handler stamps the sale with 100 ns precision and the replay reads it back
            // from PostgreSQL (microseconds), so the two answers can differ below one microsecond.
            Assert.Equal(original[i] with { SoldAtUtc = default }, replayed[i] with { SoldAtUtc = default });
            Assert.Equal(original[i].SoldAtUtc, replayed[i].SoldAtUtc, TimeSpan.FromTicks(TimeSpan.TicksPerMicrosecond - 1));
        }

        Assert.Equal(2, (await OutboxOfAsync(purchase.Id)).Count);
    }

    [Fact]
    public async Task The_published_sale_time_is_exactly_the_stored_one()
    {
        var (eventId, seats) = _data.SeedEvent(1);
        var purchase = await SessionAsync(eventId, seats);
        var ticket = Assert.Single((await PayAsync(purchase.Id, Guid.NewGuid())).Purchase!.Tickets);

        await using var db = new BookingDbContext(_options);
        var stored = await new PostgresEventSeatRepository(db).GetSaleByTicketCodeAsync(ticket.TicketCode, CancellationToken.None);
        var message = Assert.Single(await OutboxOfAsync(purchase.Id));
        var soldAt = JsonDocument.Parse(message.Payload).RootElement.GetProperty("soldAt").GetDateTime().ToUniversalTime();

        Assert.Equal(stored!.Ticket.SoldAtUtc, soldAt);
        Assert.Equal(stored.Ticket.SoldAtUtc, message.OccurredAtUtc);
    }

    [Fact]
    public async Task A_seat_sold_to_someone_else_fails_the_whole_purchase_without_calling_Payment()
    {
        var (eventId, seats) = _data.SeedEvent(2);
        var rival = await SessionAsync(eventId, seats[..1]);
        Assert.Equal(ConfirmPurchaseStatus.Purchased, (await PayAsync(rival.Id, Guid.NewGuid())).Status);

        var purchase = await SessionAsync(eventId, seats);
        var result = await PayAsync(purchase.Id, Guid.NewGuid());

        Assert.Equal(ConfirmPurchaseStatus.AlreadySold, result.Status);
        Assert.Equal([new FailedEventSeat(seats[0], SeatSaleFailure.AlreadySold)], result.FailedSeats);
        Assert.Equal(1, _payment.Captures);
        Assert.Equal([seats[0]], await _data.SoldSeatsAsync(eventId));
        Assert.Empty(await OutboxOfAsync(purchase.Id));
    }

    [Fact]
    public async Task A_double_click_sells_and_charges_once()
    {
        var (eventId, seats) = _data.SeedEvent(2);
        var purchase = await SessionAsync(eventId, seats);
        var key = Guid.NewGuid();
        var slowPayment = new StubPaymentGateway { CaptureDelay = TimeSpan.FromMilliseconds(200) };

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PayAsync(purchase.Id, key, slowPayment)));

        Assert.Equal(1, slowPayment.Captures);
        Assert.Single(results, r => r.Status == ConfirmPurchaseStatus.Purchased);
        Assert.Equal(4, results.Count(r => r.Status == ConfirmPurchaseStatus.Replayed));
        Assert.All(results, r => Assert.Equal(2, r.Purchase!.Tickets.Count));
        Assert.Equal(2, (await OutboxOfAsync(purchase.Id)).Count);
    }

    [Fact]
    public async Task The_longest_holder_name_and_email_the_domain_allows_fit_everywhere()
    {
        var name = new string('Ñ', 64);
        var email = new string('a', 254 - "@example.com".Length) + "@example.com";
        var (eventId, seats) = _data.SeedEvent(1);
        var purchase = _data.Purchase(eventId, seats, name, email);
        await _sessions.SaveAsync(purchase, CancellationToken.None);

        var result = await PayAsync(purchase.Id, Guid.NewGuid());

        Assert.Equal(ConfirmPurchaseStatus.Purchased, result.Status);
        var data = JsonDocument.Parse(Assert.Single(await OutboxOfAsync(purchase.Id)).Payload).RootElement;
        Assert.Equal(name, data.GetProperty("holderName").GetString());
        Assert.Equal(email, data.GetProperty("holderEmail").GetString());
    }

    public void Dispose() => _data.Dispose();

    private async Task<Domain.Purchases.PurchaseSession> SessionAsync(Guid eventId, IEnumerable<Guid> seats)
    {
        var purchase = _data.Purchase(eventId, seats);
        await _sessions.SaveAsync(purchase, CancellationToken.None);
        return purchase;
    }

    /// <summary>One request: its own scope, so its own <see cref="BookingDbContext"/>, as in the Api.</summary>
    private async Task<ConfirmPurchaseResult> PayAsync(Guid purchaseId, Guid key, IPaymentGateway? payment = null)
    {
        await using var db = new BookingDbContext(_options);
        var handler = new ConfirmPurchaseHandler(
            _sessions,
            new PostgresPurchaseSaleRepository(db),
            new InMemoryVenueSeatCatalog(),
            new InMemorySeatLockStore(),
            new NoCache(),
            new PostgresTicketSoldOutbox(db),
            payment ?? _payment,
            new PaymentOptions(),
            NullLogger<ConfirmPurchaseHandler>.Instance);

        return await handler.HandleAsync(new ConfirmPurchaseCommand(purchaseId, key), CancellationToken.None);
    }

    private async Task<List<OutboxMessage>> OutboxOfAsync(Guid purchaseId)
    {
        await using var db = new BookingDbContext(_options);
        var all = await db.OutboxMessages.AsNoTracking().ToListAsync();
        return all.Where(m => m.Payload.Contains(purchaseId.ToString())).ToList();
    }
}
