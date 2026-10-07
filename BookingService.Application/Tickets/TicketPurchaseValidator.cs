using System.Net.Mail;
using BookingService.Application.Repositories;

namespace BookingService.Application.Tickets;

/// <summary>
/// Validates a ticket purchase (SP-05 / VAL-02): required fields and email format
/// first (400), then event existence (404).
/// </summary>
public sealed class TicketPurchaseValidator
{
    // RFC 5321 maximum length of a forward-path.
    private const int MaxEmailLength = 254;

    private readonly IEventRepository _events;

    public TicketPurchaseValidator(IEventRepository events)
    {
        _events = events;
    }

    public Task<TicketPurchaseValidationResult> ValidateAsync(
        PurchaseTicketCommand command,
        CancellationToken cancellationToken = default)
    {
        return ValidateAsync(command.EventId, command.FullName, command.Email, cancellationToken);
    }

    /// <summary>
    /// Validates the buyer data and event of any step of the purchase flow (reservation or purchase).
    /// </summary>
    public async Task<TicketPurchaseValidationResult> ValidateAsync(
        Guid eventId,
        string? fullName,
        string? email,
        CancellationToken cancellationToken = default)
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
            return TicketPurchaseValidationResult.Invalid(errors);
        }

        return await _events.ExistsAsync(eventId, cancellationToken)
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
