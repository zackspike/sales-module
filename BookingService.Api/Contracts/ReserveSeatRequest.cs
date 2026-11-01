namespace BookingService.Api.Contracts;

/// <summary>
/// Body of POST /events/{eventId}/tickets/{ticketId}/reserve. The email identifies the buyer
/// who will hold the seat; the purchase must be made with the same email.
/// </summary>
/// <param name="Email">Email of the buyer; it must match the one used to purchase the seat.</param>
public sealed record ReserveSeatRequest(string Email);
