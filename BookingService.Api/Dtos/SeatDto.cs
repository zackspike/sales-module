namespace BookingService.Api.Dtos;

//GET /events/{eventId}/seats
public record SeatDto(
    Guid Id,
    string Section,
    string SeatNumber,
    decimal Price,
    string Status
);
