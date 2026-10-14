using BookingService.Domain.Tickets;
using BookingService.Infrastructure.Persistence;

namespace BookingService.Application.Tests.Infrastructure;

public class InMemoryTicketRepositoryTests
{
    private static readonly Guid RockFestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JazzNightId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly InMemoryTicketRepository _repository = new();

    [Fact]
    public void Tickets_are_grouped_by_event()
    {
        var rockSeats = Seats(RockFestId, 3);
        var jazzSeats = Seats(JazzNightId, 2);

        _repository.AddRange(RockFestId, rockSeats);
        _repository.AddRange(JazzNightId, jazzSeats);

        Assert.Equal(rockSeats.Select(t => t.Id).Order(), _repository.GetByEvent(RockFestId).Select(t => t.Id).Order());
        Assert.Equal(jazzSeats.Select(t => t.Id).Order(), _repository.GetByEvent(JazzNightId).Select(t => t.Id).Order());
    }

    [Fact]
    public void Adding_more_tickets_to_an_event_keeps_the_existing_ones()
    {
        _repository.AddRange(RockFestId, Seats(RockFestId, 2));
        _repository.AddRange(RockFestId, Seats(RockFestId, 3));

        Assert.Equal(5, _repository.GetByEvent(RockFestId).Count);
    }

    [Fact]
    public void Unknown_event_has_no_tickets()
    {
        Assert.Empty(_repository.GetByEvent(Guid.NewGuid()));
    }

    [Fact]
    public void GetByEvent_returns_a_snapshot()
    {
        _repository.AddRange(RockFestId, Seats(RockFestId, 2));
        var snapshot = _repository.GetByEvent(RockFestId);

        _repository.AddRange(RockFestId, Seats(RockFestId, 1));

        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public void Ticket_is_found_only_within_its_event()
    {
        var seat = Seats(RockFestId, 1)[0];
        _repository.AddRange(RockFestId, [seat]);
        _repository.AddRange(JazzNightId, Seats(JazzNightId, 1));

        Assert.Same(seat, _repository.GetById(RockFestId, seat.Id));
        Assert.Null(_repository.GetById(JazzNightId, seat.Id));
        Assert.Null(_repository.GetById(Guid.NewGuid(), seat.Id));
    }

    [Fact]
    public void Ticket_of_another_event_is_rejected_and_nothing_is_added()
    {
        var batch = Seats(RockFestId, 2).Append(Seats(JazzNightId, 1)[0]);

        Assert.Throws<ArgumentException>(() => _repository.AddRange(RockFestId, batch));
        Assert.Empty(_repository.GetByEvent(RockFestId));
    }

    [Fact]
    public void Duplicate_ticket_id_is_rejected_and_nothing_is_added()
    {
        var existing = Seats(RockFestId, 1);
        _repository.AddRange(RockFestId, existing);

        var batch = Seats(RockFestId, 2).Append(existing[0]);

        Assert.Throws<ArgumentException>(() => _repository.AddRange(RockFestId, batch));
        Assert.Single(_repository.GetByEvent(RockFestId));
    }

    [Fact]
    public void Duplicate_ticket_id_within_the_batch_is_rejected()
    {
        var seat = Seats(RockFestId, 1)[0];

        Assert.Throws<ArgumentException>(() => _repository.AddRange(RockFestId, [seat, seat]));
        Assert.Empty(_repository.GetByEvent(RockFestId));
    }

    [Fact]
    public void Concurrent_inventory_loads_for_different_events_are_all_stored()
    {
        var eventIds = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();

        Parallel.ForEach(eventIds, eventId => _repository.AddRange(eventId, Seats(eventId, 50)));

        Assert.All(eventIds, eventId => Assert.Equal(50, _repository.GetByEvent(eventId).Count));
    }

    [Fact]
    public void SeedDefaultInventory_creates_50_available_seats_for_default_event()
    {
        var seededRepository = new InMemoryTicketRepository(seedDefaultInventory: true);
        var seats = seededRepository.GetByEvent(RockFestId);

        Assert.Equal(50, seats.Count);
        Assert.All(seats, s =>
        {
            Assert.Equal(RockFestId, s.EventId);
            Assert.Equal(TicketStatus.Available, s.Status);
        });
        Assert.Equal("A-1", seats.First().SeatNumber);
        Assert.Equal("A-50", seats.Last().SeatNumber);
    }

    private static Ticket[] Seats(Guid eventId, int count) =>
        Enumerable.Range(1, count)
            .Select(number => new Ticket { Id = Guid.NewGuid(), EventId = eventId, SeatNumber = $"A-{number}" })
            .ToArray();
}
