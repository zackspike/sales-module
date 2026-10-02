using BookingService.Application.Abstractions;
using BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers infrastructure services. If a PostgreSQL connection string ("DefaultConnection")
    /// is configured, it registers EF Core with Postgres repositories. Otherwise, it falls back
    /// to in-memory stores for isolated testing without a live database.
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

        return services;
    }
}
