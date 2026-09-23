namespace BookingService.Api.Dtos;

public record ReservationDto(
    Guid ReservationId,
    Guid EventId,
    List<Guid> SeatIds,
    DateTime ExpiresAtUtc
);
