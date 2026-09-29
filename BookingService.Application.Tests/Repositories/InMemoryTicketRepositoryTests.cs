using BookingService.Domain;
using BookingService.Infrastructure.Repositories;

namespace BookingService.Application.Tests.Repositories;

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
        Assert.Equal(5, _repository.GetAll().Count);
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
        Assert.Same(seat, _repository.GetById(seat.Id));
    }

    [Fact]
    public void Ticket_of_another_event_is_rejected_and_nothing_is_added()
    {
        var batch = Seats(RockFestId, 2).Append(Seats(JazzNightId, 1)[0]);

        Assert.Throws<ArgumentException>(() => _repository.AddRange(RockFestId, batch));
        Assert.Empty(_repository.GetAll());
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
        Assert.Empty(_repository.GetAll());
    }

    [Fact]
    public void GetOrAdd_stores_the_ticket_under_its_event_once_per_key()
    {
        var key = Guid.NewGuid();
        var ticket = Seats(RockFestId, 1)[0];

        var created = _repository.GetOrAdd(key, ticket, out var wasCreated);
        var retried = _repository.GetOrAdd(key, Seats(RockFestId, 1)[0], out var wasCreatedOnRetry);

        Assert.True(wasCreated);
        Assert.False(wasCreatedOnRetry);
        Assert.Same(created, retried);
        Assert.Same(ticket, Assert.Single(_repository.GetByEvent(RockFestId)));
    }

    [Fact]
    public void Update_replaces_the_ticket_within_its_event()
    {
        var seat = Seats(RockFestId, 1)[0];
        _repository.AddRange(RockFestId, [seat]);

        var sold = new Ticket { Id = seat.Id, EventId = RockFestId, SeatNumber = seat.SeatNumber, Status = TicketStatus.Sold };
        _repository.Update(sold);

        Assert.Same(sold, _repository.GetById(RockFestId, seat.Id));
        Assert.Single(_repository.GetByEvent(RockFestId));
    }

    [Fact]
    public void Update_of_unknown_ticket_throws()
    {
        Assert.Throws<KeyNotFoundException>(() => _repository.Update(Seats(RockFestId, 1)[0]));
    }

    [Fact]
    public void Remove_deletes_the_ticket_from_its_event()
    {
        var seats = Seats(RockFestId, 2);
        _repository.AddRange(RockFestId, seats);

        Assert.True(_repository.Remove(seats[0].Id));
        Assert.False(_repository.Remove(seats[0].Id));

        Assert.Null(_repository.GetById(seats[0].Id));
        Assert.Same(seats[1], Assert.Single(_repository.GetByEvent(RockFestId)));
    }

    [Fact]
    public void Concurrent_inventory_loads_for_different_events_are_all_stored()
    {
        var eventIds = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();

        Parallel.ForEach(eventIds, eventId => _repository.AddRange(eventId, Seats(eventId, 50)));

        Assert.All(eventIds, eventId => Assert.Equal(50, _repository.GetByEvent(eventId).Count));
        Assert.Equal(1000, _repository.GetAll().Count);
    }

    private static Ticket[] Seats(Guid eventId, int count) =>
        Enumerable.Range(1, count)
            .Select(number => new Ticket { Id = Guid.NewGuid(), EventId = eventId, SeatNumber = $"A-{number}" })
            .ToArray();
}
