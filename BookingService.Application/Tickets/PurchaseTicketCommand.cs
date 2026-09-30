namespace BookingService.Application.Tickets;

/// <summary>
/// Input of the seat purchase use case: POST /events/{eventId}/tickets/{ticketId}/purchase
/// with the X-Idempotency-Key header (SP-05 / SP-06).
/// Text fields are nullable because they come straight from an untrusted request body.
/// </summary>
public sealed record PurchaseTicketCommand(
    Guid EventId,
    Guid TicketId,
    string? FullName,
    string? Email,
    Guid IdempotencyKey);
