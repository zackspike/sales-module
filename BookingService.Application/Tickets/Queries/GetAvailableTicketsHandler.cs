using BookingService.Application.Repositories;
using BookingService.Application.Tickets.Dtos;

namespace BookingService.Application.Tickets.Queries;

/// <summary>
/// Handles retrieving available tickets for an event (SP-04 / APP-01).
/// </summary>
public sealed class GetAvailableTicketsHandler
{
    private readonly ITicketRepository _tickets;
    private readonly IEventRepository _events;

    public GetAvailableTicketsHandler(ITicketRepository tickets, IEventRepository events)
    {
        _tickets = tickets;
        _events = events;
    }

    /// <summary>
    /// Returns the list of available tickets, or null if the event does not exist.
    /// </summary>
    public async Task<IReadOnlyList<SeatAvailabilityDto>?> HandleAsync(
        GetAvailableTicketsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!await _events.ExistsAsync(query.EventId, cancellationToken))
        {
            return null;
        }

        var tickets = await _tickets.GetAvailableByEventAsync(query.EventId, cancellationToken);

        return tickets
            .OrderBy(t => t.Seat!.SeatNumber, StringComparer.OrdinalIgnoreCase)
            .Select(t => new SeatAvailabilityDto(t.Id, t.Seat!.SeatNumber, t.Status.ToString()))
            .ToList();
    }
}
