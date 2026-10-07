using BookingService.Application.Repositories;
using BookingService.Domain;
using BookingService.Domain.Exceptions;

namespace BookingService.Application.Tickets;

/// <summary>
/// Buys a specific seat (SP-05 / SP-06 / APP-04): validates name, email and event (400/404),
/// then, in a single database transaction, checks the idempotency key, locks the ticket of the
/// seat, registers the buyer and sells the seat. The "a seat is sold only once" rule lives in
/// <see cref="Ticket.Purchase"/>.
/// </summary>
public sealed class PurchaseTicketHandler
{
    private readonly TicketPurchaseValidator _validator;
    private readonly ITicketRepository _tickets;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;

    public PurchaseTicketHandler(
        TicketPurchaseValidator validator,
        ITicketRepository tickets,
        IUserRepository users,
        IUnitOfWork unitOfWork)
    {
        _validator = validator;
        _tickets = tickets;
        _users = users;
        _unitOfWork = unitOfWork;
    }

    public async Task<PurchaseTicketResult> HandleAsync(
        PurchaseTicketCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _validator.ValidateAsync(command, cancellationToken);

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

        try
        {
            return await PurchaseInTransactionAsync(command, fullName, email, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // A concurrent request committed the same idempotency key or registered the same
            // email first. Its data is visible now, so a second attempt resolves to a replay,
            // a conflict or a purchase with the existing user.
            return await PurchaseInTransactionAsync(command, fullName, email, cancellationToken);
        }
    }

    private async Task<PurchaseTicketResult> PurchaseInTransactionAsync(
        PurchaseTicketCommand command,
        string fullName,
        string email,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(
                token => PurchaseAsync(command, fullName, email, token),
                cancellationToken);
        }
        catch (TicketAlreadySoldException)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.AlreadySold);
        }
    }

    private async Task<PurchaseTicketResult> PurchaseAsync(
        PurchaseTicketCommand command,
        string fullName,
        string email,
        CancellationToken cancellationToken)
    {
        var previousPurchase = await _tickets.GetByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
        if (previousPurchase is not null)
        {
            return ReplayOrConflict(previousPurchase, command);
        }

        // Locks the ticket row: a concurrent buyer of the same seat waits here until this
        // transaction ends and then sees the seat as sold.
        var ticket = await _tickets.GetForPurchaseAsync(command.EventId, command.TicketId, cancellationToken);
        if (ticket is null)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.TicketNotFound);
        }

        // A concurrent retry with the same key may have sold this seat while we waited for the lock.
        if (ticket.Status == TicketStatus.Sold && ticket.IdempotencyKey == command.IdempotencyKey)
        {
            return PurchaseTicketResult.Replayed(ticket);
        }

        var buyer = await _users.GetByEmailAsync(email, cancellationToken);
        if (buyer is null)
        {
            buyer = new User
            {
                Id = Guid.NewGuid(),
                FullName = fullName,
                Email = email
            };
            _users.Add(buyer);
        }

        ticket.Purchase(buyer, command.IdempotencyKey, DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return PurchaseTicketResult.Purchased(ticket);
    }

    private static PurchaseTicketResult ReplayOrConflict(Ticket previousPurchase, PurchaseTicketCommand command)
    {
        return previousPurchase.Id == command.TicketId && previousPurchase.Seat?.Zone?.EventId == command.EventId
            ? PurchaseTicketResult.Replayed(previousPurchase)
            : PurchaseTicketResult.Failed(PurchaseTicketStatus.IdempotencyKeyConflict);
    }
}
