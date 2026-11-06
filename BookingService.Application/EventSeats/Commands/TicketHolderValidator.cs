using System.Net.Mail;
using System.Text.RegularExpressions;
using BookingService.Domain.Tickets;

namespace BookingService.Application.EventSeats.Commands;

/// <summary>
/// Validates the ticket holder data sent to reserve or purchase an event seat (SP-05 / VAL-02):
/// required fields, length and email format. Event and event seat existence are checked by each handler.
/// </summary>
public static partial class TicketHolderValidator
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

        foreach (var (field, messages) in ValidateEmail(email))
        {
            errors[field] = messages;
        }

        return errors;
    }

    /// <summary>
    /// Validates only the buyer email, as needed to reserve a seat; errors are keyed by <c>email</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> ValidateEmail(string email)
    {
        var errors = new Dictionary<string, string[]>();

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

        // The pattern rejects forms MailAddress accepts but a fan's email never has, such as a domain
        // without a dot ("jane@localhost") or a quoted local part. Requiring the parsed address to equal
        // the input also rejects display-name forms such as "Jane <jane@example.com>".
        return EmailPattern().IsMatch(value)
            && MailAddress.TryCreate(value, out var parsed)
            && parsed.Address == value;
    }

    /// <summary>
    /// <c>local@domain.tld</c>: a dot-atom local part (no quotes or spaces), domain labels of 1 to 63
    /// letters, digits or inner hyphens, and a top-level domain that is either alphabetic with at least
    /// 2 letters or an internationalized one in punycode (<c>xn--p1ai</c>, <c>xn--fiqs8s</c>).
    /// </summary>
    [GeneratedRegex(
        @"^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+(?:[A-Za-z]{2,63}|xn--[A-Za-z0-9-]{1,59})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    private static bool IsValidFullName(string fullName)
    {
        return fullName.Trim().Length <= Ticket.MaxHolderFullNameLength;
    }
}
