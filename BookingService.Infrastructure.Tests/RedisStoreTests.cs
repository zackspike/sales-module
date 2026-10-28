using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Infrastructure.Caching;
using StackExchange.Redis;

namespace BookingService.Infrastructure.Tests;

/// <summary>Runs only when ConnectionStrings__Redis points to a Redis instance (CI sets it).</summary>
public sealed class RedisFactAttribute : FactAttribute
{
    public static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Redis");

    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            Skip = "Set ConnectionStrings__Redis to run Redis tests.";
        }
    }
}

public class RedisStoreTests
{
    private static readonly Lazy<IConnectionMultiplexer> Redis =
        new(() => ConnectionMultiplexer.Connect(RedisFactAttribute.ConnectionString!));

    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(1);

    private readonly Guid _eventSeatId = Guid.NewGuid();

    [RedisFact]
    public async Task Seat_lock_is_acquired_renewed_by_its_holder_and_refused_to_others()
    {
        var locks = new RedisSeatLockStore(Redis.Value);

        Assert.Equal(EventSeatLockResult.Acquired, await locks.TryAcquireAsync(
            _eventSeatId, "a", Duration, CancellationToken.None));
        Assert.Equal(EventSeatLockResult.Renewed, await locks.TryAcquireAsync(
            _eventSeatId, "a", Duration, CancellationToken.None));
        Assert.Equal(EventSeatLockResult.HeldByAnother, await locks.TryAcquireAsync(
            _eventSeatId, "b", Duration, CancellationToken.None));
        Assert.Equal("a", await locks.GetHolderIdentifierAsync(
            _eventSeatId, CancellationToken.None));
        Assert.Equal([_eventSeatId], await locks.GetLockedEventSeatIdsAsync(
            [_eventSeatId, Guid.NewGuid()], CancellationToken.None));
    }

    [RedisFact]
    public async Task Concurrent_buyers_lock_the_seat_once()
    {
        var locks = new RedisSeatLockStore(Redis.Value);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(i => locks.TryAcquireAsync(
                    _eventSeatId,
                    $"buyer{i}",
                    Duration,
                    CancellationToken.None)));

        Assert.Single(results, r => r == EventSeatLockResult.Acquired);
        Assert.Equal(19, results.Count(r => r == EventSeatLockResult.HeldByAnother));
    }

    [RedisFact]
    public async Task Seat_lock_expires_and_only_its_holder_releases_it()
    {
        var locks = new RedisSeatLockStore(Redis.Value);

        await locks.TryAcquireAsync(
            _eventSeatId,
            "a",
            TimeSpan.FromMilliseconds(200),
            CancellationToken.None);
        await Task.Delay(400);
        Assert.Null(await locks.GetHolderIdentifierAsync(_eventSeatId, CancellationToken.None));

        await locks.TryAcquireAsync(_eventSeatId, "a", Duration, CancellationToken.None);
        Assert.False(await locks.ReleaseAsync(_eventSeatId, "b", CancellationToken.None));
        Assert.True(await locks.ReleaseAsync(_eventSeatId, "a", CancellationToken.None));
        Assert.Null(await locks.GetHolderIdentifierAsync(_eventSeatId, CancellationToken.None));
    }

    [RedisFact]
    public async Task Idempotency_record_round_trips()
    {
        var store = new RedisIdempotencyStore(Redis.Value);
        var key = Guid.NewGuid();
        var record = new IdempotencyRecord(Guid.NewGuid(), _eventSeatId);

        Assert.Null(await store.GetIdempotencyRecordAsync(key, CancellationToken.None));
        await store.RememberAsync(key, record, Duration, CancellationToken.None);
        Assert.Equal(record, await store.GetIdempotencyRecordAsync(key, CancellationToken.None));
    }

    [RedisFact]
    public async Task Available_seats_cache_round_trips_and_invalidates()
    {
        var cache = new RedisAvailableSeatsCache(Redis.Value);
        var eventId = Guid.NewGuid();
        var seats = new List<AvailableEventSeat> { new(_eventSeatId, "A-1") };

        await cache.SetAsync(eventId, seats, CancellationToken.None);
        Assert.Equal(seats, await cache.GetAsync(eventId, CancellationToken.None));

        await cache.InvalidateAsync(eventId, CancellationToken.None);
        Assert.Null(await cache.GetAsync(eventId, CancellationToken.None));
    }
}
