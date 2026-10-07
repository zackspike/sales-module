namespace BookingService.Api.Dtos;

/// <summary>
/// Body of POST /events/{eventId}/tickets/{ticketId}/purchase. Fields are nullable because
/// they come straight from an untrusted request body; validation happens in Application.
/// </summary>
public sealed record PurchaseTicketRequest(string? FullName, string? Email);
