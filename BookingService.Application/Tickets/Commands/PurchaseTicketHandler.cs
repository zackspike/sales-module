using BookingService.Application.Abstractions;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tickets.Commands;

/// <summary>
/// Buys a specific seat (SP-05 / SP-06 / APP-04): validates name, email and event (400/404),
/// orchestrates idempotency checks and executes the business invariant via <see cref="Domain.Tickets.Ticket.Purchase"/>.
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

        // 1. Idempotency Check
        var existingTicket = _tickets.GetByIdempotencyKey(command.IdempotencyKey);
        if (existingTicket is not null)
        {
            return existingTicket.Id == command.TicketId && existingTicket.EventId == command.EventId
                ? PurchaseTicketResult.Replayed(existingTicket)
                : PurchaseTicketResult.Failed(PurchaseTicketStatus.IdempotencyKeyConflict);
        }

        // 2. Fetch Aggregate Root
        var ticket = _tickets.GetById(command.EventId, command.TicketId);
        if (ticket is null)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.TicketNotFound);
        }

        // 3. Domain Logic Execution on Aggregate
        var fullName = command.FullName!.Trim();
        var email = command.Email!.Trim();
        try
        {
            ticket.Purchase(fullName, email, command.IdempotencyKey, DateTime.UtcNow);
        }
        catch (TicketAlreadySoldException)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.AlreadySold);
        }

        // 4. Persistence with Concurrency Conflict Handling
        try
        {
            _tickets.Update(ticket);
        }
        catch (TicketAlreadySoldException)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.AlreadySold);
        }

        return PurchaseTicketResult.Purchased(ticket);
    }
}
