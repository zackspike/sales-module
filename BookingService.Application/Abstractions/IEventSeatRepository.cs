using BookingService.Domain.EventSeats;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Abstractions;

/// <summary>
/// Durable store of the event seats of each event and of the tickets issued when they are sold.
/// Also keeps the idempotency key of every sale, the durable guarantee behind SP-06.
/// </summary>
public interface IEventSeatRepository
{
    /// <summary>
    /// Returns all event seats of event <paramref name="eventId"/>; empty if the event has none.
    /// </summary>
    IReadOnlyCollection<EventSeat> GetByEvent(Guid eventId);

    /// <summary>
    /// Returns event seat <paramref name="eventSeatId"/> only if it belongs to event <paramref name="eventId"/>.
    /// </summary>
    EventSeat? GetById(Guid eventId, Guid eventSeatId);

    /// <summary>
    /// Returns the sale made with <paramref name="idempotencyKey"/> on seat <paramref name="eventSeatId"/> of event
    /// <paramref name="eventId"/>, or null if that key did not buy that seat (never used, or used for another seat).
    /// </summary>
    EventSeatSale? GetSaleByIdempotencyKey(Guid idempotencyKey, Guid eventId, Guid eventSeatId);

    /// <summary>
    /// Runs <paramref name="sell"/> on seat <paramref name="eventSeatId"/> of event <paramref name="eventId"/>
    /// at most once per <paramref name="idempotencyKey"/>. The key lookup, the seat lookup, the sale and the
    /// storage of the sold seat, its ticket and the key happen atomically, so two concurrent buyers can't both
    /// buy the same seat. If the key was already used, nothing is sold and the sale made with it is returned
    /// with <see cref="EventSeatSaleOutcome.Replayed"/> set, even if it was for another seat.
    /// </summary>
    /// <exception cref="EventSeatAlreadySoldException">
    /// <paramref name="sell"/> found the seat already sold. Nothing is stored and the exception propagates.
    /// </exception>
    EventSeatSaleOutcome SellOnce(Guid idempotencyKey, Guid eventId, Guid eventSeatId, Func<EventSeat, Ticket> sell);
}

/// <summary>
/// A sold event seat together with the ticket issued for it.
/// </summary>
public sealed record EventSeatSale(EventSeat Seat, Ticket Ticket);

/// <summary>
/// Result of <see cref="IEventSeatRepository.SellOnce"/>. <see cref="Sale"/> is null when the seat does not
/// exist in the event. When the key was already used, <see cref="Replayed"/> is true and <see cref="Sale"/>
/// is the sale made with it (which may be a different seat).
/// </summary>
public sealed record EventSeatSaleOutcome(EventSeatSale? Sale, bool Replayed);
