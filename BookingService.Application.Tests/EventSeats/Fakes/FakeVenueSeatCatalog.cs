using BookingService.Application.Abstractions;
using BookingService.Domain.Venues;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeVenueSeatCatalog(params VenueSeat[] venueSeats) : IVenueSeatCatalog
{
    private readonly Dictionary<Guid, VenueSeat> _venueSeats = venueSeats.ToDictionary(seat => seat.Id);

    public Task<VenueSeat?> GetByIdAsync(Guid venueSeatId, CancellationToken cancellationToken) =>
        Task.FromResult(_venueSeats.GetValueOrDefault(venueSeatId));

    public Task<IReadOnlyCollection<VenueSeat>> GetByIdsAsync(
        IReadOnlyCollection<Guid> venueSeatIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<VenueSeat>>(
            venueSeatIds.Where(_venueSeats.ContainsKey).Select(venueSeatId => _venueSeats[venueSeatId]).ToArray());
}
