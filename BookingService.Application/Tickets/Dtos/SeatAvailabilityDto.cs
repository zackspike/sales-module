namespace BookingService.Application.Tickets.Dtos;

/// <summary>
/// Unified DTO representing seat availability (SP-04 / ALIGN-02 / APP-01 / APP-02).
/// Matches the agreed seat contract with Frontend: ticketId, seatNumber, and status.
/// </summary>
/// <param name="TicketId">Unique identifier of the ticket (seat).</param>
/// <param name="SeatNumber">Designated seat label within the event venue (e.g., 'A-1').</param>
/// <param name="Status">Current availability status of the seat ('Available' or 'Sold').</param>
public sealed record SeatAvailabilityDto(
    Guid TicketId,
    string SeatNumber,
    string Status);
