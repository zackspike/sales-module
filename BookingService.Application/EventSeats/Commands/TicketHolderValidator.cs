using System.Net.Mail;
using BookingService.Domain.Tickets;

namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// Validates the ticket holder data sent to reserve or purchase an event seat (SP-05 / VAL-02):
/// required fields, length and email format. Event and event seat existence are checked by each handler.
/// </summary>
public static class TicketHolderValidator
{
    /// <summary>
    /// Returns the errors keyed by JSON field name (<c>fullName</c>, <c>email</c>); empty when the data is valid.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> Validate(string fullName, string email)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            errors["fullName"] = ["fullName is required."];
        }
        else if (!IsValidFullName(fullName))
        {
            errors["fullName"] = ["fullName is too long"];
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            errors["email"] = ["email is required."];
        }
        else if (!IsValidEmail(email))
        {
            errors["email"] = ["email is not a valid email address or it is too long."];
        }

        return errors;
    }

    private static bool IsValidEmail(string email)
    {
        var value = email.Trim();

        if (value.Length > Ticket.MaxHolderEmailLength)
        {
            return false;
        }

        // Requiring the parsed address to equal the input rejects display-name forms
        // such as "Jane <jane@example.com>", which MailAddress would otherwise accept.
        return MailAddress.TryCreate(value, out var parsed) && parsed.Address == value;
    }

    private static bool IsValidFullName(string fullName)
    {
        return fullName.Trim().Length <= Ticket.MaxHolderFullNameLength;
    }
}
