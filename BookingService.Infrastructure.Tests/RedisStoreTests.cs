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
    public void Seat_lock_is_acquired_renewed_by_its_holder_and_refused_to_others()
    {
        var locks = new RedisSeatLockStore(Redis.Value);

        Assert.Equal(EventSeatLockResult.Acquired, locks.TryAcquire(_eventSeatId, "a", Duration));
        Assert.Equal(EventSeatLockResult.Renewed, locks.TryAcquire(_eventSeatId, "a", Duration));
        Assert.Equal(EventSeatLockResult.HeldByAnother, locks.TryAcquire(_eventSeatId, "b", Duration));
        Assert.Equal("a", locks.GetHolderIdentifier(_eventSeatId));
        Assert.Equal([_eventSeatId], locks.GetLockedEventSeatIds([_eventSeatId, Guid.NewGuid()]));
    }

    [RedisFact]
    public void Concurrent_buyers_lock_the_seat_once()
    {
        var locks = new RedisSeatLockStore(Redis.Value);
        var results = new EventSeatLockResult[20];

        Parallel.For(0, results.Length, i => results[i] = locks.TryAcquire(_eventSeatId, $"buyer{i}", Duration));

        Assert.Single(results, r => r == EventSeatLockResult.Acquired);
        Assert.Equal(19, results.Count(r => r == EventSeatLockResult.HeldByAnother));
    }

    [RedisFact]
    public void Seat_lock_expires_and_only_its_holder_releases_it()
    {
        var locks = new RedisSeatLockStore(Redis.Value);

        locks.TryAcquire(_eventSeatId, "a", TimeSpan.FromMilliseconds(200));
        Thread.Sleep(400);
        Assert.Null(locks.GetHolderIdentifier(_eventSeatId));

        locks.TryAcquire(_eventSeatId, "a", Duration);
        Assert.False(locks.Release(_eventSeatId, "b"));
        Assert.True(locks.Release(_eventSeatId, "a"));
        Assert.Null(locks.GetHolderIdentifier(_eventSeatId));
    }

    [RedisFact]
    public void Idempotency_record_round_trips()
    {
        var store = new RedisIdempotencyStore(Redis.Value);
        var key = Guid.NewGuid();
        var record = new IdempotencyRecord(Guid.NewGuid(), _eventSeatId);

        Assert.Null(store.GetIdempotencyRecord(key));
        store.Remember(key, record, Duration);
        Assert.Equal(record, store.GetIdempotencyRecord(key));
    }

    [RedisFact]
    public void Available_seats_cache_round_trips_and_invalidates()
    {
        var cache = new RedisAvailableSeatsCache(Redis.Value);
        var eventId = Guid.NewGuid();
        var seats = new List<AvailableEventSeat> { new(_eventSeatId, "A-1") };

        cache.Set(eventId, seats);
        Assert.Equal(seats, cache.Get(eventId));

        cache.Invalidate(eventId);
        Assert.Null(cache.Get(eventId));
    }
}
