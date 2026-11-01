namespace BookingService.Api.Contracts;

/// <summary>
/// Body of POST /events/{eventId}/tickets/{ticketId}/release.
/// </summary>
/// <param name="Email">Email the seat was reserved with.</param>
public sealed record ReleaseSeatRequest(string Email);
