namespace BookingService.Api.Contracts;

/// <summary>
/// Body of POST /events/{eventId}/tickets/{ticketId}/purchase. Nullable because it is untrusted input;
/// the Application validator reports missing fields.
/// </summary>
public sealed record PurchaseTicketRequest(string? FullName, string? Email);
