using BookingService.Application.Abstractions;
using BookingService.Domain.Venues;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeVenueSeatCatalog(params VenueSeat[] venueSeats) : IVenueSeatCatalog
{
    private readonly Dictionary<Guid, VenueSeat> _venueSeats = venueSeats.ToDictionary(seat => seat.Id);

    public VenueSeat? GetById(Guid venueSeatId) => _venueSeats.GetValueOrDefault(venueSeatId);

    public IReadOnlyCollection<VenueSeat> GetByIds(IReadOnlyCollection<Guid> venueSeatIds) =>
        venueSeatIds.Where(_venueSeats.ContainsKey).Select(venueSeatId => _venueSeats[venueSeatId]).ToArray();
}
