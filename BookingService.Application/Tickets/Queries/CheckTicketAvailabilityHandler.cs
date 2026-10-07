using BookingService.Application.Repositories;
using BookingService.Application.Tickets.Dtos;

namespace BookingService.Application.Tickets.Queries;

/// <summary>
/// Handles checking the availability of a specific seat (SP-04 / APP-02).
/// </summary>
public sealed class CheckTicketAvailabilityHandler
{
    private readonly ITicketRepository _tickets;
    private readonly IEventRepository _events;

    public CheckTicketAvailabilityHandler(ITicketRepository tickets, IEventRepository events)
    {
        _tickets = tickets;
        _events = events;
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

        return new SeatAvailabilityDto(
            ticket.Id,
            ticket.Seat!.SeatNumber,
            ticket.Status.ToString());
    }
}
