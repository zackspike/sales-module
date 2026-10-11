namespace BookingService.Application.EventSeats.ReadModels;

/// <summary>
/// The ticket issued when an event seat is sold, together with the seat it grants.
/// </summary>
/// <param name="EventSeatId">Unique identifier of the sold event seat.</param>
/// <param name="EventId">Unique identifier of the event.</param>
/// <param name="SeatNumber">Venue seat label (e.g., 'A-1').</param>
/// <param name="HolderName">Full name of the ticket holder.</param>
/// <param name="HolderEmail">Email of the ticket holder.</param>
/// <param name="TicketCode">Unique ticket validation code (format: 'TK-{GUID:N}').</param>
/// <param name="SoldAtUtc">UTC time when the seat was sold.</param>
public sealed record IssuedTicket(
    Guid EventSeatId,
    Guid EventId,
    string SeatNumber,
    string HolderName,
    string HolderEmail,
    string TicketCode,
    DateTime SoldAtUtc);
