using BookingService.Domain.Venues;

namespace BookingService.Application.Abstractions;

/// <summary>
/// Data from VenueService. Read-only.
/// Resolves the physical seat (<see cref="Domain.EventSeats.EventSeat.VenueSeatId"/>) behind an event seat, e.g. its seat number.
/// </summary>
public interface IVenueSeatCatalog
{
    /// <summary>
    /// Returns the venue seat with the given id, or null if it does not exist.
    /// </summary>
    VenueSeat? GetById(Guid venueSeatId);

    /// <summary>
    /// Returns the venue seats with the given ids. If a seat does not exist, it is not included in the result.
    /// </summary>
    IReadOnlyCollection<VenueSeat> GetByIds(IReadOnlyCollection<Guid> venueSeatIds);
}
