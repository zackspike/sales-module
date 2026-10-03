using BookingService.Application.Abstractions;
using BookingService.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingService.Infrastructure.Persistence;

public class PostgresTicketRepository : ITicketRepository
{
    // Must match the index name declared in TicketConfiguration / migrations.
    private const string IdempotencyKeyIndexName = "IX_tickets_IdempotencyKey";

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
            .FirstOrDefault(t => t.EventId == eventId && t.Id == ticketId);
    }

    public Ticket? GetByIdempotencyKey(Guid idempotencyKey)
    {
        if (idempotencyKey == Guid.Empty)
        {
            return null;
        }

        return _dbContext.Tickets
            .AsNoTracking()
            .FirstOrDefault(t => t.IdempotencyKey == idempotencyKey);
    }

    public void Update(Ticket ticket)
    {
        try
        {
            var entry = _dbContext.Entry(ticket);
            if (entry.State == EntityState.Detached)
            {
                _dbContext.Tickets.Update(ticket);
            }

            _dbContext.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new TicketAlreadySoldException(ticket.Id);
        }
        catch (DbUpdateException ex) when (IsIdempotencyKeyViolation(ex))
        {
            throw new DuplicateIdempotencyKeyException(ticket.IdempotencyKey ?? Guid.Empty, ex);
        }
    }

    private static bool IsIdempotencyKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IdempotencyKeyIndexName
        };
}
