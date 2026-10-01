using System.Text.Json.Serialization;
using BookingService.Domain.Tickets;

namespace BookingService.Api.Contracts;

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
