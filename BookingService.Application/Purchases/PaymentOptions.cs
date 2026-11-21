namespace BookingService.Application.Purchases;

/// <summary>
/// What the purchase use cases need to know about the calls to Payment, from the <c>Payment</c>
/// configuration section.
/// </summary>
public sealed class PaymentOptions
{
    /// <summary>
    /// How long Booking waits for Payment's <c>/capture</c> (<c>Payment:CaptureTimeout</c>). It is also the
    /// minimum time a session must have left to pay (decision 15): with less, "Pagar" answers
    /// <c>SESSION_EXPIRED</c> without calling Payment, so a capture never runs on a token Payment is about to void.
    /// </summary>
    public TimeSpan CaptureTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
