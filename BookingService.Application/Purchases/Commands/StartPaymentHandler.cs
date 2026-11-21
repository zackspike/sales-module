using BookingService.Application.Abstractions;
using BookingService.Application.Purchases.ReadModels;

namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// Gets the payment token of a purchase from Payment's <c>/init</c> and stores it in the session: validates
/// the session (401) and that the purchase holds seats (409) before calling Payment.
/// <para>
/// Payment is always asked, even when the session already has a token: Booking cannot tell whether Payment
/// voided it (attempt limit, ADR-006), and <c>/init</c> is idempotent per purchase and seats, so it returns
/// the same token while it is still valid and a new one when it is <c>VOID</c> or the seats changed
/// (decisions 2 and 3).
/// </para>
/// </summary>
public sealed class StartPaymentHandler
{
    private readonly IPurchaseSessionStore _sessions;
    private readonly IEventSeatLockStore _eventSeatLocks;
    private readonly IPaymentGateway _payment;

    public StartPaymentHandler(
        IPurchaseSessionStore sessions,
        IEventSeatLockStore eventSeatLocks,
        IPaymentGateway payment)
    {
        _sessions = sessions;
        _eventSeatLocks = eventSeatLocks;
        _payment = payment;
    }

    public async Task<StartPaymentResult> HandleAsync(StartPaymentCommand command, CancellationToken cancellationToken)
    {
        var purchase = await _sessions.GetAsync(command.PurchaseId, cancellationToken);
        if (purchase is null)
        {
            return StartPaymentResult.Failed(StartPaymentStatus.SessionExpired);
        }

        if (purchase.IsExpired(DateTime.UtcNow))
        {
            await PurchaseSessionExpiration.ReleaseHeldSeatsAsync(purchase, _eventSeatLocks, cancellationToken);
            return StartPaymentResult.Failed(StartPaymentStatus.SessionExpired);
        }

        if (purchase.HeldEventSeatIds.Count == 0)
        {
            return StartPaymentResult.Failed(StartPaymentStatus.NoSeatsHeld);
        }

        var init = await _payment.InitAsync(
            purchase.Id,
            purchase.ExpiresAtUtc,
            purchase.HeldEventSeatIds,
            cancellationToken);

        switch (init.Status)
        {
            case PaymentCallStatus.Succeeded when !string.IsNullOrWhiteSpace(init.Token):
                purchase.SetPaymentToken(init.Token);
                await _sessions.SaveAsync(purchase, cancellationToken);
                return StartPaymentResult.Started(new StartedPayment(init.Token, purchase.ExpiresAtUtc));
            case PaymentCallStatus.TimedOut:
                return StartPaymentResult.Failed(StartPaymentStatus.PaymentTimeout);
            default:
                // Down, an invalid answer, or a success without a token: nothing usable came back.
                return StartPaymentResult.Failed(StartPaymentStatus.PaymentUnavailable);
        }
    }
}
