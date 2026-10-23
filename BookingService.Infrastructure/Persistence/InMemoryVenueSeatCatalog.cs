using BookingService.Application.Abstractions;
using BookingService.Domain.Venues;

namespace BookingService.Infrastructure.Persistence;

/// <summary>
/// In-memory mock catalog of venue seats (SP-03 / ALIGN-02). Pre-seeded with the same 50 seats the
/// <c>SeedDefaultEvent</c> migration uses, so event-seat inventory is consistent between the in-memory
/// and the PostgreSQL path. Replace with a VenueService client once that service is integrated.
/// </summary>
public sealed class InMemoryVenueSeatCatalog : IVenueSeatCatalog
{
    /// <summary>
    /// Zone that owns the default event's 50 venue seats.
    /// </summary>
    public static readonly Guid DefaultZoneId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");

    private readonly Dictionary<Guid, VenueSeat> _venueSeats = new();

    public InMemoryVenueSeatCatalog()
    {
        foreach (var venueSeat in DefaultVenueSeats)
        {
            _venueSeats[venueSeat.Id] = venueSeat;
        }
    }

    /// <summary>
    /// The 50 venue seats (A-1..A-50) of the default event's venue. Their ids are deterministic
    /// so the seed migration can reference the same physical seats.
    /// </summary>
    public static IReadOnlyList<VenueSeat> DefaultVenueSeats { get; } = Enumerable.Range(1, 50)
        .Select(n => new VenueSeat(
            Guid.Parse($"{n:D8}-0000-0000-0000-000000000000"),
            DefaultZoneId,
            $"A-{n}"))
        .ToList();

    public VenueSeat? GetById(Guid venueSeatId) =>
        _venueSeats.TryGetValue(venueSeatId, out var venueSeat) ? venueSeat : null;

    public IReadOnlyCollection<VenueSeat> GetByIds(IReadOnlyCollection<Guid> venueSeatIds)
    {
        if (venueSeatIds.Count == 0)
        {
            return [];
        }

        return venueSeatIds
            .Where(_venueSeats.ContainsKey)
            .Select(id => _venueSeats[id])
            .ToList();
    }
}
