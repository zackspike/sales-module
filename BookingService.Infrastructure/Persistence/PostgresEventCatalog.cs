using BookingService.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Persistence;

public class PostgresEventCatalog : IEventCatalog
{
    private readonly BookingDbContext _dbContext;

    public PostgresEventCatalog(BookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return _dbContext.Events.AnyAsync(e => e.Id == eventId, cancellationToken);
    }
}
