using BookingService.Application.Caching;
using BookingService.Application.Tickets.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Infrastructure.Tests;

/// <summary>
/// Tests the Redis implementations of the hot-path stores against a real Redis.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public class RedisStoreTests(InfrastructureFixture infrastructure)
{
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    private readonly ISeatLockStore _seatLocks = infrastructure.Api.Services.GetRequiredService<ISeatLockStore>();
    private readonly IIdempotencyStore _idempotency = infrastructure.Api.Services.GetRequiredService<IIdempotencyStore>();
    private readonly IAvailableSeatsCache _cache = infrastructure.Api.Services.GetRequiredService<IAvailableSeatsCache>();

    [Fact]
    public async Task Only_one_of_many_concurrent_users_acquires_a_seat_lock()
    {
        var ticketId = Guid.NewGuid();
        var users = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();

        var results = await Task.WhenAll(users.Select(userId =>
            Task.Run(() => _seatLocks.TryAcquireAsync(ticketId, userId, TenMinutes))));

        Assert.Single(results, r => r == SeatLockResult.Acquired);
        Assert.Equal(49, results.Count(r => r == SeatLockResult.HeldByAnotherUser));
        var holder = await _seatLocks.GetHolderAsync(ticketId);
        Assert.Equal(users[Array.IndexOf(results, SeatLockResult.Acquired)], holder);
    }

    [Fact]
    public async Task Holder_renews_the_lock_and_others_stay_out()
    {
        var ticketId = Guid.NewGuid();
        var holder = Guid.NewGuid();
        await _seatLocks.TryAcquireAsync(ticketId, holder, TenMinutes);

        Assert.Equal(SeatLockResult.Renewed, await _seatLocks.TryAcquireAsync(ticketId, holder, TenMinutes));
        Assert.Equal(SeatLockResult.HeldByAnotherUser, await _seatLocks.TryAcquireAsync(ticketId, Guid.NewGuid(), TenMinutes));
    }

    [Fact]
    public async Task Lock_expires_after_its_ttl()
    {
        var ticketId = Guid.NewGuid();
        await _seatLocks.TryAcquireAsync(ticketId, Guid.NewGuid(), TimeSpan.FromMilliseconds(300));

        await Task.Delay(TimeSpan.FromMilliseconds(800));

        Assert.Null(await _seatLocks.GetHolderAsync(ticketId));
        Assert.Equal(SeatLockResult.Acquired, await _seatLocks.TryAcquireAsync(ticketId, Guid.NewGuid(), TenMinutes));
    }

    [Fact]
    public async Task Only_the_holder_can_release_the_lock()
    {
        var ticketId = Guid.NewGuid();
        var holder = Guid.NewGuid();
        await _seatLocks.TryAcquireAsync(ticketId, holder, TenMinutes);

        Assert.False(await _seatLocks.ReleaseAsync(ticketId, Guid.NewGuid()));
        Assert.Equal(holder, await _seatLocks.GetHolderAsync(ticketId));
        Assert.True(await _seatLocks.ReleaseAsync(ticketId, holder));
        Assert.Null(await _seatLocks.GetHolderAsync(ticketId));
    }

    [Fact]
    public async Task Locked_ticket_ids_are_reported_in_one_lookup()
    {
        var locked = Guid.NewGuid();
        var free = Guid.NewGuid();
        await _seatLocks.TryAcquireAsync(locked, Guid.NewGuid(), TenMinutes);

        var result = await _seatLocks.GetLockedTicketIdsAsync([locked, free]);

        Assert.Equal([locked], result);
    }

    [Fact]
    public async Task Idempotency_records_are_remembered_until_they_expire()
    {
        var key = Guid.NewGuid();
        var record = new IdempotencyRecord(Guid.NewGuid(), Guid.NewGuid());

        await _idempotency.RememberAsync(key, record, TimeSpan.FromMilliseconds(300));

        Assert.Equal(record, await _idempotency.GetAsync(key));
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        Assert.Null(await _idempotency.GetAsync(key));
    }

    [Fact]
    public async Task Available_seats_cache_round_trips_and_invalidates()
    {
        var eventId = Guid.NewGuid();
        SeatAvailabilityDto[] seats = [new(Guid.NewGuid(), "A-1", "Available"), new(Guid.NewGuid(), "A-2", "Available")];

        Assert.Null(await _cache.GetAsync(eventId));
        await _cache.SetAsync(eventId, seats);
        Assert.Equal(seats, await _cache.GetAsync(eventId));

        await _cache.InvalidateAsync(eventId);
        Assert.Null(await _cache.GetAsync(eventId));
    }
}
