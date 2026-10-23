using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
namespace BookingService.Infrastructure.Tests;

/// <summary>
/// Starts a disposable PostgreSQL container (Docker is required), applies the EF Core migrations once
/// and removes the container when the collection finishes. The connection string only ever comes from
/// the container, so no application configuration or environment variable can point the tests elsewhere.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Same image as docker-compose.yml and the CI workflow.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("booking_tests")
        .WithUsername("booking_tests")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    public DbContextOptions<BookingDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        await using var db = new BookingDbContext(Options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>Tests in this collection share one <see cref="PostgresFixture"/> container and run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
