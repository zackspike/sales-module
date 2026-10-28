using BookingService.Application.Abstractions;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingService.Infrastructure.Persistence;

public sealed class PostgresEventSeatRepository : IEventSeatRepository
{
    internal const string IdempotencyKeyPropertyName = "IdempotencyKey";
    private const string EventSeatSoldIndexName = "IX_tickets_EventSeatId";

    private readonly BookingDbContext _dbContext;

    public PostgresEventSeatRepository(BookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<EventSeat>> GetByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventSeats
            .AsNoTracking()
            .Where(s => s.EventId == eventId)
            .ToListAsync(cancellationToken);
    }

    public Task<EventSeat?> GetByIdAsync(
        Guid eventId,
        Guid eventSeatId,
        CancellationToken cancellationToken)
    {
        return _dbContext.EventSeats
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.EventId == eventId && s.Id == eventSeatId,
                cancellationToken);
    }

    public async Task<EventSeatSale?> GetSaleByIdempotencyKeyAsync(
        Guid idempotencyKey,
        Guid eventId,
        Guid eventSeatId,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey == Guid.Empty)
        {
            return null;
        }

        var ticket = await FindTicketByKeyAsync(idempotencyKey, asNoTracking: true, cancellationToken);
        if (ticket is null)
        {
            return null;
        }

        var seat = await _dbContext.EventSeats
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == ticket.EventSeatId, cancellationToken);

        if (seat is null || seat.EventId != eventId || seat.Id != eventSeatId)
        {
            return null;
        }

        return new EventSeatSale(seat, ticket);
    }

    public async Task<EventSeatSaleOutcome> SellOnceAsync(
        Guid idempotencyKey,
        Guid eventId,
        Guid eventSeatId,
        Func<EventSeat, Ticket> sell,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey != Guid.Empty)
        {
            var existingTicket = await FindTicketByKeyAsync(
                idempotencyKey,
                asNoTracking: false,
                cancellationToken);
            if (existingTicket is not null)
            {
                return new EventSeatSaleOutcome(
                    await LoadSaleAsync(existingTicket, cancellationToken),
                    Replayed: true);
            }
        }

        var seat = await _dbContext.EventSeats
            .FirstOrDefaultAsync(
                s => s.EventId == eventId && s.Id == eventSeatId,
                cancellationToken);

        if (seat is null)
        {
            return new EventSeatSaleOutcome(null, Replayed: false);
        }

        Ticket ticket;
        try
        {
            ticket = sell(seat);
        }
        catch (EventSeatAlreadySoldException)
        {
            // Another buyer committed between the idempotency key lookup and the seat read.
            return await ReplayOrAlreadySoldAsync(idempotencyKey, eventSeatId, cancellationToken);
        }

        if (idempotencyKey != Guid.Empty)
        {
            _dbContext.Entry(ticket).Property<Guid>(IdempotencyKeyPropertyName).CurrentValue = idempotencyKey;
        }

        _dbContext.Tickets.Add(ticket);
        try
        {
            // xmin token: 0 rows updated if another buyer sold the seat first.
            // Unique index on EventSeatId: another buyer's ticket for this seat was committed first.
            // Unique index on IdempotencyKey: the same key won a race on another seat.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return await ReplayOrAlreadySoldAsync(idempotencyKey, eventSeatId, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _dbContext.ChangeTracker.Clear();
            var winner = await FindTicketByKeyAsync(idempotencyKey, asNoTracking: true, cancellationToken);
            if (winner is not null)
            {
                return new EventSeatSaleOutcome(
                    await LoadSaleAsync(winner, cancellationToken),
                    Replayed: true);
            }

            if (exception.InnerException is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: EventSeatSoldIndexName,
                })
            {
                throw new EventSeatAlreadySoldException(eventSeatId);
            }

            throw;
        }

        return new EventSeatSaleOutcome(new EventSeatSale(seat, ticket), Replayed: false);
    }

    private async Task<EventSeatSaleOutcome> ReplayOrAlreadySoldAsync(
        Guid idempotencyKey,
        Guid eventSeatId,
        CancellationToken cancellationToken)
    {
        var winner = await FindTicketByKeyAsync(idempotencyKey, asNoTracking: true, cancellationToken);
        if (winner is not null)
        {
            return new EventSeatSaleOutcome(
                await LoadSaleAsync(winner, cancellationToken),
                Replayed: true);
        }

        throw new EventSeatAlreadySoldException(eventSeatId);
    }

    private Task<Ticket?> FindTicketByKeyAsync(
        Guid idempotencyKey,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Tickets.Where(t => EF.Property<Guid>(t, IdempotencyKeyPropertyName) == idempotencyKey);
        return asNoTracking
            ? query.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            : query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<EventSeatSale> LoadSaleAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var seat = await _dbContext.EventSeats
            .AsNoTracking()
            .FirstAsync(s => s.Id == ticket.EventSeatId, cancellationToken);
        return new EventSeatSale(seat, ticket);
    }
}
