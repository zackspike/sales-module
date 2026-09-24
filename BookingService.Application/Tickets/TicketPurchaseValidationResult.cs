namespace BookingService.Application.Tickets;

public enum TicketPurchaseValidationStatus
{
    /// <summary>The request can proceed.</summary>
    Valid,

    /// <summary>A required field is missing or malformed (HTTP 400).</summary>
    Invalid,

    /// <summary>The target event does not exist (HTTP 404).</summary>
    EventNotFound
}

/// <summary>
/// Outcome of validating a ticket purchase. <see cref="Errors"/> is keyed by the JSON
/// field name (<c>fullName</c>, <c>email</c>) and is only populated when <see cref="Status"/>
/// is <see cref="TicketPurchaseValidationStatus.Invalid"/>.
/// </summary>
public sealed record TicketPurchaseValidationResult(
    TicketPurchaseValidationStatus Status,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    public bool IsValid => Status == TicketPurchaseValidationStatus.Valid;

    public static TicketPurchaseValidationResult Valid() =>
        new(TicketPurchaseValidationStatus.Valid, NoErrors);

    public static TicketPurchaseValidationResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(TicketPurchaseValidationStatus.Invalid, errors);

    public static TicketPurchaseValidationResult EventNotFound() =>
        new(TicketPurchaseValidationStatus.EventNotFound, NoErrors);
}
