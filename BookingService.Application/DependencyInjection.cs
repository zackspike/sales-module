using BookingService.Application.EventSeats.Commands;
using BookingService.Application.EventSeats.Queries;
using BookingService.Application.Purchases;
using BookingService.Application.Purchases.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use case handlers. They are stateless, so one instance per request is enough.
    /// <paramref name="purchaseSession"/> comes from the <c>Purchase</c> configuration section; the
    /// defaults apply when it is not configured.
    /// </summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        PurchaseSessionOptions? purchaseSession = null)
    {
        services.AddSingleton(purchaseSession ?? new PurchaseSessionOptions());
        services.AddScoped<StartPurchaseHandler>();
        services.AddScoped<HoldSeatHandler>();
        services.AddScoped<ReleaseHeldSeatHandler>();
#pragma warning disable CS0618 // The MVP-02 handlers stay registered until the Api migrates to the purchase session.
        services.AddScoped<ReserveEventSeatHandler>();
        services.AddScoped<ReleaseEventSeatHandler>();
#pragma warning restore CS0618
        services.AddScoped<PurchaseEventSeatHandler>();
        services.AddScoped<GetEventSeatsHandler>();
        services.AddScoped<GetEventSeatAvailabilityHandler>();
        services.AddScoped<GetTicketByCodeHandler>();
        return services;
    }
}
