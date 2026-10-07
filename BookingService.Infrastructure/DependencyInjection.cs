using BookingService.Application.Caching;
using BookingService.Application.Repositories;
using BookingService.Infrastructure.Caching;
using BookingService.Infrastructure.Persistence;
using BookingService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BookingService.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "BookingDatabase";
    public const string CacheConnectionStringName = "BookingCache";

    /// <summary>
    /// Registers the PostgreSQL persistence and the Redis hot-path stores. Connection strings are
    /// read when the first <see cref="BookingDbContext"/> or Redis connection is created, so tools
    /// that only build the host (OpenAPI generation) don't need a database or Redis configured.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<BookingDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(GetConnectionString(serviceProvider, ConnectionStringName));
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
        {
            var options = ConfigurationOptions.Parse(GetConnectionString(serviceProvider, CacheConnectionStringName));

            // Keep retrying in the background instead of failing for good when Redis is not
            // reachable at startup; operations fail fast until the connection is back.
            options.AbortOnConnectFail = false;

            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<ISeatLockStore, RedisSeatLockStore>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<IAvailableSeatsCache, RedisAvailableSeatsCache>();

        return services;
    }

    private static string GetConnectionString(IServiceProvider serviceProvider, string name)
    {
        return serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(name)
            ?? throw new InvalidOperationException($"Connection string '{name}' is not configured.");
    }
}
