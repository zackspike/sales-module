namespace BookingService.Application.Tickets.Dtos;

/// <summary>
/// Unified DTO representing seat availability (SP-04 / ALIGN-02 / APP-01 / APP-02).
/// Matches the agreed seat contract with Frontend: ticketId, seatNumber, and status.
/// </summary>
public sealed record SeatAvailabilityDto(
    Guid TicketId,
    string SeatNumber,
    string Status);
