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
    /// How many times the payment client may call <c>/capture</c> before the sale rolls back
    /// (<c>Payment:CaptureAttempts</c>): one attempt plus one retry by default (decision 11).
    /// </summary>
    public int CaptureAttempts { get; init; } = 2;

    /// <summary>
    /// Minimum time a session must have left to pay (decision 15): the longest a capture can take, retries
    /// included. With less, "Pagar" answers <c>SESSION_EXPIRED</c> without calling Payment, so neither the token
    /// (voided by Payment at <c>expiresAt</c>) nor the seat locks (which expire with the session, decision 17)
    /// run out while the sale is in flight.
    /// </summary>
    public TimeSpan PaymentMargin => CaptureTimeout * CaptureAttempts;
}
