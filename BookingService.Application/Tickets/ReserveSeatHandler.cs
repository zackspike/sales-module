using BookingService.Application.Caching;
using BookingService.Application.Repositories;
using BookingService.Domain;

namespace BookingService.Application.Tickets;

/// <summary>
/// First step of the purchase flow: the fan selects a seat and clicks "comprar".
/// Checks the seat's availability in the database, registers the buyer (by email) and locks the
/// seat for them in the seat lock store during <see cref="SeatReservationPolicy.LockDuration"/>.
/// </summary>
public sealed class ReserveSeatHandler
{
    private readonly TicketPurchaseValidator _validator;
    private readonly ITicketRepository _tickets;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISeatLockStore _seatLocks;
    private readonly TimeProvider _timeProvider;

    public ReserveSeatHandler(
        TicketPurchaseValidator validator,
        ITicketRepository tickets,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ISeatLockStore seatLocks,
        TimeProvider timeProvider)
    {
        _validator = validator;
        _tickets = tickets;
        _users = users;
        _unitOfWork = unitOfWork;
        _seatLocks = seatLocks;
        _timeProvider = timeProvider;
    }

    public async Task<ReserveSeatResult> HandleAsync(
        ReserveSeatCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _validator.ValidateAsync(
            command.EventId,
            command.FullName,
            command.Email,
            cancellationToken);

        if (validation.Status == TicketPurchaseValidationStatus.Invalid)
        {
            return ReserveSeatResult.Invalid(validation.Errors);
        }

        if (validation.Status == TicketPurchaseValidationStatus.EventNotFound)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.EventNotFound);
        }

        var ticket = await _tickets.GetByIdAsync(command.EventId, command.TicketId, cancellationToken);
        if (ticket is null)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.TicketNotFound);
        }

        if (ticket.Status == TicketStatus.Sold)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.AlreadySold);
        }

        var buyer = await GetOrRegisterBuyerAsync(command.FullName!.Trim(), command.Email!.Trim(), cancellationToken);
        var expiresAtUtc = _timeProvider.GetUtcNow().UtcDateTime + SeatReservationPolicy.LockDuration;

        var lockResult = await _seatLocks.TryAcquireAsync(
            ticket.Id,
            buyer.Id,
            SeatReservationPolicy.LockDuration,
            cancellationToken);

        if (lockResult == SeatLockResult.HeldByAnotherUser)
        {
            return ReserveSeatResult.Failed(ReserveSeatStatus.LockedByAnotherUser);
        }

        // The seat may have been sold (and its lock released) between the availability check
        // and the lock: re-check so a sold seat is never left reserved.
        var current = await _tickets.GetByIdAsync(command.EventId, command.TicketId, cancellationToken);
        if (current is null || current.Status == TicketStatus.Sold)
        {
            await _seatLocks.ReleaseAsync(ticket.Id, buyer.Id, cancellationToken);
            return ReserveSeatResult.Failed(ReserveSeatStatus.AlreadySold);
        }

        return ReserveSeatResult.Reserved(current, buyer.Id, expiresAtUtc);
    }

    private async Task<User> GetOrRegisterBuyerAsync(string fullName, string email, CancellationToken cancellationToken)
    {
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    var buyer = await _users.GetByEmailAsync(email, token);
                    if (buyer is not null)
                    {
                        return buyer;
                    }

                    buyer = new User
                    {
                        Id = Guid.NewGuid(),
                        FullName = fullName,
                        Email = email
                    };
                    _users.Add(buyer);
                    await _unitOfWork.SaveChangesAsync(token);

                    return buyer;
                },
                cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // A concurrent request registered the same email first; use that user.
            return await _users.GetByEmailAsync(email, cancellationToken)
                ?? throw new InvalidOperationException($"User '{email}' disappeared after a concurrent registration.");
        }
    }
}
