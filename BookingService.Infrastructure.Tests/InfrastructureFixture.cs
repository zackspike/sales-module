using BookingService.Application.Repositories;
using BookingService.Application.Tickets;
using BookingService.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace BookingService.Infrastructure.Tests;

/// <summary>
/// Starts throwaway PostgreSQL and Redis containers, applies the scripts in <c>database/init</c>
/// (the same ones the docker compose database runs) and hosts the API against them. Shared by every
/// test in the <see cref="InfrastructureCollection"/>; tests that buy seats create their own event so they
/// never depend on each other.
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    public static readonly Guid DefaultEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public WebApplicationFactory<Program> Api { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_container.StartAsync(), _redis.StartAsync());
        await ApplyInitScriptsAsync();

        Api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = ConnectionString,
                    [$"ConnectionStrings:{DependencyInjection.CacheConnectionStringName}"] = _redis.GetConnectionString()
                }));
        });
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        await _container.DisposeAsync();
        await _redis.DisposeAsync();
    }

    /// <summary>
    /// New DI scope, i.e. what a single HTTP request gets: its own DbContext, repositories and unit of work.
    /// </summary>
    public AsyncServiceScope CreateScope() => Api.Services.CreateAsyncScope();

    /// <summary>
    /// Persists a new event with one zone and <paramref name="seatCount"/> available seats "A-1".."A-N".
    /// </summary>
    public async Task<SeededEvent> CreateEventAsync(int seatCount)
    {
        var @event = new Event
        {
            Id = Guid.NewGuid(),
            Name = "Integration Test Event",
            Artist = "Test Artist",
            VenueId = Guid.NewGuid(),
            VenueName = "Test Venue",
            Date = new DateTime(2026, 12, 31, 21, 0, 0, DateTimeKind.Utc),
            TotalSeats = seatCount
        };
        var zone = new Zone { Id = Guid.NewGuid(), EventId = @event.Id, Price = 10m };
        @event.Zones.Add(zone);
        var tickets = EventInventoryFactory.CreateInitialInventory(zone, seatCount);

        await using var scope = CreateScope();
        scope.ServiceProvider.GetRequiredService<IEventRepository>().Add(@event);
        scope.ServiceProvider.GetRequiredService<ITicketRepository>().AddRange(@event.Id, tickets);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return new SeededEvent(@event.Id, tickets.Select(t => t.Id).ToList());
    }

    /// <summary>
    /// Runs the reservation step (seat lock in Redis) in its own scope, like a separate HTTP request.
    /// </summary>
    public async Task<ReserveSeatResult> ReserveAsync(Guid eventId, Guid ticketId, string email)
    {
        await using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ReserveSeatHandler>();

        return await handler.HandleAsync(new ReserveSeatCommand(eventId, ticketId, "Juan Perez", email));
    }

    public async Task<T> QueryScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task ApplyInitScriptsAsync()
    {
        var scriptsDirectory = Path.Combine(AppContext.BaseDirectory, "database", "init");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var script in Directory.GetFiles(scriptsDirectory, "*.sql").Order(StringComparer.Ordinal))
        {
            await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(script), connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

public sealed record SeededEvent(Guid EventId, IReadOnlyList<Guid> TicketIds);

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Infrastructure";
}
