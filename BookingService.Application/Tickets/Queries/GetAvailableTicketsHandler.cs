using BookingService.Application.Caching;
using BookingService.Application.Repositories;
using BookingService.Application.Tickets.Dtos;

namespace BookingService.Application.Tickets.Queries;

/// <summary>
/// Handles retrieving available tickets for an event (SP-04 / APP-01). The seats available in the
/// database are served from <see cref="IAvailableSeatsCache"/> when possible, and seats currently
/// reserved by a fan (seat lock) are left out.
/// </summary>
public sealed class GetAvailableTicketsHandler
{
    private readonly ITicketRepository _tickets;
    private readonly IEventRepository _events;
    private readonly IAvailableSeatsCache _cache;
    private readonly ISeatLockStore _seatLocks;

    public GetAvailableTicketsHandler(
        ITicketRepository tickets,
        IEventRepository events,
        IAvailableSeatsCache cache,
        ISeatLockStore seatLocks)
    {
        _tickets = tickets;
        _events = events;
        _cache = cache;
        _seatLocks = seatLocks;
    }

    /// <summary>
    /// Returns the list of available tickets, or null if the event does not exist.
    /// </summary>
    public async Task<IReadOnlyList<SeatAvailabilityDto>?> HandleAsync(
        GetAvailableTicketsQuery query,
        CancellationToken cancellationToken = default)
    {
        // Only existing events are cached, so a hit skips the database entirely.
        var seats = await _cache.GetAsync(query.EventId, cancellationToken);
        if (seats is null)
        {
            if (!await _events.ExistsAsync(query.EventId, cancellationToken))
            {
                return null;
            }

            var tickets = await _tickets.GetAvailableByEventAsync(query.EventId, cancellationToken);
            seats = tickets
                .OrderBy(t => t.Seat!.SeatNumber, StringComparer.OrdinalIgnoreCase)
                .Select(t => new SeatAvailabilityDto(t.Id, t.Seat!.SeatNumber, t.Status.ToString()))
                .ToList();

            await _cache.SetAsync(query.EventId, seats, cancellationToken);
        }

        var reserved = await _seatLocks.GetLockedTicketIdsAsync(
            seats.Select(s => s.TicketId).ToList(),
            cancellationToken);

        return seats.Where(s => !reserved.Contains(s.TicketId)).ToList();
    }
}
