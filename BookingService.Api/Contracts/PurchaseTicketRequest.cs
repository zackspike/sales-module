namespace BookingService.Api.Contracts;

/// <summary>
/// Body of POST /events/{eventId}/tickets/{ticketId}/purchase. Nullable because it is untrusted input;
/// the Application validator reports missing fields.
/// </summary>
/// <param name="FullName">Full name of the ticket purchaser (e.g., 'Jane Doe').</param>
/// <param name="Email">Valid RFC 5321 email address for order confirmation (e.g., 'jane.doe@example.com').</param>
public sealed record PurchaseTicketRequest(string? FullName, string? Email);
