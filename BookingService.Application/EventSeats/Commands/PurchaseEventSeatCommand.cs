namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// Input of the event seat purchase use case: POST /events/{eventId}/tickets/{ticketId}/purchase
/// with the X-Idempotency-Key header (SP-05 / SP-06), where the route's ticketId is the event seat id.
/// Text fields are nullable because they come straight from an untrusted request body.
/// </summary>
public sealed record PurchaseEventSeatCommand(
    Guid EventId,
    Guid EventSeatId,
    string? FullName,
    string? Email,
    Guid IdempotencyKey);
