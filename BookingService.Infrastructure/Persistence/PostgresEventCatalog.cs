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

    public bool Exists(Guid eventId)
    {
        return _dbContext.Events.Any(e => e.Id == eventId);
    }
}
