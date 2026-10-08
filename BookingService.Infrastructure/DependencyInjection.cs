using BookingService.Application.Abstractions;
using BookingService.Infrastructure.Caching;
using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BookingService.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers infrastructure services. If a PostgreSQL connection string ("DefaultConnection")
    /// is configured, it registers EF Core with Postgres repositories. Otherwise, it falls back
    /// to in-memory stores for isolated testing without a live database.
    /// Likewise, a Redis connection string ("Redis") enables the Redis seat locks, idempotency store
    /// and available seats cache; without it, seat locks are kept in memory and nothing is cached.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var connectionString = configuration?.GetConnectionString("DefaultConnection");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<BookingDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddScoped<IEventCatalog, PostgresEventCatalog>();
            services.AddScoped<ITicketRepository, PostgresTicketRepository>();
        }
        else
        {
            services.AddSingleton<IEventCatalog, InMemoryEventCatalog>();
            services.AddSingleton<ITicketRepository>(_ => new InMemoryTicketRepository(seedDefaultInventory: true));
        }

        var redisConnectionString = configuration?.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var options = ConfigurationOptions.Parse(redisConnectionString);

                // Keep retrying in the background instead of failing for good when Redis is not
                // reachable at startup; operations fail fast until the connection is back.
                options.AbortOnConnectFail = false;

                return ConnectionMultiplexer.Connect(options);
            });
            services.AddSingleton<ISeatLockStore, RedisSeatLockStore>();
            services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
            services.AddSingleton<IAvailableSeatsCache, RedisAvailableSeatsCache>();
        }
        else
        {
            services.AddSingleton<ISeatLockStore>(_ => new InMemorySeatLockStore());
            services.AddSingleton<NoCache>();
            services.AddSingleton<IIdempotencyStore>(sp => sp.GetRequiredService<NoCache>());
            services.AddSingleton<IAvailableSeatsCache>(sp => sp.GetRequiredService<NoCache>());
        }

        return services;
    }
}
