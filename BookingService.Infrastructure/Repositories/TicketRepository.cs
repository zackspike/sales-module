using BookingService.Application.Repositories;
using BookingService.Domain;
using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Repositories;

public sealed class TicketRepository : ITicketRepository
{
    private readonly BookingDbContext _context;

    public TicketRepository(BookingDbContext context)
    {
        _context = context;
    }

    public void AddRange(Guid eventId, IEnumerable<Ticket> tickets)
    {
        var batch = tickets.ToList();

        // Validate the whole batch before tracking anything, so a bad ticket leaves the
        // event inventory untouched.
        var batchIds = new HashSet<Guid>();
        foreach (var ticket in batch)
        {
            if (ticket.Seat?.Zone?.EventId != eventId)
            {
                throw new ArgumentException(
                    $"Ticket '{ticket.Id}' does not belong to a seat of event '{eventId}'.",
                    nameof(tickets));
            }

            if (!batchIds.Add(ticket.Id))
            {
                throw new ArgumentException($"Ticket with id '{ticket.Id}' is duplicated.", nameof(tickets));
            }
        }

        _context.Tickets.AddRange(batch);
    }

    public async Task<IReadOnlyList<Ticket>> GetAvailableByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        return await TicketsWithDetails()
            .AsNoTracking()
            .Where(t => t.Status == TicketStatus.Available && t.Seat!.Zone!.EventId == eventId)
            .ToListAsync(cancellationToken);
    }

    public Task<Ticket?> GetByIdAsync(Guid eventId, Guid ticketId, CancellationToken cancellationToken = default)
    {
        return TicketsWithDetails()
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == ticketId && t.Seat!.Zone!.EventId == eventId, cancellationToken);
    }

    public async Task<Ticket?> GetForPurchaseAsync(
        Guid eventId,
        Guid ticketId,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The ticket can only be locked inside a transaction.");
        }

        // Row lock held until the transaction ends. Under READ COMMITTED a waiting buyer
        // resumes after the lock is released and reads the committed (sold) state below.
        await _context.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM tickets WHERE ticket_id = {ticketId} FOR UPDATE",
            cancellationToken);

        return await TicketsWithDetails()
            .SingleOrDefaultAsync(t => t.Id == ticketId && t.Seat!.Zone!.EventId == eventId, cancellationToken);
    }

    public Task<Ticket?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        return TicketsWithDetails()
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    private IQueryable<Ticket> TicketsWithDetails()
    {
        return _context.Tickets
            .Include(t => t.Seat)
            .ThenInclude(s => s!.Zone)
            .Include(t => t.User);
    }
}
