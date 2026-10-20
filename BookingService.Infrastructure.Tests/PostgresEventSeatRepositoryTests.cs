using BookingService.Application.EventSeats.Commands;
using BookingService.Domain.Events;
using BookingService.Domain.EventSeats;
using BookingService.Infrastructure.Caching;
using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Infrastructure.Tests;

/// <summary>Runs only when ConnectionStrings__DefaultConnection points to a PostgreSQL instance (CI sets it).</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            Skip = "Set ConnectionStrings__DefaultConnection to run PostgreSQL tests.";
        }
    }
}

public class PostgresEventSeatRepositoryTests
{
    private static readonly Lazy<DbContextOptions<BookingDbContext>> Options = new(() =>
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(PostgresFactAttribute.ConnectionString)
            .Options;

        using var db = new BookingDbContext(options);
        db.Database.Migrate();
        return options;
    });

    [PostgresFact]
    public void Concurrent_buyers_of_the_same_seat_sell_it_once()
    {
        var (eventId, seats) = SeedEvent(1);

        var results = Race(20, _ => new PurchaseEventSeatCommand(eventId, seats[0], "Juan Perez", "juan.perez@example.com", Guid.NewGuid()));

        Assert.Single(results, s => s == PurchaseEventSeatStatus.Purchased);
        Assert.Equal(19, results.Count(s => s == PurchaseEventSeatStatus.AlreadySold));
    }

    [PostgresFact]
    public void Concurrent_retries_with_the_same_key_replay_the_purchase()
    {
        var (eventId, seats) = SeedEvent(1);
        var key = Guid.NewGuid();

        var results = Race(10, _ => new PurchaseEventSeatCommand(eventId, seats[0], "Juan Perez", "juan.perez@example.com", key));

        Assert.Single(results, s => s == PurchaseEventSeatStatus.Purchased);
        Assert.Equal(9, results.Count(s => s == PurchaseEventSeatStatus.Replayed));
    }

    [PostgresFact]
    public void Same_key_racing_on_two_seats_is_a_conflict()
    {
        var (eventId, seats) = SeedEvent(2);
        var key = Guid.NewGuid();

        var results = Race(2, i => new PurchaseEventSeatCommand(eventId, seats[i], "Juan Perez", "juan.perez@example.com", key));

        Assert.Single(results, s => s == PurchaseEventSeatStatus.Purchased);
        Assert.Single(results, s => s == PurchaseEventSeatStatus.IdempotencyKeyConflict);
    }

    // Every buyer uses the same email, so all of them hold the seat lock and the database decides.
    private static PurchaseEventSeatStatus[] Race(int buyers, Func<int, PurchaseEventSeatCommand> command)
    {
        var results = new PurchaseEventSeatStatus[buyers];
        var locks = new InMemorySeatLockStore();
        for (var i = 0; i < buyers; i++)
        {
            locks.TryAcquire(command(i).EventSeatId, "juan.perez@example.com", TimeSpan.FromMinutes(10));
        }

        Parallel.For(0, buyers, new ParallelOptions { MaxDegreeOfParallelism = buyers }, index =>
        {
            using var db = new BookingDbContext(Options.Value);
            var handler = new PurchaseEventSeatHandler(
                new PostgresEventCatalog(db),
                new PostgresEventSeatRepository(db),
                new InMemoryVenueSeatCatalog(),
                locks,
                new NoCache(),
                new NoCache(),
                NullLogger<PurchaseEventSeatHandler>.Instance);
            results[index] = handler.Handle(command(index)).Status;
        });

        return results;
    }

    private static (Guid EventId, Guid[] Seats) SeedEvent(int totalSeats)
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

        using var db = new BookingDbContext(Options.Value);
        db.Events.Add(@event);
        db.EventSeats.AddRange(eventSeats);
        db.SaveChanges();

        return (@event.Id, eventSeats.Select(s => s.Id).ToArray());
    }
}
