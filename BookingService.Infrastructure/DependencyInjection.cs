using BookingService.Application.Abstractions;
using BookingService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the in-memory stores. They hold the application state, so they must be singletons.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IEventCatalog, InMemoryEventCatalog>();
        services.AddSingleton<ITicketRepository>(_ => new InMemoryTicketRepository(seedDefaultInventory: true));
        return services;
    }
}
