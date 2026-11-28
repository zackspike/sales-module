using BookingService.Domain.Events;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;
using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Tests.Support;

/// <summary>
/// Seeds events and purchases in the shared PostgreSQL container and removes exactly what it created.
/// Children go first because the foreign keys are Restrict.
/// </summary>
public sealed class PostgresTestData : IDisposable
{
    private readonly DbContextOptions<BookingDbContext> _options;
    private readonly List<Guid> _eventIds = [];
    private readonly List<Guid> _purchaseIds = [];

    public PostgresTestData(DbContextOptions<BookingDbContext> options)
    {
        _options = options;
    }

    public (Guid EventId, Guid[] Seats) SeedEvent(int totalSeats)
    {
        var @event = new Event(
            Guid.NewGuid(),
            "Integration test",
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
        _eventIds.Add(@event.Id);

        return (@event.Id, eventSeats.Select(s => s.Id).ToArray());
    }

    /// <summary>A purchase of <paramref name="eventId"/> holding <paramref name="seats"/>, with a payment token.</summary>
    public PurchaseSession Purchase(
        Guid eventId,
        IEnumerable<Guid> seats,
        string name = "Jane Doe",
        string email = "jane.doe@example.com")
    {
        var nowUtc = DateTime.UtcNow;
        var purchase = PurchaseSession.Start(eventId, name, email, nowUtc, TimeSpan.FromMinutes(10));
        foreach (var seat in seats)
        {
            purchase.Hold(eventId, seat, maxSeats: 10, nowUtc);
        }

        purchase.SetPaymentToken("pay_tok_test");
        Track(purchase.Id);
        return purchase;
    }

    /// <summary>Registers a purchase id whose outbox messages and sales must be removed at the end.</summary>
    public void Track(Guid purchaseId) => _purchaseIds.Add(purchaseId);

    public async Task<List<Guid>> SoldSeatsAsync(Guid eventId)
    {
        await using var db = new BookingDbContext(_options);
        return await db.EventSeats
            .Where(s => s.EventId == eventId && s.Status == EventSeatStatus.Sold)
            .Select(s => s.Id)
            .ToListAsync();
    }

    public void Dispose()
    {
        using var db = new BookingDbContext(_options);
        var purchaseIds = _purchaseIds.Select(id => id.ToString()).ToList();
        db.OutboxMessages.RemoveRange(
            db.OutboxMessages.AsEnumerable().Where(m => purchaseIds.Any(m.Payload.Contains)));
        db.SaveChanges();

        db.Tickets
            .Where(t => db.EventSeats.Any(s => s.Id == t.EventSeatId && _eventIds.Contains(s.EventId)))
            .ExecuteDelete();
        db.PurchaseSales.Where(s => _purchaseIds.Contains(s.PurchaseId)).ExecuteDelete();
        db.EventSeats.Where(s => _eventIds.Contains(s.EventId)).ExecuteDelete();
        db.Events.Where(e => _eventIds.Contains(e.Id)).ExecuteDelete();
    }
}
