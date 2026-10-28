using BookingService.Application.EventSeats.Commands;
using BookingService.Domain.Events;
using BookingService.Domain.EventSeats;
using BookingService.Infrastructure.Caching;
using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PostgresEventSeatRepositoryTests : IDisposable
{
    private readonly DbContextOptions<BookingDbContext> _options;
    private readonly List<Guid> _seededEventIds = [];

    public PostgresEventSeatRepositoryTests(PostgresFixture postgres)
    {
        _options = postgres.Options;
    }

    [Fact]
    public async Task Concurrent_buyers_of_the_same_seat_sell_it_once()
    {
        var (eventId, seats) = SeedEvent(1);

        var results = await Race(20, _ => new PurchaseEventSeatCommand(
            eventId,
            seats[0],
            "Juan Perez",
            "juan.perez@example.com",
            Guid.NewGuid()));

        Assert.Single(results, s => s == PurchaseEventSeatStatus.Purchased);
        Assert.Equal(19, results.Count(s => s == PurchaseEventSeatStatus.AlreadySold));
    }

    [Fact]
    public async Task Concurrent_retries_with_the_same_key_replay_the_purchase()
    {
        var (eventId, seats) = SeedEvent(1);
        var key = Guid.NewGuid();

        var results = await Race(10, _ => new PurchaseEventSeatCommand(
            eventId,
            seats[0],
            "Juan Perez",
            "juan.perez@example.com",
            key));

        Assert.Single(results, s => s == PurchaseEventSeatStatus.Purchased);
        Assert.Equal(9, results.Count(s => s == PurchaseEventSeatStatus.Replayed));
    }

    [Fact]
    public async Task Same_key_racing_on_two_seats_is_a_conflict()
    {
        var (eventId, seats) = SeedEvent(2);
        var key = Guid.NewGuid();

        var results = await Race(2, i => new PurchaseEventSeatCommand(
            eventId,
            seats[i],
            "Juan Perez",
            "juan.perez@example.com",
            key));

        Assert.Single(results, s => s == PurchaseEventSeatStatus.Purchased);
        Assert.Single(results, s => s == PurchaseEventSeatStatus.IdempotencyKeyConflict);
    }

    // Every buyer uses the same email, so all of them hold the seat lock and the database decides.
    private async Task<PurchaseEventSeatStatus[]> Race(
        int buyers,
        Func<int, PurchaseEventSeatCommand> command)
    {
        var locks = new InMemorySeatLockStore();
        for (var i = 0; i < buyers; i++)
        {
            await locks.TryAcquireAsync(
                command(i).EventSeatId,
                "juan.perez@example.com",
                TimeSpan.FromMinutes(10),
                CancellationToken.None);
        }

        return await Task.WhenAll(
            Enumerable.Range(0, buyers).Select(i => RunPurchaseAsync(command(i), locks)));
    }

    private async Task<PurchaseEventSeatStatus> RunPurchaseAsync(
        PurchaseEventSeatCommand command,
        InMemorySeatLockStore locks)
    {
        await using var db = new BookingDbContext(_options);
        var handler = new PurchaseEventSeatHandler(
            new PostgresEventCatalog(db),
            new PostgresEventSeatRepository(db),
            new InMemoryVenueSeatCatalog(),
            locks,
            new NoCache(),
            new NoCache(),
            NullLogger<PurchaseEventSeatHandler>.Instance);

        var result = await handler.HandleAsync(command, CancellationToken.None);
        return result.Status;
    }

    private (Guid EventId, Guid[] Seats) SeedEvent(int totalSeats)
    {
        var @event = new Event(
            Guid.NewGuid(),
            "Race test",
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
        db.Tickets
            .Where(t => db.EventSeats.Any(s => s.Id == t.EventSeatId && _seededEventIds.Contains(s.EventId)))
            .ExecuteDelete();
        db.EventSeats.Where(s => _seededEventIds.Contains(s.EventId)).ExecuteDelete();
        db.Events.Where(e => _seededEventIds.Contains(e.Id)).ExecuteDelete();
    }
}
