namespace BookingService.Application.EventSeats.Commands;

public enum SeatPurchaseValidationStatus
{
    /// <summary>The request can proceed.</summary>
    Valid,

    /// <summary>A required field is missing or malformed (HTTP 400).</summary>
    Invalid,

    /// <summary>The target event does not exist (HTTP 404).</summary>
    EventNotFound
}

/// <summary>
/// Outcome of validating an event seat reservation or purchase. <see cref="Errors"/> is keyed by the JSON
/// field name (<c>fullName</c>, <c>email</c>) and is only populated when <see cref="Status"/>
/// is <see cref="SeatPurchaseValidationStatus.Invalid"/>.
/// </summary>
public sealed record SeatPurchaseValidationResult(
    SeatPurchaseValidationStatus Status,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public bool IsValid => Status == SeatPurchaseValidationStatus.Valid;

    public static SeatPurchaseValidationResult Valid() =>
        new(SeatPurchaseValidationStatus.Valid, NoErrors);

    public static SeatPurchaseValidationResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(SeatPurchaseValidationStatus.Invalid, errors);

    public static SeatPurchaseValidationResult EventNotFound() =>
        new(SeatPurchaseValidationStatus.EventNotFound, NoErrors);
}
