namespace BookingService.Api.Dtos;

//GET /events/{eventId}/tickets
public record TicketSummaryDto(
    string Section,
    decimal Price,
    int AvailableCount
);
