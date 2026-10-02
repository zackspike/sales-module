using BookingService.Application.Abstractions;
using BookingService.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Persistence;

public class PostgresTicketRepository : ITicketRepository
{
    private readonly BookingDbContext _dbContext;

    public PostgresTicketRepository(BookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void AddRange(Guid eventId, IEnumerable<Ticket> tickets)
    {
        var batch = tickets.ToList();
        var batchIds = new HashSet<Guid>();

        foreach (var ticket in batch)
        {
            if (ticket.EventId != eventId)
            {
                throw new ArgumentException(
                    $"Ticket '{ticket.Id}' belongs to event '{ticket.EventId}', not '{eventId}'.",
                    nameof(tickets));
            }

            if (!batchIds.Add(ticket.Id))
            {
                throw new ArgumentException($"Duplicate ticket with id '{ticket.Id}' in batch.", nameof(tickets));
            }
        }

        var existingIds = _dbContext.Tickets
            .Where(t => batchIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToList();

        if (existingIds.Count > 0)
        {
            throw new ArgumentException($"Ticket with id '{existingIds[0]}' already exists.", nameof(tickets));
        }

        _dbContext.Tickets.AddRange(batch);
        _dbContext.SaveChanges();
    }

    public IReadOnlyCollection<Ticket> GetByEvent(Guid eventId)
    {
        return _dbContext.Tickets
            .AsNoTracking()
            .Where(t => t.EventId == eventId)
            .ToList();
    }

    public Ticket? GetById(Guid eventId, Guid ticketId)
    {
        return _dbContext.Tickets
            .AsNoTracking()
            .FirstOrDefault(t => t.EventId == eventId && t.Id == ticketId);
    }

    public TicketPurchaseOutcome PurchaseOnce(Guid idempotencyKey, Guid eventId, Guid ticketId, Action<Ticket> purchase)
    {
        using var transaction = _dbContext.Database.BeginTransaction();

        if (idempotencyKey != Guid.Empty)
        {
            var existingTicket = _dbContext.Tickets
                .FirstOrDefault(t => t.IdempotencyKey == idempotencyKey);

            if (existingTicket is not null)
            {
                return new TicketPurchaseOutcome(existingTicket, Replayed: true);
            }
        }

        var ticket = _dbContext.Tickets
            .FirstOrDefault(t => t.EventId == eventId && t.Id == ticketId);

        if (ticket is null)
        {
            return new TicketPurchaseOutcome(null, Replayed: false);
        }

        purchase(ticket);

        _dbContext.SaveChanges();
        transaction.Commit();

        return new TicketPurchaseOutcome(ticket, Replayed: false);
    }
}
