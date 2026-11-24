using BookingService.Application.Abstractions;
using BookingService.Application.Purchases.ReadModels;

namespace BookingService.Application.Purchases.Commands;

public enum ConfirmPurchaseStatus
{
    /// <summary>The payment was captured and the tickets issued by this request (HTTP 200).</summary>
    Purchased,

    /// <summary>The idempotency key already paid this purchase; the same tickets are returned (HTTP 200).</summary>
    Replayed,

    /// <summary>The idempotency key is missing or empty (HTTP 400, VALIDATION_FAILED).</summary>
    Invalid,

    /// <summary>
    /// The purchase session is missing, expired, or has less time left than the capture may take
    /// (HTTP 401, SESSION_EXPIRED). Payment is not called.
    /// </summary>
    SessionExpired,

    /// <summary>The purchase holds no seats (HTTP 409, NO_SEATS_HELD).</summary>
    NoSeatsHeld,

    /// <summary>
    /// There is no payment token, or Payment says it is not <c>AUTHORIZED</c> (HTTP 409, PAYMENT_NOT_AUTHORIZED).
    /// Nothing is sold.
    /// </summary>
    PaymentNotAuthorized,

    /// <summary>
    /// At least one held seat was already sold (HTTP 409, SEAT_ALREADY_SOLD). Nothing is sold;
    /// <see cref="ConfirmPurchaseResult.FailedSeats"/> lists every seat that failed and why.
    /// </summary>
    AlreadySold,

    /// <summary>
    /// The held seats that failed no longer exist in the event (HTTP 404, SEAT_NOT_FOUND). Nothing is sold;
    /// <see cref="ConfirmPurchaseResult.FailedSeats"/> lists them.
    /// </summary>
    EventSeatNotFound,

    /// <summary>The idempotency key was already used by another purchase (HTTP 409, IDEMPOTENCY_KEY_CONFLICT).</summary>
    IdempotencyKeyConflict,

    /// <summary>Payment is down or rejected the capture (HTTP 502, PAYMENT_UNAVAILABLE). Nothing is sold.</summary>
    PaymentUnavailable,

    /// <summary>Payment did not answer the capture in time (HTTP 504, PAYMENT_TIMEOUT). Nothing is sold.</summary>
    PaymentTimeout
}

/// <summary>
/// Outcome of <see cref="ConfirmPurchaseHandler"/>. <see cref="Purchase"/> is set for
/// <see cref="ConfirmPurchaseStatus.Purchased"/> and <see cref="ConfirmPurchaseStatus.Replayed"/>;
/// <see cref="Errors"/> only for <see cref="ConfirmPurchaseStatus.Invalid"/>, keyed by field name;
/// <see cref="FailedSeats"/> only for <see cref="ConfirmPurchaseStatus.AlreadySold"/> and
/// <see cref="ConfirmPurchaseStatus.EventSeatNotFound"/>, so the front knows which seats to release.
/// </summary>
public sealed record ConfirmPurchaseResult(
    ConfirmPurchaseStatus Status,
    ConfirmedPurchase? Purchase,
    IReadOnlyDictionary<string, string[]> Errors,
    IReadOnlyList<FailedEventSeat> FailedSeats)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public static ConfirmPurchaseResult Purchased(ConfirmedPurchase purchase) =>
        new(ConfirmPurchaseStatus.Purchased, purchase, NoErrors, []);

    public static ConfirmPurchaseResult Replayed(ConfirmedPurchase purchase) =>
        new(ConfirmPurchaseStatus.Replayed, purchase, NoErrors, []);

    public static ConfirmPurchaseResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(ConfirmPurchaseStatus.Invalid, null, errors, []);

    public static ConfirmPurchaseResult Failed(ConfirmPurchaseStatus status) =>
        new(status, null, NoErrors, []);

    public static ConfirmPurchaseResult SeatsUnavailable(ConfirmPurchaseStatus status, IReadOnlyList<FailedEventSeat> failedSeats) =>
        new(status, null, NoErrors, failedSeats);
}
