namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// Input of POST /events/{eventId}/tickets/{ticketId}/release, where the route's ticketId is the event
/// seat id. The purchase comes from the purchase cookie, so the request carries no body.
/// </summary>
public sealed record ReleaseHeldSeatCommand(Guid PurchaseId, Guid EventId, Guid EventSeatId);
