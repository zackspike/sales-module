using BookingService.Application.Purchases.ReadModels;

namespace BookingService.Application.Purchases.Commands;

public enum StartPaymentStatus
{
    /// <summary>The payment token is stored in the session and returned (HTTP 200).</summary>
    Started,

    /// <summary>The purchase session is missing or expired (HTTP 401, SESSION_EXPIRED).</summary>
    SessionExpired,

    /// <summary>The purchase holds no seats (HTTP 409, NO_SEATS_HELD).</summary>
    NoSeatsHeld,

    /// <summary>Payment is down or answered something invalid (HTTP 502, PAYMENT_UNAVAILABLE).</summary>
    PaymentUnavailable,

    /// <summary>Payment did not answer in time (HTTP 504, PAYMENT_TIMEOUT).</summary>
    PaymentTimeout
}

/// <summary>
/// Outcome of <see cref="StartPaymentHandler"/>. <see cref="Payment"/> is set only for
/// <see cref="StartPaymentStatus.Started"/>.
/// </summary>
public sealed record StartPaymentResult(StartPaymentStatus Status, StartedPayment? Payment)
{
    public static StartPaymentResult Started(StartedPayment payment) =>
        new(StartPaymentStatus.Started, payment);

    public static StartPaymentResult Failed(StartPaymentStatus status) =>
        new(status, null);
}
