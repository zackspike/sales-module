using BookingService.Application.Repositories;
using BookingService.Domain;
using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Repositories;

public sealed class EventRepository : IEventRepository
{
    private readonly BookingDbContext _context;

    public EventRepository(BookingDbContext context)
    {
        _context = context;
    }

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        return _context.Events.AnyAsync(e => e.Id == eventId, cancellationToken);
    }

    public Task<Event?> GetByIdAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        return _context.Events
            .AsNoTracking()
            .Include(e => e.Zones)
            .SingleOrDefaultAsync(e => e.Id == eventId, cancellationToken);
    }

    public void Add(Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _context.Events.Add(@event);
    }
}
