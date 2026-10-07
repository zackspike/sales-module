using BookingService.Application.Caching;
using BookingService.Application.Repositories;
using BookingService.Application.Tickets.Dtos;
using BookingService.Domain;

namespace BookingService.Application.Tickets.Queries;

/// <summary>
/// Handles checking the availability of a specific seat (SP-04 / APP-02).
/// The status is <c>Sold</c> or <c>Available</c> as stored in the database, or
/// <see cref="ReservedStatus"/> when an available seat is locked by a fan.
/// </summary>
public sealed class CheckTicketAvailabilityHandler
{
    public const string ReservedStatus = "Reserved";

    private readonly ITicketRepository _tickets;
    private readonly IEventRepository _events;
    private readonly ISeatLockStore _seatLocks;

    public CheckTicketAvailabilityHandler(
        ITicketRepository tickets,
        IEventRepository events,
        ISeatLockStore seatLocks)
    {
        _tickets = tickets;
        _events = events;
        _seatLocks = seatLocks;
    }

    /// <summary>
    /// Returns the availability information for the seat, or null if the event or ticket is not found.
    /// </summary>
    public async Task<SeatAvailabilityDto?> HandleAsync(
        CheckTicketAvailabilityQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!await _events.ExistsAsync(query.EventId, cancellationToken))
        {
            return null;
        }

        var ticket = await _tickets.GetByIdAsync(query.EventId, query.TicketId, cancellationToken);
        if (ticket is null)
        {
            return null;
        }

        var status = ticket.Status == TicketStatus.Available
            && await _seatLocks.GetHolderAsync(ticket.Id, cancellationToken) is not null
                ? ReservedStatus
                : ticket.Status.ToString();

        return new SeatAvailabilityDto(ticket.Id, ticket.Seat!.SeatNumber, status);
    }
}
