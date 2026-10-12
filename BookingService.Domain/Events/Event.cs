namespace BookingService.Domain.Events;

// TODO: property names follow the contract agreed with EventService. If it can be renegotiated, consider
// dropping the redundant "Event" prefix (EventDateTime -> StartsAtUtc, EventSalesStart/EndDateTime ->
// SalesStartAtUtc/SalesEndAtUtc) and stating the time zone with the "Utc" suffix, as SoldAtUtc and ExpiresAtUtc do.
// Note: the sales window (EventSalesStartDateTime/EventSalesEndDateTime) is not enforced by any use case yet.

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
