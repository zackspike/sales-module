using BookingService.Application.Repositories;
using BookingService.Application.Tickets;
using BookingService.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Infrastructure.Tests;

/// <summary>
/// Runs the real purchase use case against PostgreSQL. Each call uses its own DI scope
/// (own DbContext and connection), exactly like concurrent HTTP requests.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class PurchaseTransactionTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Purchase_is_persisted_with_its_buyer()
    {
        var seeded = await database.CreateEventAsync(1);
        var key = Guid.NewGuid();
        var email = UniqueEmail();

        var result = await PurchaseAsync(seeded.EventId, seeded.TicketIds[0], key, email);

        Assert.Equal(PurchaseTicketStatus.Purchased, result.Status);

        await using var scope = database.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ITicketRepository>()
            .GetByIdAsync(seeded.EventId, seeded.TicketIds[0]);
        Assert.NotNull(stored);
        Assert.Equal(TicketStatus.Sold, stored.Status);
        Assert.Equal(result.Ticket!.TicketCode, stored.TicketCode);
        Assert.Equal(key, stored.IdempotencyKey);
        Assert.NotNull(stored.PurchasedAtUtc);
        Assert.Equal(email, stored.User!.Email);
        Assert.Equal("Juan Perez", stored.User.FullName);
    }

    [Fact]
    public async Task Concurrent_buyers_of_the_same_seat_only_one_wins_and_losers_leave_no_trace()
    {
        var seeded = await database.CreateEventAsync(1);
        var emails = Enumerable.Range(0, 20).Select(_ => UniqueEmail()).ToList();

        var results = await Task.WhenAll(emails.Select(email =>
            Task.Run(() => PurchaseAsync(seeded.EventId, seeded.TicketIds[0], Guid.NewGuid(), email))));

        Assert.Single(results, r => r.Status == PurchaseTicketStatus.Purchased);
        Assert.Equal(19, results.Count(r => r.Status == PurchaseTicketStatus.AlreadySold));

        // Atomicity: the buyers that lost the seat were rolled back, users included.
        var storedUsers = await database.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE user_email_address = ANY(@emails)",
            ("emails", emails.ToArray()));
        Assert.Equal(1, storedUsers);
    }

    [Fact]
    public async Task Concurrent_retries_with_the_same_key_sell_the_seat_once()
    {
        var seeded = await database.CreateEventAsync(1);
        var key = Guid.NewGuid();
        var email = UniqueEmail();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => PurchaseAsync(seeded.EventId, seeded.TicketIds[0], key, email))));

        Assert.Single(results, r => r.Status == PurchaseTicketStatus.Purchased);
        Assert.Equal(9, results.Count(r => r.Status == PurchaseTicketStatus.Replayed));
        Assert.Single(results.Select(r => r.Ticket!.TicketCode).Distinct());
    }

    [Fact]
    public async Task Concurrent_requests_reusing_a_key_for_different_seats_buy_only_one()
    {
        var seeded = await database.CreateEventAsync(5);
        var key = Guid.NewGuid();
        var email = UniqueEmail();

        var results = await Task.WhenAll(seeded.TicketIds.Select(ticketId =>
            Task.Run(() => PurchaseAsync(seeded.EventId, ticketId, key, email))));

        Assert.Single(results, r => r.Status == PurchaseTicketStatus.Purchased);
        Assert.Equal(4, results.Count(r => r.Status == PurchaseTicketStatus.IdempotencyKeyConflict));

        await using var scope = database.CreateScope();
        var available = await scope.ServiceProvider.GetRequiredService<ITicketRepository>()
            .GetAvailableByEventAsync(seeded.EventId);
        Assert.Equal(4, available.Count);
    }

    [Fact]
    public async Task Concurrent_purchases_by_a_new_buyer_register_the_user_once()
    {
        var seeded = await database.CreateEventAsync(5);
        var email = UniqueEmail();

        var results = await Task.WhenAll(seeded.TicketIds.Select(ticketId =>
            Task.Run(() => PurchaseAsync(seeded.EventId, ticketId, Guid.NewGuid(), email))));

        Assert.All(results, r => Assert.Equal(PurchaseTicketStatus.Purchased, r.Status));
        Assert.Single(results.Select(r => r.Ticket!.UserId).Distinct());

        var storedUsers = await database.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE user_email_address = @email",
            ("email", email));
        Assert.Equal(1, storedUsers);
    }

    [Fact]
    public async Task Rejected_purchase_does_not_change_the_seat()
    {
        var seeded = await database.CreateEventAsync(1);
        await PurchaseAsync(seeded.EventId, seeded.TicketIds[0], Guid.NewGuid(), UniqueEmail());
        var secondEmail = UniqueEmail();

        var result = await PurchaseAsync(seeded.EventId, seeded.TicketIds[0], Guid.NewGuid(), secondEmail);

        Assert.Equal(PurchaseTicketStatus.AlreadySold, result.Status);
        var storedUsers = await database.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE user_email_address = @email",
            ("email", secondEmail));
        Assert.Equal(0, storedUsers);
    }

    private async Task<PurchaseTicketResult> PurchaseAsync(Guid eventId, Guid ticketId, Guid key, string email)
    {
        await using var scope = database.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<PurchaseTicketHandler>();

        return await handler.HandleAsync(new PurchaseTicketCommand(eventId, ticketId, "Juan Perez", email, key));
    }

    private static string UniqueEmail() => $"buyer-{Guid.NewGuid():N}@example.com";
}
