using System.Text.Json.Serialization;
using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Api.Contracts;

/// <summary>
/// An issued ticket looked up by its code, with everything needed to show it again.
/// </summary>
/// <param name="EventId">Unique identifier of the event.</param>
/// <param name="SeatNumber">Designated seat number (e.g., 'A-1').</param>
/// <param name="FullName">Full name of the ticket holder.</param>
/// <param name="Email">Email address associated with the purchase.</param>
/// <param name="TicketCode">Unique ticket validation code (format: 'TK-{GUID:N}').</param>
/// <param name="PurchasedAtUtc">UTC timestamp when the ticket was purchased.</param>
public sealed record TicketDetailsResponse(
    Guid EventId,
    string SeatNumber,
    string FullName,
    string Email,
    string TicketCode,
    [property: JsonPropertyName("createdAt")]
    DateTime PurchasedAtUtc)
{
    public static TicketDetailsResponse From(IssuedTicket ticket) => new(
        ticket.EventId,
        ticket.SeatNumber,
        ticket.HolderName,
        ticket.HolderEmail,
        ticket.TicketCode,
        ticket.SoldAtUtc);
}
