using BookingService.Application.Abstractions;
using BookingService.Application.Tickets.Dtos;
using BookingService.Infrastructure.Caching;
using StackExchange.Redis;

namespace BookingService.Application.Tests.Infrastructure;

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

    private readonly Guid _ticketId = Guid.NewGuid();

    [RedisFact]
    public void Seat_lock_is_acquired_renewed_by_its_holder_and_refused_to_others()
    {
        var locks = new RedisSeatLockStore(Redis.Value);

        Assert.Equal(SeatLockResult.Acquired, locks.TryAcquire(_ticketId, "a", Duration));
        Assert.Equal(SeatLockResult.Renewed, locks.TryAcquire(_ticketId, "a", Duration));
        Assert.Equal(SeatLockResult.HeldByAnother, locks.TryAcquire(_ticketId, "b", Duration));
        Assert.Equal("a", locks.GetHolder(_ticketId));
        Assert.Equal([_ticketId], locks.GetLockedTicketIds([_ticketId, Guid.NewGuid()]));
    }

    [RedisFact]
    public void Concurrent_buyers_lock_the_seat_once()
    {
        var locks = new RedisSeatLockStore(Redis.Value);
        var results = new SeatLockResult[20];

        Parallel.For(0, results.Length, i => results[i] = locks.TryAcquire(_ticketId, $"buyer{i}", Duration));

        Assert.Single(results, r => r == SeatLockResult.Acquired);
        Assert.Equal(19, results.Count(r => r == SeatLockResult.HeldByAnother));
    }

    [RedisFact]
    public void Seat_lock_expires_and_only_its_holder_releases_it()
    {
        var locks = new RedisSeatLockStore(Redis.Value);

        locks.TryAcquire(_ticketId, "a", TimeSpan.FromMilliseconds(200));
        Thread.Sleep(400);
        Assert.Null(locks.GetHolder(_ticketId));

        locks.TryAcquire(_ticketId, "a", Duration);
        Assert.False(locks.Release(_ticketId, "b"));
        Assert.True(locks.Release(_ticketId, "a"));
        Assert.Null(locks.GetHolder(_ticketId));
    }

    [RedisFact]
    public void Idempotency_record_round_trips()
    {
        var store = new RedisIdempotencyStore(Redis.Value);
        var key = Guid.NewGuid();
        var record = new IdempotencyRecord(Guid.NewGuid(), _ticketId);

        Assert.Null(store.Get(key));
        store.Remember(key, record, Duration);
        Assert.Equal(record, store.Get(key));
    }

    [RedisFact]
    public void Available_seats_cache_round_trips_and_invalidates()
    {
        var cache = new RedisAvailableSeatsCache(Redis.Value);
        var eventId = Guid.NewGuid();
        var seats = new List<SeatAvailabilityDto> { new(_ticketId, "A-1", "Available") };

        cache.Set(eventId, seats);
        Assert.Equal(seats, cache.Get(eventId));

        cache.Invalidate(eventId);
        Assert.Null(cache.Get(eventId));
    }
}
