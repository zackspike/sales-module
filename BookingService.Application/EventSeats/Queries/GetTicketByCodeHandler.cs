using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Application.EventSeats.Queries;

/// <summary>
/// Handles getting an issued ticket by its code. The code is random (see <see cref="Domain.Tickets.Ticket"/>),
/// so it is enough to look the ticket up without authentication.
/// </summary>
public sealed class GetTicketByCodeHandler
{
    private readonly IEventSeatRepository _eventSeatRepository;
    private readonly IVenueSeatCatalog _venueSeatCatalog;

    public GetTicketByCodeHandler(IEventSeatRepository eventSeatRepository, IVenueSeatCatalog venueSeatCatalog)
    {
        _eventSeatRepository = eventSeatRepository;
        _venueSeatCatalog = venueSeatCatalog;
    }

    /// <summary>
    /// Returns the ticket with the given code, or null if there is none.
    /// </summary>
    public async Task<IssuedTicket?> HandleAsync(GetTicketByCodeQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.TicketCode))
        {
            return null;
        }

        var sale = await _eventSeatRepository.GetSaleByTicketCodeAsync(query.TicketCode.Trim(), cancellationToken);
        if (sale is null)
        {
            return null;
        }

        var venueSeat = await _venueSeatCatalog.GetByIdAsync(sale.Seat.VenueSeatId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Event seat {sale.Seat.Id} references venue seat {sale.Seat.VenueSeatId}, which is not in the catalog.");

        return IssuedTicket.From(sale, venueSeat.SeatNumber);
    }
}
