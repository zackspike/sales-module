using BookingService.Application.Abstractions;
using BookingService.Application.Tickets.Dtos;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tickets.Queries;

/// <summary>
/// Handles checking the availability of a specific seat (SP-04 / APP-02).
/// The status is <c>Sold</c> or <c>Available</c> as stored in the repository, or
/// <see cref="ReservedStatus"/> when an available seat is locked by a fan.
/// </summary>
public sealed class CheckTicketAvailabilityHandler
{
    public const string ReservedStatus = "Reserved";

    private readonly ITicketRepository _tickets;
    private readonly IEventCatalog _eventCatalog;
    private readonly ISeatLockStore _seatLocks;

    public CheckTicketAvailabilityHandler(ITicketRepository tickets, IEventCatalog eventCatalog, ISeatLockStore seatLocks)
    {
        _tickets = tickets;
        _eventCatalog = eventCatalog;
        _seatLocks = seatLocks;
    }

    /// <summary>
    /// Returns the availability information for the seat, or null if the event or ticket is not found.
    /// </summary>
    public SeatAvailabilityDto? Handle(CheckTicketAvailabilityQuery query)
    {
        if (!_eventCatalog.Exists(query.EventId))
        {
            return null;
        }

        var ticket = _tickets.GetById(query.EventId, query.TicketId);
        if (ticket is null)
        {
            return null;
        }

        var status = ticket.Status == TicketStatus.Available && _seatLocks.GetHolder(ticket.Id) is not null
            ? ReservedStatus
            : ticket.Status.ToString();

        return new SeatAvailabilityDto(ticket.Id, ticket.SeatNumber, status);
    }
}
