using System.Text.Json.Serialization;
using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Api.Contracts;

/// <summary>
/// Response payload returning issued ticket details.
/// </summary>
/// <param name="SeatNumber">Designated seat number (e.g., 'A-1').</param>
/// <param name="TicketCode">Unique ticket validation code (format: 'TK-{GUID:N}').</param>
/// <param name="PurchasedAtUtc">UTC timestamp when the ticket was purchased.</param>
public sealed record TicketResponse(
    string SeatNumber,
    string TicketCode,
    [property: JsonPropertyName("createdAt")]
    DateTime? PurchasedAtUtc)
{
    public static TicketResponse From(IssuedTicket ticket) => new(
        ticket.SeatNumber,
        ticket.TicketCode,
        ticket.SoldAtUtc);
}
