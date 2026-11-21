using BookingService.Application.Abstractions;
using BookingService.Application.EventSeats.Commands;
using BookingService.Application.Purchases.ReadModels;
using BookingService.Domain.Purchases;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// Opens the purchase session that governs a purchase: validates the buyer's name and email (400) and
/// stores a new <see cref="Purchase"/> until it expires. From here on the buyer is identified by the
/// purchase id, so no later call carries the email again.
/// </summary>
public sealed class StartPurchaseHandler
{
    private readonly IPurchaseSessionStore _sessions;
    private readonly PurchaseSessionOptions _options;

    public StartPurchaseHandler(IPurchaseSessionStore sessions, PurchaseSessionOptions options)
    {
        _sessions = sessions;
        _options = options;
    }

    public async Task<StartPurchaseResult> HandleAsync(
        StartPurchaseCommand command,
        CancellationToken cancellationToken)
    {
        var errors = NameAndEmailErrorsOf(command);
        if (errors.Count > 0)
        {
            return StartPurchaseResult.Invalid(errors);
        }

        var purchase = Purchase.Start(
            command.Name.Trim(),
            command.Email.Trim(),
            DateTime.UtcNow,
            _options.SessionDuration);

        await _sessions.SaveAsync(purchase, cancellationToken);

        return StartPurchaseResult.Started(new StartedPurchase(purchase.Id, purchase.ExpiresAtUtc));
    }

    /// <summary>
    /// Errors keyed by JSON field name (<c>name</c>, <c>email</c>); empty when the buyer's data is valid.
    /// The email rules are the ticket holder's, reused as they are; the name is checked here because the
    /// purchase calls the field <c>name</c> while a ticket calls it <c>fullName</c>.
    /// </summary>
    private static Dictionary<string, string[]> NameAndEmailErrorsOf(StartPurchaseCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            errors["name"] = ["name is required."];
        }
        else if (command.Name.Trim().Length > Ticket.MaxHolderFullNameLength)
        {
            errors["name"] = [$"name must be at most {Ticket.MaxHolderFullNameLength} characters."];
        }

        foreach (var (field, messages) in TicketHolderValidator.ValidateEmail(command.Email))
        {
            errors[field] = messages;
        }

        return errors;
    }
}
