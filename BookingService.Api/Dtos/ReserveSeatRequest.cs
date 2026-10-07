namespace BookingService.Api.Dtos;

/// <summary>
/// Body of POST /events/{eventId}/tickets/{ticketId}/reserve. The email identifies the buyer
/// who will hold the seat; the purchase must be made with the same email.
/// </summary>
public sealed record ReserveSeatRequest(string? FullName, string? Email);
