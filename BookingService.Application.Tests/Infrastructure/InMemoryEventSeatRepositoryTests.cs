using BookingService.Domain.EventSeats;
using BookingService.Domain.Tickets;
using BookingService.Infrastructure.Persistence;

namespace BookingService.Application.Tests.Infrastructure;

public class InMemoryEventSeatRepositoryTests
{
    private static readonly Guid RockFestId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly InMemoryEventSeatRepository _repository = new(seedDefaultInventory: true);

    [Fact]
    public void SeedDefaultInventory_creates_50_available_seats_for_default_event()
    {
        var seats = _repository.GetByEvent(RockFestId);

        Assert.Equal(50, seats.Count);
        Assert.All(seats, s =>
        {
            Assert.Equal(RockFestId, s.EventId);
            Assert.Equal(EventSeatStatus.Available, s.Status);
        });
        Assert.Single(seats, s => s.VenueSeatId == InMemoryVenueSeatCatalog.DefaultVenueSeats[0].Id);
    }

    [Fact]
    public void Unknown_event_has_no_seats()
    {
        Assert.Empty(_repository.GetByEvent(Guid.NewGuid()));
    }

    [Fact]
    public void GetByEvent_returns_a_fresh_list_of_the_live_seats()
    {
        var before = _repository.GetByEvent(RockFestId);
        var seat = before.First();

        _repository.SellOnce(Guid.NewGuid(), RockFestId, seat.Id, s => s.Sell("Jane Doe", "jane@example.com", DateTime.UtcNow));

        var after = _repository.GetByEvent(RockFestId);

        Assert.NotSame(before, after);
        Assert.Equal(50, after.Count);
        Assert.Same(seat, after.First());
        Assert.Equal(EventSeatStatus.Sold, after.First().Status);
    }

    [Fact]
    public void Seat_is_found_only_within_its_event()
    {
        var seat = _repository.GetByEvent(RockFestId).First();

        Assert.Same(seat, _repository.GetById(RockFestId, seat.Id));
        Assert.Null(_repository.GetById(Guid.NewGuid(), seat.Id));
    }

    [Fact]
    public void SellOnce_sells_a_seat_and_issues_its_ticket()
    {
        var seat = _repository.GetByEvent(RockFestId).First();
        var key = Guid.NewGuid();

        var outcome = _repository.SellOnce(key, RockFestId, seat.Id, s => s.Sell("Jane Doe", "jane@example.com", DateTime.UtcNow));

        Assert.False(outcome.Replayed);
        Assert.NotNull(outcome.Sale);
        Assert.Same(seat, outcome.Sale!.Seat);
        Assert.Equal(EventSeatStatus.Sold, seat.Status);
        Assert.Equal("Jane Doe", outcome.Sale.Ticket.HolderName);
        Assert.Equal("jane@example.com", outcome.Sale.Ticket.HolderEmail);
        Assert.Equal(seat.Id, outcome.Sale.Ticket.EventSeatId);

        var remembered = _repository.GetSaleByIdempotencyKey(key, RockFestId, seat.Id);
        Assert.NotNull(remembered);
        Assert.Same(outcome.Sale!.Ticket, remembered!.Ticket);
    }

    [Fact]
    public void Same_key_replays_the_original_sale()
    {
        var first = _repository.GetByEvent(RockFestId).First();
        var second = _repository.GetByEvent(RockFestId).Skip(1).First();
        var key = Guid.NewGuid();

        _repository.SellOnce(key, RockFestId, first.Id, s => s.Sell("Jane Doe", "jane@example.com", DateTime.UtcNow));
        var outcome = _repository.SellOnce(key, RockFestId, second.Id, s => s.Sell("Jane Doe", "jane@example.com", DateTime.UtcNow));

        Assert.True(outcome.Replayed);
        Assert.NotNull(outcome.Sale);
        Assert.Equal(first.Id, outcome.Sale!.Seat.Id);
        Assert.Equal(EventSeatStatus.Available, second.Status);
    }

    [Fact]
    public void SellOnce_on_unknown_seat_returns_null_sale()
    {
        var outcome = _repository.SellOnce(Guid.NewGuid(), RockFestId, Guid.NewGuid(), s => s.Sell("Jane", "jane@example.com", DateTime.UtcNow));

        Assert.False(outcome.Replayed);
        Assert.Null(outcome.Sale);
    }

    [Fact]
    public void Seat_can_only_be_sold_once()
    {
        var seat = _repository.GetByEvent(RockFestId).First();

        _repository.SellOnce(Guid.NewGuid(), RockFestId, seat.Id, s => s.Sell("Jane Doe", "jane@example.com", DateTime.UtcNow));

        void SellAgain()
        {
            _repository.SellOnce(Guid.NewGuid(), RockFestId, seat.Id, s => s.Sell("John", "john@example.com", DateTime.UtcNow));
        }

        Assert.Throws<EventSeatAlreadySoldException>(SellAgain);
    }
}
