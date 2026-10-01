namespace BookingService.Application.Tickets.Dtos;

/// <summary>
/// DTO representing an available seat in an event (SP-04 / ALIGN-02 / APP-01).
/// </summary>
public sealed record AvailableTicketDto(
    Guid TicketId,
    string SeatNumber,
    string Status);
