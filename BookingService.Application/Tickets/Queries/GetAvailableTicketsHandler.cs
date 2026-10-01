using BookingService.Application.Repositories;
using BookingService.Application.Tickets.Dtos;
using BookingService.Domain;

namespace BookingService.Application.Tickets.Queries;

/// <summary>
/// Handles retrieving available tickets for an event (SP-04 / APP-01).
/// </summary>
public sealed class GetAvailableTicketsHandler
{
    private readonly ITicketRepository _tickets;
    private readonly IEventCatalog _eventCatalog;

    public GetAvailableTicketsHandler(ITicketRepository tickets, IEventCatalog eventCatalog)
    {
        _tickets = tickets;
        _eventCatalog = eventCatalog;
    }

    /// <summary>
    /// Returns the list of available tickets, or null if the event does not exist.
    /// </summary>
    public IReadOnlyList<SeatAvailabilityDto>? Handle(GetAvailableTicketsQuery query)
    {
        if (!_eventCatalog.Exists(query.EventId))
        {
            return null;
        }

        return _tickets.GetByEvent(query.EventId)
            .Where(t => t.Status == TicketStatus.Available)
            .OrderBy(t => t.SeatNumber, StringComparer.OrdinalIgnoreCase)
            .Select(t => new SeatAvailabilityDto(t.Id, t.SeatNumber, t.Status.ToString()))
            .ToList();
    }
}
