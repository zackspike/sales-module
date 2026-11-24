namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// Input of POST /events/{eventId}/tickets/{ticketId}/reserve, where the route's ticketId is the event
/// seat id. The purchase comes from the purchase cookie, so the request carries no body: the buyer's
/// email lives in the session.
/// </summary>
public sealed record HoldSeatCommand(Guid PurchaseId, Guid EventId, Guid EventSeatId);
