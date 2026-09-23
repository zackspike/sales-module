namespace BookingService.Application.Tickets;

/// <summary>
/// Input of the ticket purchase use case: POST /events/{eventId}/tickets.
/// Text fields are nullable because they come straight from an untrusted request body.
/// </summary>
public sealed record PurchaseTicketCommand(Guid EventId, string? FullName, string? Email);
