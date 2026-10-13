using BookingService.Application.Abstractions;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Persistence;

public sealed class PostgresEventSeatRepository : IEventSeatRepository
{
    internal const string IdempotencyKeyPropertyName = "IdempotencyKey";

    private readonly BookingDbContext _dbContext;

    public PostgresEventSeatRepository(BookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyCollection<EventSeat> GetByEvent(Guid eventId)
    {
        return _dbContext.EventSeats
            .AsNoTracking()
            .Where(s => s.EventId == eventId)
            .ToList();
    }

    public EventSeat? GetById(Guid eventId, Guid eventSeatId)
    {
        return _dbContext.EventSeats
            .AsNoTracking()
            .FirstOrDefault(s => s.EventId == eventId && s.Id == eventSeatId);
    }

    public EventSeatSale? GetSaleByIdempotencyKey(Guid idempotencyKey, Guid eventId, Guid eventSeatId)
    {
        if (idempotencyKey == Guid.Empty)
        {
            return null;
        }

        var ticket = FindTicketByKey(idempotencyKey, asNoTracking: true);
        if (ticket is null)
        {
            return null;
        }

        var seat = _dbContext.EventSeats
            .AsNoTracking()
            .FirstOrDefault(s => s.Id == ticket.EventSeatId);

        if (seat is null || seat.EventId != eventId || seat.Id != eventSeatId)
        {
            return null;
        }

        return new EventSeatSale(seat, ticket);
    }

    public EventSeatSaleOutcome SellOnce(Guid idempotencyKey, Guid eventId, Guid eventSeatId, Func<EventSeat, Ticket> sell)
    {
        if (idempotencyKey != Guid.Empty)
        {
            var existingTicket = FindTicketByKey(idempotencyKey, asNoTracking: false);
            if (existingTicket is not null)
            {
                return new EventSeatSaleOutcome(LoadSale(existingTicket), Replayed: true);
            }
        }

        var seat = _dbContext.EventSeats
            .FirstOrDefault(s => s.EventId == eventId && s.Id == eventSeatId);

        if (seat is null)
        {
            return new EventSeatSaleOutcome(null, Replayed: false);
        }

        var ticket = sell(seat);

        if (idempotencyKey != Guid.Empty)
        {
            _dbContext.Entry(ticket).Property<Guid>(IdempotencyKeyPropertyName).CurrentValue = idempotencyKey;
        }

        _dbContext.Tickets.Add(ticket);
        try
        {
            // xmin token: 0 rows updated if another buyer sold the seat first.
            // Unique index on IdempotencyKey: the same key won a race on another seat.
            _dbContext.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            var winner = FindTicketByKey(idempotencyKey, asNoTracking: true);
            if (winner is not null)
            {
                return new EventSeatSaleOutcome(LoadSale(winner), Replayed: true);
            }

            throw new EventSeatAlreadySoldException(eventSeatId);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            var winner = FindTicketByKey(idempotencyKey, asNoTracking: true);
            if (winner is not null)
            {
                return new EventSeatSaleOutcome(LoadSale(winner), Replayed: true);
            }

            throw;
        }

        return new EventSeatSaleOutcome(new EventSeatSale(seat, ticket), Replayed: false);
    }

    private Ticket? FindTicketByKey(Guid idempotencyKey, bool asNoTracking)
    {
        var query = _dbContext.Tickets.Where(t => EF.Property<Guid>(t, IdempotencyKeyPropertyName) == idempotencyKey);
        return asNoTracking ? query.AsNoTracking().FirstOrDefault() : query.FirstOrDefault();
    }

    private EventSeatSale LoadSale(Ticket ticket)
    {
        var seat = _dbContext.EventSeats.AsNoTracking().First(s => s.Id == ticket.EventSeatId);
        return new EventSeatSale(seat, ticket);
    }
}
