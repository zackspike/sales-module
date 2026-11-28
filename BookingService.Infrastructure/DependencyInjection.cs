using BookingService.Application.Abstractions;
using BookingService.Infrastructure.Caching;
using BookingService.Infrastructure.Messaging;
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
    /// and event seats cache; without it, seat locks are kept in memory and nothing is cached.
    /// With PostgreSQL, the purchase sale repository and the <c>ticketSold</c> outbox are registered too, and a
    /// RabbitMQ connection string ("RabbitMq") adds the relay that publishes the outbox.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration? configuration = null)
    {
        // Until VenueService is integrated, the venue seat catalog is served from the agreed test data.
        services.AddSingleton<IVenueSeatCatalog, InMemoryVenueSeatCatalog>();

        var connectionString = configuration?.GetConnectionString("DefaultConnection");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<BookingDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddScoped<IEventCatalog, PostgresEventCatalog>();
            services.AddScoped<IEventSeatRepository, PostgresEventSeatRepository>();

            // Same scoped BookingDbContext: the outbox joins the sale's transaction (ADR-003).
            services.AddScoped<IPurchaseSaleRepository, PostgresPurchaseSaleRepository>();
            services.AddScoped<ITicketSoldOutbox, PostgresTicketSoldOutbox>();

            AddOutboxRelay(services, configuration!);
        }
        else
        {
            services.AddSingleton<IEventCatalog, InMemoryEventCatalog>();
            services.AddSingleton<IEventSeatRepository>(_ => new InMemoryEventSeatRepository(seedDefaultInventory: true));
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
            services.AddSingleton<IEventSeatLockStore, RedisSeatLockStore>();
            services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
            services.AddSingleton<IEventSeatsCache, RedisEventSeatsCache>();
        }
        else
        {
            services.AddSingleton<IEventSeatLockStore>(_ => new InMemorySeatLockStore());
            services.AddSingleton<NoCache>();
            services.AddSingleton<IIdempotencyStore>(sp => sp.GetRequiredService<NoCache>());
            services.AddSingleton<IEventSeatsCache>(sp => sp.GetRequiredService<NoCache>());
        }

        return services;
    }

    /// <summary>
    /// Registers the relay that publishes the outbox to RabbitMQ when <c>ConnectionStrings:RabbitMq</c> is set.
    /// Without it nothing is published and the <c>ticketSold</c> messages wait in the outbox table.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <c>Outbox:PollInterval</c>, <c>Outbox:BatchSize</c> or <c>Outbox:PublishTimeout</c> is not positive.
    /// </exception>
    private static void AddOutboxRelay(IServiceCollection services, IConfiguration configuration)
    {
        var rabbitMqConnectionString = configuration.GetConnectionString("RabbitMq");
        if (string.IsNullOrWhiteSpace(rabbitMqConnectionString))
        {
            return;
        }

        var outbox = configuration.GetSection("Outbox").Get<OutboxOptions>() ?? new OutboxOptions();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(outbox.PollInterval, TimeSpan.Zero, "Outbox:PollInterval");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outbox.BatchSize, "Outbox:BatchSize");
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(outbox.PublishTimeout, TimeSpan.Zero, "Outbox:PublishTimeout");
        services.AddSingleton(outbox);

        var rabbitMq = configuration.GetSection("RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMq.Exchange, "RabbitMq:Exchange");

        services.AddSingleton<IOutboxPublisher>(_ => new RabbitMqOutboxPublisher(rabbitMqConnectionString, rabbitMq));
        services.AddHostedService<OutboxRelay>();
    }
}
