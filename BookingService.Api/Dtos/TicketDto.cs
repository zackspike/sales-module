namespace BookingService.Api.Dtos;

public record TicketDto(
    Guid TicketId,
    Guid EventId,
    string FullName,
    string Email,
    string TicketCode,
    DateTime CreatedAtUtc
);
