using System.ComponentModel.DataAnnotations;

namespace BookingService.Api.Dtos;

//POST /orders/{orderId}/confirm-payment
public record PaymentConfirmationDto(
    [Required] string ExternalTransactionId
);
