using System.Text.Json.Serialization;
using BookingService.Domain.Tickets;

namespace BookingService.Api.Contracts;

/// <summary>
/// Response payload returning issued ticket details.
/// </summary>
/// <param name="TicketId">Unique identifier of the ticket.</param>
/// <param name="EventId">Unique identifier of the event.</param>
/// <param name="SeatNumber">Designated seat number (e.g., 'A-1').</param>
/// <param name="FullName">Full name of the ticket holder.</param>
/// <param name="Email">Email address associated with the purchase.</param>
/// <param name="TicketCode">Unique ticket validation code (format: 'TK-{GUID:N}').</param>
/// <param name="PurchasedAtUtc">UTC timestamp when the ticket was issued.</param>
public sealed record TicketResponse(
    Guid TicketId,
    Guid EventId,
    string SeatNumber,
    string FullName,
    string Email,
    string TicketCode,
    [property: JsonPropertyName("createdAt")] DateTime? PurchasedAtUtc)
{
    public static TicketResponse From(Ticket ticket) => new(
        ticket.Id,
        ticket.EventId,
        ticket.SeatNumber,
        ticket.FullName,
        ticket.Email,
        ticket.TicketCode,
        ticket.PurchasedAtUtc);
}
