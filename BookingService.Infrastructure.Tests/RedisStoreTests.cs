using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.ReadModels;
using BookingService.Domain.Purchases;
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

    private static RedisKey SessionKey(Guid purchaseId) => $"booking:session:{purchaseId}";

    private static RedisKey SeatLockKey(Guid eventSeatId) => $"booking:seat-lock:{eventSeatId}";

    private static PurchaseSession NewSession(TimeSpan duration) =>
        PurchaseSession.Start(Guid.NewGuid(), "Jane Doe", "jane.doe@example.com", DateTime.UtcNow, duration);

    private static async Task<bool> TrySaveAsync(RedisPurchaseSessionStore store, PurchaseSession purchase)
    {
        try
        {
            await store.SaveAsync(purchase, CancellationToken.None);
            return true;
        }
        catch (PurchaseSessionConcurrencyException)
        {
            return false;
        }
    }

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
    public async Task Event_seats_cache_round_trips_and_invalidates()
    {
        var cache = new RedisEventSeatsCache(Redis.Value);
        var eventId = Guid.NewGuid();
        var seats = new List<EventSeatAvailability> { new(_eventSeatId, "A-1", EventSeatAvailabilityStatus.Sold) };

        await cache.SetAsync(eventId, seats, CancellationToken.None);
        Assert.Equal(seats, await cache.GetAsync(eventId, CancellationToken.None));

        await cache.InvalidateAsync(eventId, CancellationToken.None);
        Assert.Null(await cache.GetAsync(eventId, CancellationToken.None));
    }

    [RedisFact]
    public async Task Purchase_session_round_trips_with_its_deadline_token_and_held_seats()
    {
        var store = new RedisPurchaseSessionStore(Redis.Value);
        var db = Redis.Value.GetDatabase();
        var purchase = NewSession(Duration);
        var seat = Guid.NewGuid();
        var key = SessionKey(purchase.Id);
        purchase.Hold(purchase.EventId, seat, 3, DateTime.UtcNow);
        purchase.SetPaymentToken("pay_tok_test");

        try
        {
            await store.SaveAsync(purchase, CancellationToken.None);

            var loaded = await store.GetAsync(purchase.Id, CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Equal(purchase.Id, loaded.Id);
            Assert.Equal(purchase.EventId, loaded.EventId);
            Assert.Equal(purchase.Name, loaded.Name);
            Assert.Equal(purchase.Email, loaded.Email);
            Assert.Equal(purchase.ExpiresAtUtc, loaded.ExpiresAtUtc);
            Assert.Equal([seat], loaded.HeldEventSeatIds);
            Assert.Equal("pay_tok_test", loaded.PaymentToken);

            var ttl = db.KeyTimeToLive(key);
            Assert.NotNull(ttl);
            Assert.True(ttl > TimeSpan.FromSeconds(50));
            Assert.True(ttl <= Duration);
        }
        finally
        {
            await db.KeyDeleteAsync(key);
        }
    }

    [RedisFact]
    public async Task Saving_a_session_again_never_extends_its_deadline()
    {
        var store = new RedisPurchaseSessionStore(Redis.Value);
        var db = Redis.Value.GetDatabase();
        var purchase = NewSession(TimeSpan.FromSeconds(3));
        var key = SessionKey(purchase.Id);

        try
        {
            await store.SaveAsync(purchase, CancellationToken.None);
            await Task.Delay(300);

            var before = db.KeyTimeToLive(key);
            await store.SaveAsync(purchase, CancellationToken.None);
            var after = db.KeyTimeToLive(key);

            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.True(after <= before);

            var loaded = await store.GetAsync(purchase.Id, CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Equal(purchase.ExpiresAtUtc, loaded.ExpiresAtUtc);
        }
        finally
        {
            await db.KeyDeleteAsync(key);
        }
    }

    [RedisFact]
    public async Task Concurrent_writes_to_one_session_conflict()
    {
        var store = new RedisPurchaseSessionStore(Redis.Value);
        var db = Redis.Value.GetDatabase();
        var purchase = NewSession(Duration);
        var key = SessionKey(purchase.Id);

        try
        {
            await store.SaveAsync(purchase, CancellationToken.None);

            var first = await store.GetAsync(purchase.Id, CancellationToken.None);
            var second = await store.GetAsync(purchase.Id, CancellationToken.None);
            Assert.NotNull(first);
            Assert.NotNull(second);

            first.Hold(first.EventId, Guid.NewGuid(), 3, DateTime.UtcNow);
            second.Hold(second.EventId, Guid.NewGuid(), 3, DateTime.UtcNow);

            var results = await Task.WhenAll(
                TrySaveAsync(store, first),
                TrySaveAsync(store, second));

            Assert.Single(results, saved => saved);
            Assert.Single(results, saved => !saved);

            var stored = await store.GetAsync(purchase.Id, CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Single(stored.HeldEventSeatIds);
        }
        finally
        {
            await db.KeyDeleteAsync(key);
        }
    }

    [RedisFact]
    public async Task Two_purchases_compete_for_one_seat_with_the_session_deadline()
    {
        var locks = new RedisSeatLockStore(Redis.Value);
        var db = Redis.Value.GetDatabase();
        var first = NewSession(Duration);
        var second = NewSession(Duration);
        var seat = Guid.NewGuid();
        var key = SeatLockKey(seat);
        var remaining = first.ExpiresAtUtc - DateTime.UtcNow;

        try
        {
            var results = await Task.WhenAll(
                locks.TryAcquireAsync(seat, first.Id.ToString(), remaining, CancellationToken.None),
                locks.TryAcquireAsync(seat, second.Id.ToString(), remaining, CancellationToken.None));

            Assert.Single(results, result => result == EventSeatLockResult.Acquired);
            Assert.Single(results, result => result == EventSeatLockResult.HeldByAnother);

            var winner = results[0] == EventSeatLockResult.Acquired ? first : second;
            var loser = winner == first ? second : first;

            Assert.Equal(winner.Id.ToString(), await locks.GetHolderIdentifierAsync(seat, CancellationToken.None));

            var ttl = db.KeyTimeToLive(key);
            Assert.NotNull(ttl);
            Assert.True(ttl > TimeSpan.FromSeconds(50));
            Assert.True(ttl <= Duration);

            Assert.False(await locks.ReleaseAsync(seat, loser.Id.ToString(), CancellationToken.None));
            Assert.True(await locks.ReleaseAsync(seat, winner.Id.ToString(), CancellationToken.None));
        }
        finally
        {
            await db.KeyDeleteAsync(key);
        }
    }
}
