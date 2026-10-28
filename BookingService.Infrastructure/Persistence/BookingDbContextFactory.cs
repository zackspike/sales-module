using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookingService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build and generate the model snapshot
/// without booting the API host. Uses <c>ConnectionStrings__DefaultConnection</c> when set, and
/// otherwise falls back to the local docker-compose PostgreSQL (host port 5433).
/// </summary>
public sealed class BookingDbContextFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5433;Database=bookingservice_db;Username=postgres;Password=postgres";

    public BookingDbContext CreateDbContext(string[] args)
    {
        var envConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        var connectionString = string.IsNullOrWhiteSpace(envConnectionString)
            ? DefaultConnectionString
            : envConnectionString;

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BookingDbContext(options);
    }
}
