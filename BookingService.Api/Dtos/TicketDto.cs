namespace BookingService.Api.Dtos;

public record TicketDto(
    Guid TicketId,
    string TicketNumber,
    string Section,
    decimal Price
);
