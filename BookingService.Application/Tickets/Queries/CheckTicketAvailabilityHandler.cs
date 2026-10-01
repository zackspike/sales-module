using BookingService.Application.Repositories;
using BookingService.Application.Tickets.Dtos;

namespace BookingService.Application.Tickets.Queries;

/// <summary>
/// Handles checking the availability of a specific seat (SP-04 / APP-02).
/// </summary>
public sealed class CheckTicketAvailabilityHandler
{
    private readonly ITicketRepository _tickets;
    private readonly IEventCatalog _eventCatalog;

    public CheckTicketAvailabilityHandler(ITicketRepository tickets, IEventCatalog eventCatalog)
    {
        _tickets = tickets;
        _eventCatalog = eventCatalog;
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

        return new SeatAvailabilityDto(
            ticket.Id,
            ticket.SeatNumber,
            ticket.Status.ToString());
    }
}
