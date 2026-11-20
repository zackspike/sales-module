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
    public async Task Free_seat_is_acquired_renewed_by_its_holder_and_refused_to_others()
    {
        Assert.Equal(EventSeatLockResult.Acquired, await _locks.TryAcquireAsync(
            _eventSeatId, "a", Duration, CancellationToken.None));
        Assert.Equal(EventSeatLockResult.Renewed, await _locks.TryAcquireAsync(
            _eventSeatId, "a", Duration, CancellationToken.None));
        Assert.Equal(EventSeatLockResult.HeldByAnother, await _locks.TryAcquireAsync(
            _eventSeatId, "b", Duration, CancellationToken.None));
        Assert.Equal("a", await _locks.GetHolderIdentifierAsync(
            _eventSeatId, CancellationToken.None));
    }

    [Fact]
    public async Task Lock_expires_after_its_duration()
    {
        await _locks.TryAcquireAsync(_eventSeatId, "a", Duration, CancellationToken.None);

        _clock.Advance(Duration);

        Assert.Null(await _locks.GetHolderIdentifierAsync(_eventSeatId, CancellationToken.None));
        Assert.Empty(await _locks.GetLockedEventSeatIdsAsync(
            [_eventSeatId], CancellationToken.None));
        Assert.Equal(EventSeatLockResult.Acquired, await _locks.TryAcquireAsync(
            _eventSeatId, "b", Duration, CancellationToken.None));
    }

    [Fact]
    public async Task Only_the_holder_can_release_the_lock()
    {
        await _locks.TryAcquireAsync(_eventSeatId, "a", Duration, CancellationToken.None);

        Assert.False(await _locks.ReleaseAsync(_eventSeatId, "b", CancellationToken.None));
        Assert.True(await _locks.ReleaseAsync(_eventSeatId, "a", CancellationToken.None));
        Assert.Null(await _locks.GetHolderIdentifierAsync(_eventSeatId, CancellationToken.None));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
