namespace BookingService.Api.Dtos;

//GET /orders/{orderId}
public record OrderDto(
    Guid OrderId,
    Guid ReservationId,
    decimal TotalAmount,
    string Status,
    DateTime CreatedAtUtc
);
