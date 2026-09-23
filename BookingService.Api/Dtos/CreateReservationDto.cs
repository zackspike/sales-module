using System.ComponentModel.DataAnnotations;

namespace BookingService.Api.Dtos;

//POST /events/{eventId}/reservations
public record CreateReservationDto(
    [Required] List<Guid> SeatIds,
    [Required, EmailAddress] string UserEmail
);
