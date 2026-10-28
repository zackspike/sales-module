using BookingService.Domain.EventSeats;
using BookingService.Infrastructure.Persistence;

namespace BookingService.Infrastructure.Tests;

public class InMemoryEventSeatRepositoryTests
{
    private static readonly Guid RockFestId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly InMemoryEventSeatRepository _repository = new(seedDefaultInventory: true);

    [Fact]
    public async Task SeedDefaultInventory_creates_50_available_seats_for_default_event()
    {
        var seats = await _repository.GetByEventAsync(RockFestId, CancellationToken.None);

        Assert.Equal(50, seats.Count);
        Assert.All(seats, s =>
        {
            Assert.Equal(RockFestId, s.EventId);
            Assert.Equal(EventSeatStatus.Available, s.Status);
        });
        Assert.Single(seats, s => s.VenueSeatId == InMemoryVenueSeatCatalog.DefaultVenueSeats[0].Id);
    }

    [Fact]
    public async Task Unknown_event_has_no_seats()
    {
        Assert.Empty(await _repository.GetByEventAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetByEvent_returns_a_fresh_list_of_the_live_seats()
    {
        var before = await _repository.GetByEventAsync(RockFestId, CancellationToken.None);
        var seat = before.First();

        await _repository.SellOnceAsync(
            Guid.NewGuid(),
            RockFestId,
            seat.Id,
            s => s.Sell("Jane Doe", "jane@example.com", DateTime.UtcNow),
            CancellationToken.None);

        var after = await _repository.GetByEventAsync(RockFestId, CancellationToken.None);

        Assert.NotSame(before, after);
        Assert.Equal(50, after.Count);
        Assert.Same(seat, after.First());
        Assert.Equal(EventSeatStatus.Sold, after.First().Status);
    }

    [Fact]
    public async Task Seat_is_found_only_within_its_event()
    {
        var seat = (await _repository.GetByEventAsync(RockFestId, CancellationToken.None)).First();

        Assert.Same(seat, await _repository.GetByIdAsync(RockFestId, seat.Id, CancellationToken.None));
        Assert.Null(await _repository.GetByIdAsync(Guid.NewGuid(), seat.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SellOnce_sells_a_seat_and_issues_its_ticket()
    {
        var seat = (await _repository.GetByEventAsync(RockFestId, CancellationToken.None)).First();
        var key = Guid.NewGuid();

        var outcome = await _repository.SellOnceAsync(
            key,
            RockFestId,
            seat.Id,
            s => s.Sell("Jane Doe", "jane@example.com", DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(outcome.Replayed);
        Assert.NotNull(outcome.Sale);
        Assert.Same(seat, outcome.Sale!.Seat);
        Assert.Equal(EventSeatStatus.Sold, seat.Status);
        Assert.Equal("Jane Doe", outcome.Sale.Ticket.HolderName);
        Assert.Equal("jane@example.com", outcome.Sale.Ticket.HolderEmail);
        Assert.Equal(seat.Id, outcome.Sale.Ticket.EventSeatId);

        var remembered = await _repository.GetSaleByIdempotencyKeyAsync(
            key,
            RockFestId,
            seat.Id,
            CancellationToken.None);
        Assert.NotNull(remembered);
        Assert.Same(outcome.Sale!.Ticket, remembered!.Ticket);
    }

    [Fact]
    public async Task Same_key_replays_the_original_sale()
    {
        var first = (await _repository.GetByEventAsync(RockFestId, CancellationToken.None)).First();
        var second = (await _repository.GetByEventAsync(RockFestId, CancellationToken.None)).Skip(1).First();
        var key = Guid.NewGuid();

        await _repository.SellOnceAsync(
            key,
            RockFestId,
            first.Id,
            s => s.Sell("Jane Doe", "jane@example.com", DateTime.UtcNow),
            CancellationToken.None);
        var outcome = await _repository.SellOnceAsync(
            key,
            RockFestId,
            second.Id,
            s => s.Sell("Jane Doe", "jane@example.com", DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(outcome.Replayed);
        Assert.NotNull(outcome.Sale);
        Assert.Equal(first.Id, outcome.Sale!.Seat.Id);
        Assert.Equal(EventSeatStatus.Available, second.Status);
    }

    [Fact]
    public async Task SellOnce_on_unknown_seat_returns_null_sale()
    {
        var outcome = await _repository.SellOnceAsync(
            Guid.NewGuid(),
            RockFestId,
            Guid.NewGuid(),
            s => s.Sell("Jane", "jane@example.com", DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(outcome.Replayed);
        Assert.Null(outcome.Sale);
    }

    [Fact]
    public async Task Seat_can_only_be_sold_once()
    {
        var seat = (await _repository.GetByEventAsync(RockFestId, CancellationToken.None)).First();

        await _repository.SellOnceAsync(
            Guid.NewGuid(),
            RockFestId,
            seat.Id,
            s => s.Sell("Jane Doe", "jane@example.com", DateTime.UtcNow),
            CancellationToken.None);

        async Task SellAgain()
        {
            await _repository.SellOnceAsync(
                Guid.NewGuid(),
                RockFestId,
                seat.Id,
                s => s.Sell("John", "john@example.com", DateTime.UtcNow),
                CancellationToken.None);
        }

        await Assert.ThrowsAsync<EventSeatAlreadySoldException>(SellAgain);
    }
}
