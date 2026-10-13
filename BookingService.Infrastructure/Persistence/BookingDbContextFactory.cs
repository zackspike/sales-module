using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookingService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build and generate the model snapshot
/// without booting the API host. Matches the local docker-compose PostgreSQL (host port 5433).
/// </summary>
public sealed class BookingDbContextFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=bookingservice_db;Username=postgres;Password=postgres")
            .Options;

        return new BookingDbContext(options);
    }
}
