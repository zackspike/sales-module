using BookingService.Application.EventSeats.Commands;
using BookingService.Application.EventSeats.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use case handlers. They are stateless, so one instance per request is enough.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ReserveEventSeatHandler>();
        services.AddScoped<PurchaseEventSeatHandler>();
        services.AddScoped<GetAvailableEventSeatsHandler>();
        services.AddScoped<GetEventSeatAvailabilityHandler>();
        return services;
    }
}
