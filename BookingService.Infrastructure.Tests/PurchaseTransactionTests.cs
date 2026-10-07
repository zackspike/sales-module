using BookingService.Application.Caching;
using BookingService.Application.Repositories;
using BookingService.Application.Tickets;
using BookingService.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Infrastructure.Tests;

/// <summary>
/// Runs the real reservation and purchase use cases against PostgreSQL and Redis. Each call uses its
/// own DI scope (own DbContext and connection), exactly like concurrent HTTP requests.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public class PurchaseTransactionTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Reserved_purchase_is_persisted_with_its_buyer_and_frees_the_lock()
    {
        var seeded = await infrastructure.CreateEventAsync(1);
        var ticketId = seeded.TicketIds[0];
        var key = Guid.NewGuid();
        var email = UniqueEmail();
        var reservation = await infrastructure.ReserveAsync(seeded.EventId, ticketId, email);

        var result = await PurchaseAsync(seeded.EventId, ticketId, key, email);

        Assert.Equal(ReserveSeatStatus.Reserved, reservation.Status);
        Assert.Equal(PurchaseTicketStatus.Purchased, result.Status);

        await using var scope = infrastructure.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ITicketRepository>()
            .GetByIdAsync(seeded.EventId, ticketId);
        Assert.NotNull(stored);
        Assert.Equal(TicketStatus.Sold, stored.Status);
        Assert.Equal(result.Ticket!.TicketCode, stored.TicketCode);
        Assert.Equal(key, stored.IdempotencyKey);
        Assert.NotNull(stored.PurchasedAtUtc);
        Assert.Equal(email, stored.User!.Email);
        Assert.Equal(reservation.BuyerId, stored.UserId);

        Assert.Null(await scope.ServiceProvider.GetRequiredService<ISeatLockStore>().GetHolderAsync(ticketId));
        Assert.Equal(
            new IdempotencyRecord(seeded.EventId, ticketId),
            await scope.ServiceProvider.GetRequiredService<IIdempotencyStore>().GetAsync(key));
    }

    [Fact]
    public async Task Concurrent_reservations_of_the_same_seat_only_one_buyer_wins()
    {
        var seeded = await infrastructure.CreateEventAsync(1);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => infrastructure.ReserveAsync(seeded.EventId, seeded.TicketIds[0], UniqueEmail()))));

        Assert.Single(results, r => r.Status == ReserveSeatStatus.Reserved);
        Assert.Equal(19, results.Count(r => r.Status == ReserveSeatStatus.LockedByAnotherUser));
    }

    [Fact]
    public async Task Purchase_without_reservation_is_forbidden_and_changes_nothing()
    {
        var seeded = await infrastructure.CreateEventAsync(1);

        var result = await PurchaseAsync(seeded.EventId, seeded.TicketIds[0], Guid.NewGuid(), UniqueEmail());

        Assert.Equal(PurchaseTicketStatus.ReservationRequired, result.Status);
        var available = await infrastructure.QueryScalarAsync<bool>(
            "SELECT is_ticket_available FROM tickets WHERE ticket_id = @id",
            ("id", seeded.TicketIds[0]));
        Assert.True(available);
    }

    [Fact]
    public async Task Purchase_of_a_seat_reserved_by_another_buyer_is_forbidden()
    {
        var seeded = await infrastructure.CreateEventAsync(2);
        var otherBuyer = UniqueEmail();
        await infrastructure.ReserveAsync(seeded.EventId, seeded.TicketIds[0], UniqueEmail());
        await infrastructure.ReserveAsync(seeded.EventId, seeded.TicketIds[1], otherBuyer);

        var result = await PurchaseAsync(seeded.EventId, seeded.TicketIds[0], Guid.NewGuid(), otherBuyer);

        Assert.Equal(PurchaseTicketStatus.ReservationRequired, result.Status);
    }

    [Fact]
    public async Task Concurrent_purchases_by_the_holder_sell_the_seat_once()
    {
        var seeded = await infrastructure.CreateEventAsync(1);
        var email = UniqueEmail();
        await infrastructure.ReserveAsync(seeded.EventId, seeded.TicketIds[0], email);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => PurchaseAsync(seeded.EventId, seeded.TicketIds[0], Guid.NewGuid(), email))));

        Assert.Single(results, r => r.Status == PurchaseTicketStatus.Purchased);
        Assert.All(
            results.Where(r => r.Status != PurchaseTicketStatus.Purchased),
            r => Assert.Contains(r.Status, new[] { PurchaseTicketStatus.AlreadySold, PurchaseTicketStatus.ReservationRequired }));

        var sold = await infrastructure.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM tickets WHERE ticket_id = @id AND NOT is_ticket_available",
            ("id", seeded.TicketIds[0]));
        Assert.Equal(1, sold);
    }

    [Fact]
    public async Task Concurrent_retries_with_the_same_key_sell_the_seat_once()
    {
        var seeded = await infrastructure.CreateEventAsync(1);
        var key = Guid.NewGuid();
        var email = UniqueEmail();
        await infrastructure.ReserveAsync(seeded.EventId, seeded.TicketIds[0], email);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => PurchaseAsync(seeded.EventId, seeded.TicketIds[0], key, email))));

        Assert.Single(results, r => r.Status == PurchaseTicketStatus.Purchased);
        Assert.Equal(9, results.Count(r => r.Status == PurchaseTicketStatus.Replayed));
        Assert.Single(results.Select(r => r.Ticket!.TicketCode).Distinct());
    }

    [Fact]
    public async Task Concurrent_requests_reusing_a_key_for_different_seats_buy_only_one()
    {
        var seeded = await infrastructure.CreateEventAsync(5);
        var key = Guid.NewGuid();
        var email = UniqueEmail();
        foreach (var ticketId in seeded.TicketIds)
        {
            await infrastructure.ReserveAsync(seeded.EventId, ticketId, email);
        }

        var results = await Task.WhenAll(seeded.TicketIds.Select(ticketId =>
            Task.Run(() => PurchaseAsync(seeded.EventId, ticketId, key, email))));

        Assert.Single(results, r => r.Status == PurchaseTicketStatus.Purchased);
        Assert.Equal(4, results.Count(r => r.Status == PurchaseTicketStatus.IdempotencyKeyConflict));

        var sold = await infrastructure.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM tickets WHERE ticket_id = ANY(@ids) AND NOT is_ticket_available",
            ("ids", seeded.TicketIds.ToArray()));
        Assert.Equal(1, sold);
    }

    [Fact]
    public async Task Concurrent_reservations_by_a_new_buyer_register_the_user_once()
    {
        var seeded = await infrastructure.CreateEventAsync(5);
        var email = UniqueEmail();

        var results = await Task.WhenAll(seeded.TicketIds.Select(ticketId =>
            Task.Run(() => infrastructure.ReserveAsync(seeded.EventId, ticketId, email))));

        Assert.All(results, r => Assert.Equal(ReserveSeatStatus.Reserved, r.Status));
        Assert.Single(results.Select(r => r.BuyerId).Distinct());

        var storedUsers = await infrastructure.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE user_email_address = @email",
            ("email", email));
        Assert.Equal(1, storedUsers);
    }

    [Fact]
    public async Task Sold_seat_cannot_be_reserved_or_bought_again()
    {
        var seeded = await infrastructure.CreateEventAsync(1);
        var email = UniqueEmail();
        await infrastructure.ReserveAsync(seeded.EventId, seeded.TicketIds[0], email);
        await PurchaseAsync(seeded.EventId, seeded.TicketIds[0], Guid.NewGuid(), email);

        var reservation = await infrastructure.ReserveAsync(seeded.EventId, seeded.TicketIds[0], UniqueEmail());
        var purchase = await PurchaseAsync(seeded.EventId, seeded.TicketIds[0], Guid.NewGuid(), email);

        Assert.Equal(ReserveSeatStatus.AlreadySold, reservation.Status);
        Assert.Equal(PurchaseTicketStatus.AlreadySold, purchase.Status);
    }

    private async Task<PurchaseTicketResult> PurchaseAsync(Guid eventId, Guid ticketId, Guid key, string email)
    {
        await using var scope = infrastructure.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<PurchaseTicketHandler>();

        return await handler.HandleAsync(new PurchaseTicketCommand(eventId, ticketId, "Juan Perez", email, key));
    }

    private static string UniqueEmail() => $"buyer-{Guid.NewGuid():N}@example.com";
}
