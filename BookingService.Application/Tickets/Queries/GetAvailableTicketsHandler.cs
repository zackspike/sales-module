using BookingService.Application.Abstractions;
using BookingService.Application.Tickets.Dtos;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tickets.Queries;

/// <summary>
/// Handles retrieving available tickets for an event (SP-04 / APP-01). The seats available in the
/// repository are served from <see cref="IAvailableSeatsCache"/> when possible, and seats currently
/// reserved by a fan (seat lock) are left out.
/// </summary>
public sealed class GetAvailableTicketsHandler
{
    private readonly ITicketRepository _tickets;
    private readonly IEventCatalog _eventCatalog;
    private readonly IAvailableSeatsCache _cache;
    private readonly ISeatLockStore _seatLocks;

    public GetAvailableTicketsHandler(
        ITicketRepository tickets,
        IEventCatalog eventCatalog,
        IAvailableSeatsCache cache,
        ISeatLockStore seatLocks)
    {
        _tickets = tickets;
        _eventCatalog = eventCatalog;
        _cache = cache;
        _seatLocks = seatLocks;
    }

    /// <summary>
    /// Returns the list of available tickets, or null if the event does not exist.
    /// </summary>
    public IReadOnlyList<SeatAvailabilityDto>? Handle(GetAvailableTicketsQuery query)
    {
        // Only existing events are cached, so a hit skips the event check too.
        var seats = _cache.Get(query.EventId);
        if (seats is null)
        {
            if (!_eventCatalog.Exists(query.EventId))
            {
                return null;
            }

            seats = _tickets.GetByEvent(query.EventId)
                .Where(t => t.Status == TicketStatus.Available)
                .OrderBy(t => t.SeatNumber, StringComparer.OrdinalIgnoreCase)
                .Select(t => new SeatAvailabilityDto(t.Id, t.SeatNumber, t.Status.ToString()))
                .ToList();

            _cache.Set(query.EventId, seats);
        }

        var reserved = _seatLocks.GetLockedTicketIds(seats.Select(s => s.TicketId).ToList());

        return seats.Where(s => !reserved.Contains(s.TicketId)).ToList();
    }
}
