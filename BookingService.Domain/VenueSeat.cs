namespace BookingService.Domain;

/// <summary>
/// Represents a physical seat in a venue, in the booking domain.
/// The shared seat model agreed with VenueService.
/// </summary>

public sealed record VenueSeat(
    Guid Id,
    Guid ZoneId,
    string SeatNumber);