using System.Text.Json.Serialization;

namespace BookingService.Api.Contracts;

public record TicketResponse(
    Guid TicketId,
    Guid EventId,
    string FullName,
    string Email,
    string TicketCode,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAtUtc
);
