namespace BookingService.Application.Tickets.Dtos;

/// <summary>
/// DTO representing the availability status of a specific ticket/seat (SP-04 / APP-02).
/// </summary>
public sealed record TicketAvailabilityDto(
    Guid TicketId,
    Guid EventId,
    string SeatNumber,
    string Status);
