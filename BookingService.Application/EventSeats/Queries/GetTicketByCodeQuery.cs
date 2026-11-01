namespace BookingService.Application.EventSeats.Queries;

/// <summary>
/// Query to get an issued ticket by its <see cref="TicketCode"/>, e.g. to show it again after the purchase.
/// </summary>
public sealed record GetTicketByCodeQuery(string TicketCode);
