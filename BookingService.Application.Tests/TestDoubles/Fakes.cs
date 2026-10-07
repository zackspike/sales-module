using BookingService.Application.Caching;
using BookingService.Application.Repositories;
using BookingService.Application.Tickets.Dtos;
using BookingService.Domain;

namespace BookingService.Application.Tests.TestDoubles;

/// <summary>
/// Builds tickets with the seat/zone graph the repositories return.
/// </summary>
internal static class TestTickets
{
    public static Ticket Create(Guid eventId, string seatNumber, TicketStatus status = TicketStatus.Available)
    {
        var zone = new Zone { Id = Guid.NewGuid(), EventId = eventId };
        var seat = new Seat { Id = Guid.NewGuid(), ZoneId = zone.Id, Zone = zone, SeatNumber = seatNumber };

        return new Ticket
        {
            Id = Guid.NewGuid(),
            SeatId = seat.Id,
            Seat = seat,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}

internal sealed class FakeEventRepository(params Guid[] knownEvents) : IEventRepository
{
    private readonly HashSet<Guid> _events = [.. knownEvents];

    public int ExistsCalls { get; private set; }

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        ExistsCalls++;
        return Task.FromResult(_events.Contains(eventId));
    }

    public Task<Event?> GetByIdAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Contains(eventId) ? new Event { Id = eventId } : null);

    public void Add(Event @event) => _events.Add(@event.Id);
}

internal sealed class FakeTicketRepository(params Ticket[] tickets) : ITicketRepository
{
    private readonly List<Ticket> _tickets = [.. tickets];

    public void AddRange(Guid eventId, IEnumerable<Ticket> tickets) => _tickets.AddRange(tickets);

    public Task<IReadOnlyList<Ticket>> GetAvailableByEventAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Ticket>>(_tickets
            .Where(t => t.Status == TicketStatus.Available && t.Seat!.Zone!.EventId == eventId)
            .ToList());

    public Task<Ticket?> GetByIdAsync(Guid eventId, Guid ticketId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tickets.FirstOrDefault(t => t.Id == ticketId && t.Seat!.Zone!.EventId == eventId));

    public Task<Ticket?> GetForPurchaseAsync(Guid eventId, Guid ticketId, CancellationToken cancellationToken = default) =>
        GetByIdAsync(eventId, ticketId, cancellationToken);

    public Task<Ticket?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tickets.FirstOrDefault(t => t.IdempotencyKey == idempotencyKey));
}

internal sealed class FakeUserRepository : IUserRepository
{
    public List<User> Users { get; } = [];

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Email == email));

    public void Add(User user) => Users.Add(user);
}

/// <summary>
/// Runs one "transaction" at a time, like the row lock taken by the real purchase.
/// </summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly SemaphoreSlim _transactionLock = new(1, 1);

    public int SaveChangesCalls { get; private set; }
    public int Transactions { get; private set; }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        await _transactionLock.WaitAsync(cancellationToken);
        Transactions++;
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCalls++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory seat locks without expiration; <see cref="Expire"/> simulates the TTL running out.
/// </summary>
internal sealed class FakeSeatLockStore : ISeatLockStore
{
    private readonly Dictionary<Guid, Guid> _holders = new();
    private readonly Lock _lock = new();

    public Task<SeatLockResult> TryAcquireAsync(
        Guid ticketId,
        Guid userId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_holders.TryGetValue(ticketId, out var holder))
            {
                _holders[ticketId] = userId;
                return Task.FromResult(SeatLockResult.Acquired);
            }

            return Task.FromResult(holder == userId ? SeatLockResult.Renewed : SeatLockResult.HeldByAnotherUser);
        }
    }

    public Task<Guid?> GetHolderAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<Guid?>(_holders.TryGetValue(ticketId, out var holder) ? holder : null);
        }
    }

    public Task<IReadOnlySet<Guid>> GetLockedTicketIdsAsync(
        IReadOnlyCollection<Guid> ticketIds,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlySet<Guid>>(ticketIds.Where(_holders.ContainsKey).ToHashSet());
        }
    }

    public Task<bool> ReleaseAsync(Guid ticketId, Guid userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var released = _holders.TryGetValue(ticketId, out var holder) && holder == userId
                && _holders.Remove(ticketId);
            return Task.FromResult(released);
        }
    }

    public void Expire(Guid ticketId)
    {
        lock (_lock)
        {
            _holders.Remove(ticketId);
        }
    }
}

internal sealed class FakeIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<Guid, IdempotencyRecord> _records = new();
    private readonly Lock _lock = new();

    public bool ThrowOnRemember { get; set; }

    public Task<IdempotencyRecord?> GetAsync(Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_records.GetValueOrDefault(idempotencyKey));
        }
    }

    public Task RememberAsync(
        Guid idempotencyKey,
        IdempotencyRecord record,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnRemember)
        {
            throw new InvalidOperationException("Simulated cache outage.");
        }

        lock (_lock)
        {
            _records[idempotencyKey] = record;
        }

        return Task.CompletedTask;
    }

    public void Forget(Guid idempotencyKey)
    {
        lock (_lock)
        {
            _records.Remove(idempotencyKey);
        }
    }
}

internal sealed class FakeAvailableSeatsCache : IAvailableSeatsCache
{
    private readonly Dictionary<Guid, IReadOnlyList<SeatAvailabilityDto>> _seats = new();
    private readonly Lock _lock = new();

    public List<Guid> Invalidations { get; } = [];

    public Task<IReadOnlyList<SeatAvailabilityDto>?> GetAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_seats.GetValueOrDefault(eventId));
        }
    }

    public Task SetAsync(Guid eventId, IReadOnlyList<SeatAvailabilityDto> seats, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _seats[eventId] = seats;
        }

        return Task.CompletedTask;
    }

    public Task InvalidateAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _seats.Remove(eventId);
            Invalidations.Add(eventId);
        }

        return Task.CompletedTask;
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
