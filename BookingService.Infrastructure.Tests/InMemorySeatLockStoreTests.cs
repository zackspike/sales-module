using BookingService.Application.Abstractions;
using BookingService.Infrastructure.Caching;

namespace BookingService.Infrastructure.Tests;

public class InMemorySeatLockStoreTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(10);

    private readonly ManualClock _clock = new();
    private readonly InMemorySeatLockStore _locks;
    private readonly Guid _eventSeatId = Guid.NewGuid();

    public InMemorySeatLockStoreTests()
    {
        _locks = new InMemorySeatLockStore(_clock);
    }

    [Fact]
    public void Free_seat_is_acquired_renewed_by_its_holder_and_refused_to_others()
    {
        Assert.Equal(EventSeatLockResult.Acquired, _locks.TryAcquire(_eventSeatId, "a", Duration));
        Assert.Equal(EventSeatLockResult.Renewed, _locks.TryAcquire(_eventSeatId, "a", Duration));
        Assert.Equal(EventSeatLockResult.HeldByAnother, _locks.TryAcquire(_eventSeatId, "b", Duration));
        Assert.Equal("a", _locks.GetHolderIdentifier(_eventSeatId));
    }

    [Fact]
    public void Lock_expires_after_its_duration()
    {
        _locks.TryAcquire(_eventSeatId, "a", Duration);

        _clock.Advance(Duration);

        Assert.Null(_locks.GetHolderIdentifier(_eventSeatId));
        Assert.Empty(_locks.GetLockedEventSeatIds([_eventSeatId]));
        Assert.Equal(EventSeatLockResult.Acquired, _locks.TryAcquire(_eventSeatId, "b", Duration));
    }

    [Fact]
    public void Only_the_holder_can_release_the_lock()
    {
        _locks.TryAcquire(_eventSeatId, "a", Duration);

        Assert.False(_locks.Release(_eventSeatId, "b"));
        Assert.True(_locks.Release(_eventSeatId, "a"));
        Assert.Null(_locks.GetHolderIdentifier(_eventSeatId));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
