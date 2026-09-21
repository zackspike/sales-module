using System.ComponentModel.DataAnnotations;

namespace BookingService.Api.Dtos;

//GET /events/{eventId}/tickets
public record TicketSummaryDto(
    string Section,
    decimal Price,
    int AvailableCount
);

//GET /events/{eventId}/seats
public record SeatDto(
    Guid Id,
    string Section,
    string SeatNumber,
    decimal Price,
    string Status
);

//POST /events/{eventId}/reservations
public record CreateReservationDto(
    [Required] List<Guid> SeatIds,
    [Required, EmailAddress] string UserEmail
);

public record ReservationDto(
    Guid ReservationId,
    Guid EventId,
    List<Guid> SeatIds,
    DateTime ExpiresAtUtc
);

//POST /events/{eventId}/orders
public record CreateOrderDto(
    [Required] Guid ReservationId,
    [Required, EmailAddress] string UserEmail
);

//GET /orders/{orderId}
public record OrderDto(
    Guid OrderId,
    Guid ReservationId,
    decimal TotalAmount,
    string Status,
    DateTime CreatedAtUtc
);

//POST /orders/{orderId}/payment
public record PaymentDto(
    [Required] string PaymentMethod
);

public record TicketDto(
    Guid TicketId,
    string TicketNumber,
    string Section,
    decimal Price
);