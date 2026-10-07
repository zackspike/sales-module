using BookingService.Application.Repositories;
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

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Contains(eventId));

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

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        await _transactionLock.WaitAsync(cancellationToken);
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
