using BookingService.Application.Abstractions;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tickets.Commands;

/// <summary>
/// Buys a specific seat (SP-05 / SP-06 / APP-04): validates name, email and event (400/404),
/// then sells the seat atomically and idempotently through <see cref="ITicketRepository.PurchaseOnce"/>.
/// The "a seat is sold only once" rule lives in <see cref="Domain.Tickets.Ticket.Purchase"/>.
/// </summary>
public sealed class PurchaseTicketHandler
{
    private readonly TicketPurchaseValidator _validator;
    private readonly ITicketRepository _tickets;

    public PurchaseTicketHandler(TicketPurchaseValidator validator, ITicketRepository tickets)
    {
        _validator = validator;
        _tickets = tickets;
    }

    public PurchaseTicketResult Handle(PurchaseTicketCommand command)
    {
        var validation = _validator.Validate(command);

        if (validation.Status == TicketPurchaseValidationStatus.Invalid)
        {
            return PurchaseTicketResult.Invalid(validation.Errors);
        }

        if (command.IdempotencyKey == Guid.Empty)
        {
            return PurchaseTicketResult.Invalid(new Dictionary<string, string[]>
            {
                ["idempotencyKey"] = ["X-Idempotency-Key is required."]
            });
        }

        if (validation.Status == TicketPurchaseValidationStatus.EventNotFound)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.EventNotFound);
        }

        var fullName = command.FullName!.Trim();
        var email = command.Email!.Trim();

        TicketPurchaseOutcome outcome;
        try
        {
            outcome = _tickets.PurchaseOnce(
                command.IdempotencyKey,
                command.EventId,
                command.TicketId,
                ticket => ticket.Purchase(fullName, email, command.IdempotencyKey, DateTime.UtcNow));
        }
        catch (TicketAlreadySoldException)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.AlreadySold);
        }

        if (outcome.Ticket is null)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.TicketNotFound);
        }

        if (!outcome.Replayed)
        {
            return PurchaseTicketResult.Purchased(outcome.Ticket);
        }

        return outcome.Ticket.Id == command.TicketId && outcome.Ticket.EventId == command.EventId
            ? PurchaseTicketResult.Replayed(outcome.Ticket)
            : PurchaseTicketResult.Failed(PurchaseTicketStatus.IdempotencyKeyConflict);
    }
}
