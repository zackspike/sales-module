using BookingService.Domain.Common;

namespace BookingService.Domain.Exceptions;

public sealed class EventSeatAlreadySoldException : DomainException
{
    public EventSeatAlreadySoldException(Guid eventSeatId)
        : base("EVENT_SEAT_ALREADY_SOLD", "The requested seat has already been purchased.")
    {
        EventSeatId = eventSeatId;
    }

    public Guid EventSeatId { get; }
}
