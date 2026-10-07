using System.Text.Json.Serialization;

namespace BookingService.Api.Dtos;

public record TicketDto(
    Guid TicketId,
    Guid EventId,
    string SeatNumber,
    string FullName,
    string Email,
    string TicketCode,
    // Issuance time of the ticket (its purchase); "createdAt" is the agreed contract name (SP-05).
    [property: JsonPropertyName("createdAt")] DateTime PurchasedAtUtc
);
