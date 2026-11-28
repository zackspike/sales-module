using BookingService.Application.Abstractions;
using BookingService.Domain.EventSeats;
using BookingService.Infrastructure.Persistence;
using BookingService.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Tests;

/// <summary>
/// The MVP-02 sale (one key per ticket in tickets.IdempotencyKey) stays live until T7 migrates the Api, so both
/// flows write the same tables. Neither may break the other's guarantees.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LegacyAndPurchaseSaleCoexistenceTests : IDisposable
{
    private static readonly PaymentCaptureResult Captured = new(PaymentCallStatus.Succeeded);

    private readonly DbContextOptions<BookingDbContext> _options;
    private readonly PostgresTestData _data;

    public LegacyAndPurchaseSaleCoexistenceTests(PostgresFixture postgres)
    {
        _options = postgres.Options;
        _data = new PostgresTestData(_options);
    }

    [Fact]
    public async Task Many_new_sales_leave_the_legacy_key_empty_without_hitting_its_unique_index()
    {
        var (eventId, seats) = _data.SeedEvent(4);

        foreach (var pair in seats.Chunk(2))
        {
            Assert.Equal(PurchaseSaleStatus.Sold, (await SellNewAsync(eventId, pair)).Status);
        }

        await using var db = new BookingDbContext(_options);
        var legacyKeys = await db.Tickets
            .Where(t => seats.Contains(t.EventSeatId))
            .Select(t => EF.Property<Guid>(t, "IdempotencyKey"))
            .ToListAsync();
        Assert.Equal(4, legacyKeys.Count);
        Assert.All(legacyKeys, key => Assert.Equal(Guid.Empty, key));
    }

    [Fact]
    public async Task A_seat_sold_by_one_flow_is_sold_for_the_other()
    {
        var (eventId, seats) = _data.SeedEvent(2);
        await SellLegacyAsync(eventId, seats[0], Guid.NewGuid());
        Assert.Equal(PurchaseSaleStatus.Sold, (await SellNewAsync(eventId, [seats[1]])).Status);

        var newFlow = await SellNewAsync(eventId, [seats[0]]);
        Assert.Equal(PurchaseSaleStatus.SeatsUnavailable, newFlow.Status);
        Assert.Equal([new FailedEventSeat(seats[0], SeatSaleFailure.AlreadySold)], newFlow.FailedSeats);

        await Assert.ThrowsAsync<EventSeatAlreadySoldException>(() => SellLegacyAsync(eventId, seats[1], Guid.NewGuid()));
    }

    [Fact]
    public async Task Legacy_replays_and_new_replays_never_answer_for_each_other()
    {
        var (eventId, seats) = _data.SeedEvent(2);
        var key = Guid.NewGuid();
        var legacy = await SellLegacyAsync(eventId, seats[0], key);

        // The same key value, used by the new flow for another seat: purchase_sales is a separate key space.
        var newSale = await SellNewAsync(eventId, [seats[1]], key);
        Assert.Equal(PurchaseSaleStatus.Sold, newSale.Status);

        var legacyReplay = await SellLegacyAsync(eventId, seats[0], key);
        Assert.True(legacyReplay.Replayed);
        Assert.Equal(legacy.Sale!.Ticket.TicketCode, legacyReplay.Sale!.Ticket.TicketCode);

        await using var db = new BookingDbContext(_options);
        var newReplay = await new PostgresPurchaseSaleRepository(db).FindByIdempotencyKeyAsync(key, CancellationToken.None);
        Assert.Equal([newSale.Sales[0].Ticket.TicketCode], newReplay!.Sales.Select(s => s.Ticket.TicketCode));
    }

    public void Dispose() => _data.Dispose();

    private async Task<PurchaseSaleOutcome> SellNewAsync(Guid eventId, Guid[] seats, Guid? key = null)
    {
        var purchase = _data.Purchase(eventId, seats);
        var nowUtc = DateTime.UtcNow;
        await using var db = new BookingDbContext(_options);
        return await new PostgresPurchaseSaleRepository(db).SellOnceAsync(
            key ?? Guid.NewGuid(),
            purchase,
            seat => seat.Sell(purchase, nowUtc),
            (_, _) => Task.FromResult(Captured),
            CancellationToken.None);
    }

    private async Task<EventSeatSaleOutcome> SellLegacyAsync(Guid eventId, Guid seat, Guid key)
    {
        await using var db = new BookingDbContext(_options);
#pragma warning disable CS0618 // MVP-02 sale, still used by the Api until T7.
        return await new PostgresEventSeatRepository(db).SellOnceAsync(
            key,
            eventId,
            seat,
            s => s.Sell("Juan Perez", "juan.perez@example.com", DateTime.UtcNow),
            CancellationToken.None);
#pragma warning restore CS0618
    }
}
