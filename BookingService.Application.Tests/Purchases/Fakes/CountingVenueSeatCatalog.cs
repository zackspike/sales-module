using BookingService.Application.Abstractions;
using BookingService.Domain.Venues;

namespace BookingService.Application.Tests.Purchases.Fakes;

/// <summary>
/// Venue seat catalog that counts its batch lookups and can fail every lookup after a given number, to simulate
/// VenueService or its cache going down mid-request.
/// </summary>
public sealed class CountingVenueSeatCatalog(params VenueSeat[] venueSeats) : IVenueSeatCatalog
{
    private readonly Dictionary<Guid, VenueSeat> _venueSeats = venueSeats.ToDictionary(seat => seat.Id);

    public int GetByIdsCalls { get; private set; }

    /// <summary>Lookups after this many batch calls throw; null never fails.</summary>
    public int? FailAfterCalls { get; init; }

    public Task<VenueSeat?> GetByIdAsync(Guid venueSeatId, CancellationToken cancellationToken) =>
        Task.FromResult(_venueSeats.GetValueOrDefault(venueSeatId));

    public Task<IReadOnlyCollection<VenueSeat>> GetByIdsAsync(
        IReadOnlyCollection<Guid> venueSeatIds,
        CancellationToken cancellationToken)
    {
        GetByIdsCalls++;
        if (GetByIdsCalls > FailAfterCalls)
        {
            throw new HttpRequestException("The venue seat catalog is unavailable.");
        }

        return Task.FromResult<IReadOnlyCollection<VenueSeat>>(
            venueSeatIds.Where(_venueSeats.ContainsKey).Select(venueSeatId => _venueSeats[venueSeatId]).ToArray());
    }
}
