using BookingService.Application.Purchases.ReadModels;

namespace BookingService.Application.Purchases.Commands;

public enum StartPurchaseStatus
{
    /// <summary>The purchase session was opened (HTTP 201).</summary>
    Started,

    /// <summary>A required field is missing or malformed (HTTP 400, VALIDATION_FAILED).</summary>
    Invalid
}

/// <summary>
/// Outcome of <see cref="StartPurchaseHandler"/>. <see cref="Purchase"/> is set only for
/// <see cref="StartPurchaseStatus.Started"/>; <see cref="Errors"/> only for
/// <see cref="StartPurchaseStatus.Invalid"/>, keyed by JSON field name.
/// </summary>
public sealed record StartPurchaseResult(
    StartPurchaseStatus Status,
    StartedPurchase? Purchase,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public static StartPurchaseResult Started(StartedPurchase purchase) =>
        new(StartPurchaseStatus.Started, purchase, NoErrors);

    public static StartPurchaseResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(StartPurchaseStatus.Invalid, null, errors);
}
