namespace BookingService.Domain;

/// <summary>
/// Represents an event in the booking domain (SP-03 / ALIGN-01).
/// The shared event model agreed with EventService and VenueService.
/// </summary>
public sealed record Event(
    Guid Id,
    string Name,
    Guid ArtistId,
    string ArtistName,
    Guid VenueId,
    string VenueName,
    DateTime EventDateTime,
    DateTime EventSalesStartDateTime,
    DateTime EventSalesEndDateTime);
