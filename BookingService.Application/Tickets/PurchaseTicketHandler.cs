using BookingService.Application.Caching;
using BookingService.Application.Repositories;
using BookingService.Domain;
using BookingService.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace BookingService.Application.Tickets;

/// <summary>
/// Buys a reserved seat (SP-05 / SP-06 / APP-04): validates name, email and event (400/404),
/// answers duplicated requests from the idempotency store, requires the buyer to hold the seat
/// lock taken by <see cref="ReserveSeatHandler"/> (403), and then, in a single database
/// transaction, checks the idempotency key, locks the ticket row, and sells the seat.
/// The "a seat is sold only once" rule lives in <see cref="Ticket.Purchase"/>.
/// </summary>
public sealed class PurchaseTicketHandler
{
    /// <summary>
    /// How long a used idempotency key is answered from the idempotency store.
    /// </summary>
    public static readonly TimeSpan IdempotencyKeyRetention = TimeSpan.FromMinutes(10);

    private readonly TicketPurchaseValidator _validator;
    private readonly ITicketRepository _tickets;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISeatLockStore _seatLocks;
    private readonly IIdempotencyStore _idempotency;
    private readonly IAvailableSeatsCache _availableSeats;
    private readonly ILogger<PurchaseTicketHandler> _logger;

    public PurchaseTicketHandler(
        TicketPurchaseValidator validator,
        ITicketRepository tickets,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ISeatLockStore seatLocks,
        IIdempotencyStore idempotency,
        IAvailableSeatsCache availableSeats,
        ILogger<PurchaseTicketHandler> logger)
    {
        _validator = validator;
        _tickets = tickets;
        _users = users;
        _unitOfWork = unitOfWork;
        _seatLocks = seatLocks;
        _idempotency = idempotency;
        _availableSeats = availableSeats;
        _logger = logger;
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

        var remembered = await _idempotency.GetAsync(command.IdempotencyKey, cancellationToken);
        if (remembered is not null)
        {
            return await ReplayRememberedPurchaseAsync(remembered, command, cancellationToken);
        }

        var ticket = await _tickets.GetByIdAsync(command.EventId, command.TicketId, cancellationToken);
        if (ticket is null)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.TicketNotFound);
        }

        if (ticket.Status == TicketStatus.Sold)
        {
            // Covers retries whose idempotency entry expired: the database still knows the key.
            return ticket.IdempotencyKey == command.IdempotencyKey
                ? PurchaseTicketResult.Replayed(ticket)
                : PurchaseTicketResult.Failed(PurchaseTicketStatus.AlreadySold);
        }

        var buyer = await _users.GetByEmailAsync(email, cancellationToken);
        var lockHolder = await _seatLocks.GetHolderAsync(command.TicketId, cancellationToken);
        if (buyer is null || lockHolder != buyer.Id)
        {
            // The key may already have bought another seat (expired idempotency entry).
            var previousPurchase = await _tickets.GetByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
            return previousPurchase is null
                ? PurchaseTicketResult.Failed(PurchaseTicketStatus.ReservationRequired)
                : ReplayOrConflict(previousPurchase, command);
        }

        PurchaseTicketResult result;
        try
        {
            result = await PurchaseInTransactionAsync(command, fullName, email, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // A concurrent request committed the same idempotency key first. Its data is
            // visible now, so a second attempt resolves to a replay or a conflict.
            result = await PurchaseInTransactionAsync(command, fullName, email, cancellationToken);
        }

        if (result.Status == PurchaseTicketStatus.Purchased)
        {
            await AfterPurchaseCommittedAsync(result.Ticket!, command, cancellationToken);
        }

        return result;
    }

    private async Task<PurchaseTicketResult> ReplayRememberedPurchaseAsync(
        IdempotencyRecord remembered,
        PurchaseTicketCommand command,
        CancellationToken cancellationToken)
    {
        if (remembered.TicketId != command.TicketId || remembered.EventId != command.EventId)
        {
            return PurchaseTicketResult.Failed(PurchaseTicketStatus.IdempotencyKeyConflict);
        }

        var ticket = await _tickets.GetByIdAsync(command.EventId, command.TicketId, cancellationToken);
        return ticket is null
            ? PurchaseTicketResult.Failed(PurchaseTicketStatus.TicketNotFound)
            : PurchaseTicketResult.Replayed(ticket);
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

    /// <summary>
    /// Updates the hot-path stores once the sale is durable. Failures are only logged: the sale is
    /// already committed, the lock and cache expire on their own, and replays fall back to the database.
    /// </summary>
    private async Task AfterPurchaseCommittedAsync(
        Ticket ticket,
        PurchaseTicketCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            await _idempotency.RememberAsync(
                command.IdempotencyKey,
                new IdempotencyRecord(command.EventId, ticket.Id),
                IdempotencyKeyRetention,
                cancellationToken);
            await _seatLocks.ReleaseAsync(ticket.Id, ticket.UserId!.Value, cancellationToken);
            await _availableSeats.InvalidateAsync(command.EventId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Ticket {TicketId} was sold but the seat lock/idempotency/cache stores could not be updated.",
                ticket.Id);
        }
    }

    private static PurchaseTicketResult ReplayOrConflict(Ticket previousPurchase, PurchaseTicketCommand command)
    {
        return previousPurchase.Id == command.TicketId && previousPurchase.Seat?.Zone?.EventId == command.EventId
            ? PurchaseTicketResult.Replayed(previousPurchase)
            : PurchaseTicketResult.Failed(PurchaseTicketStatus.IdempotencyKeyConflict);
    }
}
