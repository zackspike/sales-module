namespace BookingService.Application.Abstractions;

/// <summary>
/// The two calls Booking makes to Payment: <c>POST /init</c> and <c>PATCH /capture</c>. Booking never
/// calls <c>/authorize</c> and never counts attempts: that belongs to Payment (ADR-006).
/// Implementations translate transport failures into <see cref="PaymentCallStatus"/> instead of throwing.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Asks Payment for the payment token of <paramref name="purchaseId"/>. Idempotent per purchase and seats:
    /// while the token is not <c>VOID</c> and the seats did not change, Payment returns the same token; otherwise
    /// it cancels the previous one and issues a new one with the same <paramref name="expiresAtUtc"/>.
    /// </summary>
    Task<PaymentInitResult> InitAsync(
        Guid purchaseId,
        DateTime expiresAtUtc,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Captures the payment authorized with <paramref name="token"/>. Must be idempotent per token: a payment
    /// that was already captured is reported as <see cref="PaymentCallStatus.Succeeded"/>.
    /// </summary>
    Task<PaymentCaptureResult> CaptureAsync(string token, CancellationToken cancellationToken);
}

public enum PaymentCallStatus
{
    /// <summary>Payment answered 200 (for <c>/init</c>, with a token).</summary>
    Succeeded,

    /// <summary>The token is not <c>AUTHORIZED</c>, so it cannot be captured. Only returned by <c>/capture</c>.</summary>
    NotAuthorized,

    /// <summary>Payment is down or answered something invalid (HTTP 502, PAYMENT_UNAVAILABLE).</summary>
    Unavailable,

    /// <summary>Payment did not answer in time (HTTP 504, PAYMENT_TIMEOUT).</summary>
    TimedOut
}

/// <summary>
/// Outcome of <see cref="IPaymentGateway.InitAsync"/>. <see cref="Token"/> is set only for
/// <see cref="PaymentCallStatus.Succeeded"/>.
/// </summary>
public sealed record PaymentInitResult(PaymentCallStatus Status, string? Token);

/// <summary>
/// Outcome of <see cref="IPaymentGateway.CaptureAsync"/>.
/// </summary>
public sealed record PaymentCaptureResult(PaymentCallStatus Status);
