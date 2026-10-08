using BookingService.Application.Tickets.Commands;
using BookingService.Application.Tickets.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use case handlers. They are stateless, so one instance per request is enough.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<TicketPurchaseValidator>();
        services.AddScoped<ReserveSeatHandler>();
        services.AddScoped<PurchaseTicketHandler>();
        services.AddScoped<GetAvailableTicketsHandler>();
        services.AddScoped<CheckTicketAvailabilityHandler>();
        return services;
    }
}
