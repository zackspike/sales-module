using BookingService.Application.EventSeats.Commands;
using BookingService.Application.EventSeats.Queries;
using BookingService.Application.Purchases;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use case handlers. They are stateless, so one instance per request is enough.
    /// <paramref name="purchaseSession"/> comes from the <c>Purchase</c> configuration section; the
    /// defaults apply when it is not configured, and invalid values fail here instead of turning every
    /// request into a 500.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <c>Purchase:MaxSeats</c> or <c>Purchase:SessionDuration</c> is not positive.
    /// </exception>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        PurchaseSessionOptions? purchaseSession = null)
    {
        var options = purchaseSession ?? new PurchaseSessionOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxSeats, "Purchase:MaxSeats");
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.SessionDuration, TimeSpan.Zero, "Purchase:SessionDuration");
        services.AddSingleton(options);

        // StartPurchaseHandler, HoldSeatHandler and ReleaseHeldSeatHandler depend on
        // IPurchaseSessionStore, which nothing implements yet: T4 registers them along with the store.
        // StartPaymentHandler and ConfirmPurchaseHandler also depend on IPaymentGateway,
        // IPurchaseSaleRepository, ITicketSoldOutbox and PaymentOptions: T5/T6 register them along with those.
        // No endpoint resolves them in the meantime, and registering them now breaks app.Build() in
        // Development, where ValidateOnBuild is on.
#pragma warning disable CS0618 // The MVP-02 handlers stay registered until the Api migrates to the purchase session.
        services.AddScoped<ReserveEventSeatHandler>();
        services.AddScoped<ReleaseEventSeatHandler>();
        services.AddScoped<PurchaseEventSeatHandler>();
#pragma warning restore CS0618
        services.AddScoped<GetEventSeatsHandler>();
        services.AddScoped<GetEventSeatAvailabilityHandler>();
        services.AddScoped<GetTicketByCodeHandler>();
        return services;
    }
}
