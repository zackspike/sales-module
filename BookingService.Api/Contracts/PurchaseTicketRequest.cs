namespace BookingService.Api.Contracts;

/// <summary>
/// Body of POST /events/{eventId}/tickets/{ticketId}/purchase. Both fields are required;
/// if either is missing or blank the request is rejected with 400 Bad Request.
/// </summary>
/// <param name="FullName">Full name of the ticket purchaser (e.g., 'Jane Doe').</param>
/// <param name="Email">Valid RFC 5321 email address for order confirmation (e.g., 'jane.doe@example.com').</param>
public sealed record PurchaseTicketRequest(string FullName, string Email);
