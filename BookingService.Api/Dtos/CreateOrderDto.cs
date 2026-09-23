using System.ComponentModel.DataAnnotations;

namespace BookingService.Api.Dtos;

//POST /events/{eventId}/orders
public record CreateOrderDto(
    [Required] Guid ReservationId,
    [Required, EmailAddress] string UserEmail
);
