using BookingService.Domain.Common;

namespace BookingService.Domain.Purchases;

public sealed class SeatLimitReachedException : DomainException
{
    public SeatLimitReachedException(int maxSeats)
        : base("SEAT_LIMIT_REACHED", $"A purchase can hold at most {maxSeats} seats.")
    {
        MaxSeats = maxSeats;
    }

    public int MaxSeats { get; }
}
