namespace BookingService.Application.Purchases;

/// <summary>
/// What the purchase use cases need to know about the calls to Payment, from the <c>Payment</c>
/// configuration section.
/// </summary>
public sealed class PaymentOptions
{
    /// <summary>
    /// How long Booking waits for each call to Payment's <c>/capture</c> (<c>Payment:CaptureTimeout</c>).
    /// </summary>
    public TimeSpan CaptureTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How many capture timeouts the payment margin covers (<c>Payment:CaptureAttempts</c>). It is 1, so the
    /// margin is the capture timeout, as the contract and decision 15 define it. Raise it only if the contract
    /// changes to cover the capture retry of decision 11 (still open with Payment).
    /// </summary>
    public int CaptureAttempts { get; init; } = 1;

    /// <summary>
    /// Minimum time a session must have left to pay (decision 15): <see cref="CaptureTimeout"/> by default.
    /// With less, "Pagar" answers <c>SESSION_EXPIRED</c> without calling Payment, so the capture never runs on a
    /// token Payment is about to void at <c>expiresAt</c>, nor on seat locks about to expire with the session
    /// (decision 17).
    /// </summary>
    public TimeSpan PaymentMargin => CaptureTimeout * CaptureAttempts;
}
