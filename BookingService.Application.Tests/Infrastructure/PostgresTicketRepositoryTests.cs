using BookingService.Application.Tickets.Commands;
using BookingService.Domain.Events;
using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Application.Tests.Infrastructure;

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

public class PostgresTicketRepositoryTests
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

        var results = Race(20, _ => new(eventId, seats[0], "Juan Perez", "juan.perez@example.com", Guid.NewGuid()));

        Assert.Single(results, s => s == PurchaseTicketStatus.Purchased);
        Assert.Equal(19, results.Count(s => s == PurchaseTicketStatus.AlreadySold));
    }

    [PostgresFact]
    public void Concurrent_retries_with_the_same_key_replay_the_purchase()
    {
        var (eventId, seats) = SeedEvent(1);
        var key = Guid.NewGuid();

        var results = Race(10, _ => new(eventId, seats[0], "Juan Perez", "juan.perez@example.com", key));

        Assert.Single(results, s => s == PurchaseTicketStatus.Purchased);
        Assert.Equal(9, results.Count(s => s == PurchaseTicketStatus.Replayed));
    }

    [PostgresFact]
    public void Same_key_racing_on_two_seats_is_a_conflict()
    {
        var (eventId, seats) = SeedEvent(2);
        var key = Guid.NewGuid();

        var results = Race(2, i => new(eventId, seats[i], "Juan Perez", "juan.perez@example.com", key));

        Assert.Single(results, s => s == PurchaseTicketStatus.Purchased);
        Assert.Single(results, s => s == PurchaseTicketStatus.IdempotencyKeyConflict);
    }

    private static PurchaseTicketStatus[] Race(int buyers, Func<int, PurchaseTicketCommand> command)
    {
        var results = new PurchaseTicketStatus[buyers];

        Parallel.For(0, buyers, new ParallelOptions { MaxDegreeOfParallelism = buyers }, index =>
        {
            using var db = new BookingDbContext(Options.Value);
            var handler = new PurchaseTicketHandler(
                new TicketPurchaseValidator(new PostgresEventCatalog(db)),
                new PostgresTicketRepository(db));
            results[index] = handler.Handle(command(index)).Status;
        });

        return results;
    }

    private static (Guid EventId, Guid[] Seats) SeedEvent(int totalSeats)
    {
        var @event = new Event { Id = Guid.NewGuid(), Name = "Race test", Date = DateTime.UtcNow, TotalSeats = totalSeats };
        var tickets = EventInventoryFactory.CreateInitialInventory(@event);

        using var db = new BookingDbContext(Options.Value);
        db.Events.Add(@event);
        db.Tickets.AddRange(tickets);
        db.SaveChanges();

        return (@event.Id, tickets.Select(t => t.Id).ToArray());
    }
}
