using System.Net.Mail;
using BookingService.Application.Abstractions;

namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// Validates an event seat reservation or purchase (SP-05 / VAL-02): required fields and email format
/// first (400), then event existence (404).
/// </summary>
public sealed class SeatPurchaseValidator
{
    // RFC 5321 maximum length of a forward-path.
    private const int MaxEmailLength = 254;

    private readonly IEventCatalog _events;

    public SeatPurchaseValidator(IEventCatalog events)
    {
        _events = events;
    }

    public SeatPurchaseValidationResult Validate(PurchaseEventSeatCommand command) =>
        Validate(command.EventId, command.FullName, command.Email);

    /// <summary>
    /// Validates the buyer data and event of any step of the purchase flow (reservation or purchase).
    /// </summary>
    public SeatPurchaseValidationResult Validate(Guid eventId, string? fullName, string? email)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            errors["fullName"] = ["fullName is required."];
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            errors["email"] = ["email is required."];
        }
        else if (!IsValidEmail(email))
        {
            errors["email"] = ["email is not a valid email address."];
        }

        if (errors.Count > 0)
        {
            return SeatPurchaseValidationResult.Invalid(errors);
        }

        return _events.Exists(eventId)
            ? SeatPurchaseValidationResult.Valid()
            : SeatPurchaseValidationResult.EventNotFound();
    }

    private static bool IsValidEmail(string email)
    {
        var value = email.Trim();

        if (value.Length > MaxEmailLength)
        {
            return false;
        }

        // Requiring the parsed address to equal the input rejects display-name forms
        // such as "Jane <jane@example.com>", which MailAddress would otherwise accept.
        return MailAddress.TryCreate(value, out var parsed) && parsed.Address == value;
    }
}
