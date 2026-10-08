using BookingService.Application.Abstractions;
using BookingService.Infrastructure.Caching;

namespace BookingService.Application.Tests.Infrastructure;

public class InMemorySeatLockStoreTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(10);

    private readonly ManualClock _clock = new();
    private readonly InMemorySeatLockStore _locks;
    private readonly Guid _ticketId = Guid.NewGuid();

    public InMemorySeatLockStoreTests()
    {
        _locks = new InMemorySeatLockStore(_clock);
    }

    [Fact]
    public void Free_seat_is_acquired_renewed_by_its_holder_and_refused_to_others()
    {
        Assert.Equal(SeatLockResult.Acquired, _locks.TryAcquire(_ticketId, "a", Duration));
        Assert.Equal(SeatLockResult.Renewed, _locks.TryAcquire(_ticketId, "a", Duration));
        Assert.Equal(SeatLockResult.HeldByAnother, _locks.TryAcquire(_ticketId, "b", Duration));
        Assert.Equal("a", _locks.GetHolder(_ticketId));
    }

    [Fact]
    public void Lock_expires_after_its_duration()
    {
        _locks.TryAcquire(_ticketId, "a", Duration);

        _clock.Advance(Duration);

        Assert.Null(_locks.GetHolder(_ticketId));
        Assert.Empty(_locks.GetLockedTicketIds([_ticketId]));
        Assert.Equal(SeatLockResult.Acquired, _locks.TryAcquire(_ticketId, "b", Duration));
    }

    [Fact]
    public void Only_the_holder_can_release_the_lock()
    {
        _locks.TryAcquire(_ticketId, "a", Duration);

        Assert.False(_locks.Release(_ticketId, "b"));
        Assert.True(_locks.Release(_ticketId, "a"));
        Assert.Null(_locks.GetHolder(_ticketId));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
