using BookingService.Application.Repositories;
using BookingService.Domain;
using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Infrastructure.Tests;

[Collection(PostgreSqlCollection.Name)]
public class RepositoryTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Seed_script_creates_the_default_event_with_50_available_seats()
    {
        await using var scope = database.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<IEventRepository>();
        var tickets = scope.ServiceProvider.GetRequiredService<ITicketRepository>();

        var defaultEvent = await events.GetByIdAsync(PostgreSqlFixture.DefaultEventId);
        var available = await tickets.GetAvailableByEventAsync(PostgreSqlFixture.DefaultEventId);

        Assert.NotNull(defaultEvent);
        Assert.Equal("Rock Fest 2026", defaultEvent.Name);
        Assert.Equal("The Rockers", defaultEvent.Artist);
        Assert.Equal("Estadio Nacional", defaultEvent.VenueName);
        Assert.Equal(50, defaultEvent.TotalSeats);
        Assert.Single(defaultEvent.Zones);
        Assert.Equal(50, available.Count);
        Assert.Equal(
            Enumerable.Range(1, 50).Select(i => $"A-{i}").Order(),
            available.Select(t => t.Seat!.SeatNumber).Order());
    }

    [Fact]
    public async Task Unknown_event_does_not_exist()
    {
        await using var scope = database.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<IEventRepository>();

        Assert.False(await events.ExistsAsync(Guid.NewGuid()));
        Assert.True(await events.ExistsAsync(PostgreSqlFixture.DefaultEventId));
    }

    [Fact]
    public async Task Tickets_are_only_returned_for_their_own_event()
    {
        var first = await database.CreateEventAsync(3);
        var second = await database.CreateEventAsync(2);

        await using var scope = database.CreateScope();
        var tickets = scope.ServiceProvider.GetRequiredService<ITicketRepository>();

        var available = await tickets.GetAvailableByEventAsync(first.EventId);
        var found = await tickets.GetByIdAsync(first.EventId, first.TicketIds[0]);
        var fromOtherEvent = await tickets.GetByIdAsync(second.EventId, first.TicketIds[0]);

        Assert.Equal(first.TicketIds.Order(), available.Select(t => t.Id).Order());
        Assert.NotNull(found);
        Assert.Equal(first.EventId, found.Seat!.Zone!.EventId);
        Assert.Null(fromOtherEvent);
    }

    [Fact]
    public async Task AddRange_rejects_tickets_of_another_event_without_adding_any()
    {
        var zone = new Zone { Id = Guid.NewGuid(), EventId = Guid.NewGuid() };
        var tickets = EventInventoryFactory.CreateInitialInventory(zone, 2);

        await using var scope = database.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITicketRepository>();

        Assert.Throws<ArgumentException>(() => repository.AddRange(Guid.NewGuid(), tickets));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<BookingDbContext>().ChangeTracker.Entries());
    }

    [Fact]
    public async Task Locking_a_ticket_outside_a_transaction_is_rejected()
    {
        var seeded = await database.CreateEventAsync(1);

        await using var scope = database.CreateScope();
        var tickets = scope.ServiceProvider.GetRequiredService<ITicketRepository>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tickets.GetForPurchaseAsync(seeded.EventId, seeded.TicketIds[0]));
    }

    [Fact]
    public async Task Duplicate_email_is_reported_as_a_unique_constraint_violation()
    {
        var email = $"{Guid.NewGuid():N}@example.com";

        await using (var scope = database.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserRepository>()
                .Add(new User { Id = Guid.NewGuid(), FullName = "First", Email = email });
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using (var scope = database.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserRepository>()
                .Add(new User { Id = Guid.NewGuid(), FullName = "Second", Email = email });

            var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
            Assert.Equal("uq_users_user_email_address", exception.ConstraintName);
        }
    }

    [Fact]
    public async Task Database_rejects_a_sold_ticket_without_buyer()
    {
        var seeded = await database.CreateEventAsync(1);

        await using var scope = database.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var ticket = await context.Tickets.SingleAsync(t => t.Id == seeded.TicketIds[0]);
        ticket.Status = TicketStatus.Sold;

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Failed_transaction_rolls_back_every_change()
    {
        var email = $"{Guid.NewGuid():N}@example.com";

        await using (var scope = database.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                unitOfWork.ExecuteInTransactionAsync<bool>(async token =>
                {
                    users.Add(new User { Id = Guid.NewGuid(), FullName = "Rolled Back", Email = email });
                    await unitOfWork.SaveChangesAsync(token);
                    throw new InvalidOperationException("Simulated failure after saving.");
                }));

            // Nothing stays pending in the context either.
            Assert.Null(await users.GetByEmailAsync(email));
        }

        var persisted = await database.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE user_email_address = @email",
            ("email", email));
        Assert.Equal(0, persisted);
    }
}
