using BookingService.Application.Abstractions;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeEventSeatRepository(params EventSeat[] initialSeats) : IEventSeatRepository
{
    private readonly object _sync = new();
    private readonly List<EventSeat> _seats = initialSeats.ToList();
    private readonly Dictionary<Guid, EventSeatSale> _salesByKey = [];

    public int GetByEventCalls { get; private set; }

    public int GetByIdCalls { get; private set; }

    public List<Guid> RequestedIds { get; } = [];

    public Action<EventSeat?>? AfterSecondRead { get; set; }

    public bool ReturnNullAfterSecondRead { get; init; }

    public bool ThrowAlreadySold { get; init; }

    public bool RemoveSeatBeforeSellOnce { get; init; }

    public IReadOnlyCollection<EventSeat> Seats => _seats;

    public IReadOnlyCollection<EventSeat> GetByEvent(Guid eventId)
    {
        lock (_sync)
        {
            GetByEventCalls++;
            return _seats.Where(seat => seat.EventId == eventId).ToArray();
        }
    }

    public EventSeat? GetById(Guid eventId, Guid eventSeatId)
    {
        lock (_sync)
        {
            GetByIdCalls++;
            RequestedIds.Add(eventSeatId);
            var seat = _seats.FirstOrDefault(candidate =>
                candidate.EventId == eventId && candidate.Id == eventSeatId);
            if (GetByIdCalls == 2)
            {
                AfterSecondRead?.Invoke(seat);
            }

            return ReturnNullAfterSecondRead && GetByIdCalls == 2 ? null : seat;
        }
    }

    public EventSeatSale? GetSaleByIdempotencyKey(Guid idempotencyKey, Guid eventId, Guid eventSeatId)
    {
        lock (_sync)
        {
            return _salesByKey.GetValueOrDefault(idempotencyKey) is { } sale
                && sale.Seat.EventId == eventId
                && sale.Seat.Id == eventSeatId
                ? sale
                : null;
        }
    }

    public EventSeatSaleOutcome SellOnce(
        Guid idempotencyKey,
        Guid eventId,
        Guid eventSeatId,
        Func<EventSeat, Ticket> sell)
    {
        lock (_sync)
        {
            if (_salesByKey.TryGetValue(idempotencyKey, out var replay))
            {
                return new EventSeatSaleOutcome(replay, true);
            }

            if (RemoveSeatBeforeSellOnce)
            {
                _seats.RemoveAll(candidate =>
                    candidate.EventId == eventId && candidate.Id == eventSeatId);
            }

            var seat = _seats.FirstOrDefault(candidate =>
                candidate.EventId == eventId && candidate.Id == eventSeatId);
            if (seat is null)
            {
                return new EventSeatSaleOutcome(null, false);
            }

            if (ThrowAlreadySold)
            {
                throw new EventSeatAlreadySoldException(seat.Id);
            }

            var sale = new EventSeatSale(seat, sell(seat));
            _salesByKey[idempotencyKey] = sale;
            return new EventSeatSaleOutcome(sale, false);
        }
    }

    public void RememberSale(Guid idempotencyKey, EventSeat seat)
    {
        lock (_sync)
        {
            _salesByKey[idempotencyKey] = new EventSeatSale(
                seat,
                seat.Sell("original", "original@example.com", DateTime.UtcNow));
        }
    }
}
