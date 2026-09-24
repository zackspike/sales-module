using System.Net.Mail;

namespace BookingService.Application.Tickets;

/// <summary>
/// Validates a ticket purchase (SP-05 / VAL-02): required fields and email format
/// first (400), then event existence (404).
/// </summary>
public sealed class TicketPurchaseValidator
{
    // RFC 5321 maximum length of a forward-path.
    private const int MaxEmailLength = 254;

    private readonly IEventCatalog _events;

    public TicketPurchaseValidator(IEventCatalog events)
    {
        _events = events;
    }

    public TicketPurchaseValidationResult Validate(PurchaseTicketCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(command.FullName))
        {
            errors["fullName"] = ["fullName is required."];
        }

        if (string.IsNullOrWhiteSpace(command.Email))
        {
            errors["email"] = ["email is required."];
        }
        else if (!IsValidEmail(command.Email))
        {
            errors["email"] = ["email is not a valid email address."];
        }

        if (errors.Count > 0)
        {
            return TicketPurchaseValidationResult.Invalid(errors);
        }

        return _events.Exists(command.EventId)
            ? TicketPurchaseValidationResult.Valid()
            : TicketPurchaseValidationResult.EventNotFound();
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
