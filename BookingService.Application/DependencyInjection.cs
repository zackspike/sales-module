using BookingService.Application.EventSeats.Commands;
using BookingService.Application.EventSeats.Queries;
using BookingService.Application.Purchases;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use case handlers. They are stateless, so one instance per request is enough.
    /// <paramref name="purchaseSession"/> and <paramref name="payment"/> come from the <c>Purchase</c> and
    /// <c>Payment</c> configuration sections; the defaults apply when they are not configured, and invalid
    /// values fail here, at startup, instead of turning every request into an error.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <c>Purchase:MaxSeats</c>, <c>Purchase:SessionDuration</c>, <c>Payment:CaptureTimeout</c> or
    /// <c>Payment:CaptureAttempts</c> is not positive.
    /// </exception>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        PurchaseSessionOptions? purchaseSession = null,
        PaymentOptions? payment = null)
    {
        var options = purchaseSession ?? new PurchaseSessionOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxSeats, "Purchase:MaxSeats");
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.SessionDuration, TimeSpan.Zero, "Purchase:SessionDuration");
        services.AddSingleton(options);

        var paymentOptions = payment ?? new PaymentOptions();
        // A zero or negative value would silently turn off the margin of decision 15.
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(paymentOptions.CaptureTimeout, TimeSpan.Zero, "Payment:CaptureTimeout");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentOptions.CaptureAttempts, "Payment:CaptureAttempts");
        services.AddSingleton(paymentOptions);

        // StartPurchaseHandler, HoldSeatHandler and ReleaseHeldSeatHandler depend on
        // IPurchaseSessionStore, which nothing implements yet: T4 registers them along with the store.
        // StartPaymentHandler and ConfirmPurchaseHandler also depend on IPaymentGateway,
        // IPurchaseSaleRepository and ITicketSoldOutbox: T5/T6 register them along with those.
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
