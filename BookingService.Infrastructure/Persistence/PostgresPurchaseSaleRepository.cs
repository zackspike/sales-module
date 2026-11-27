using BookingService.Application.Abstractions;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;
using BookingService.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BookingService.Infrastructure.Persistence;

/// <summary>
/// Sells every seat a purchase holds in one PostgreSQL transaction that commits only once Payment captured the
/// payment (ADR-003). The <c>ticketSold</c> messages that the capture callback writes through
/// <see cref="Messaging.PostgresTicketSoldOutbox"/> use the same <see cref="BookingDbContext"/>, so they join
/// this transaction: a rollback leaves neither sold seats nor messages.
/// <para>
/// Concurrency: the idempotency key row (primary key of <c>purchase_sales</c>) makes two calls with the same key
/// wait for each other, and the <c>xmin</c> token of each seat plus the unique index on <c>tickets.EventSeatId</c>
/// keep two purchases from selling the same seat.
/// </para>
/// </summary>
public sealed class PostgresPurchaseSaleRepository : IPurchaseSaleRepository
{
    internal const string PurchaseSaleKeyPropertyName = "PurchaseSaleIdempotencyKey";
    private const string PurchaseSalesPrimaryKeyName = "PK_purchase_sales";
    private const string EventSeatSoldIndexName = "IX_tickets_EventSeatId";

    private readonly BookingDbContext _dbContext;

    public PostgresPurchaseSaleRepository(BookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PurchaseSaleRecord?> FindByIdempotencyKeyAsync(
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.PurchaseSales
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IdempotencyKey == idempotencyKey, cancellationToken);

        return row is null
            ? null
            : new PurchaseSaleRecord(row.PurchaseId, await SalesOfAsync(idempotencyKey, cancellationToken));
    }

    public async Task<PurchaseSaleOutcome> SellOnceAsync(
        Guid idempotencyKey,
        PurchaseSession purchase,
        Func<EventSeat, Ticket> sell,
        Func<IReadOnlyList<EventSeatSale>, CancellationToken, Task<PaymentCaptureResult>> capture,
        CancellationToken cancellationToken)
    {
        if (await ReplayOfAsync(idempotencyKey, purchase.Id, cancellationToken) is { } replay)
        {
            return replay;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Claims the key first: a concurrent call with the same key waits here until this one ends.
            _dbContext.PurchaseSales.Add(new PurchaseSaleRow
            {
                IdempotencyKey = idempotencyKey,
                PurchaseId = purchase.Id,
                CreatedAtUtc = DateTime.UtcNow
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception, PurchaseSalesPrimaryKeyName))
            {
                // Another call with the same key committed first.
                await RollbackAsync(transaction);
                return await ReplayOfAsync(idempotencyKey, purchase.Id, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Idempotency key {idempotencyKey} was taken by a sale that is no longer stored.");
            }

            var heldEventSeatIds = purchase.HeldEventSeatIds.ToList();
            var seats = await _dbContext.EventSeats
                .Where(s => s.EventId == purchase.EventId && heldEventSeatIds.Contains(s.Id))
                .ToListAsync(cancellationToken);

            var failedSeats = FailedSeatsOf(heldEventSeatIds, seats);
            if (failedSeats.Count > 0)
            {
                await RollbackAsync(transaction);
                return PurchaseSaleOutcome.SeatsUnavailable(failedSeats);
            }

            var sales = new List<EventSeatSale>(heldEventSeatIds.Count);
            foreach (var seat in heldEventSeatIds.Select(id => seats.First(s => s.Id == id)))
            {
                var ticket = sell(seat);
                _dbContext.Tickets.Add(ticket);
                _dbContext.Entry(ticket).Property<Guid?>(PurchaseSaleKeyPropertyName).CurrentValue = idempotencyKey;
                sales.Add(new EventSeatSale(seat, ticket));
            }

            try
            {
                // xmin token: 0 rows updated if another purchase sold a seat after it was read.
                // Unique index on EventSeatId: another purchase's ticket for a seat was committed first.
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (
                exception is DbUpdateConcurrencyException || IsUniqueViolation(exception, EventSeatSoldIndexName))
            {
                await RollbackAsync(transaction);
                return PurchaseSaleOutcome.SeatsUnavailable(
                    await FailedSeatsAfterLostRaceAsync(purchase, heldEventSeatIds, cancellationToken));
            }

            var captured = await capture(sales, cancellationToken);
            if (captured.Status != PaymentCallStatus.Succeeded)
            {
                await RollbackAsync(transaction);
                return PurchaseSaleOutcome.NotCaptured(captured.Status);
            }

            // The payment is taken: a client that disconnects now must not undo the sale (decision 23).
            await transaction.CommitAsync(CancellationToken.None);
            return PurchaseSaleOutcome.Sold(sales);
        }
        catch
        {
            // sell or capture threw (or the database failed) before the commit: nothing is kept.
            await RollbackAsync(transaction);
            throw;
        }
    }

    private async Task<PurchaseSaleOutcome?> ReplayOfAsync(
        Guid idempotencyKey,
        Guid purchaseId,
        CancellationToken cancellationToken)
    {
        var paid = await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (paid is null)
        {
            return null;
        }

        return paid.PurchaseId == purchaseId
            ? PurchaseSaleOutcome.Replayed(paid.Sales)
            : PurchaseSaleOutcome.IdempotencyKeyConflict();
    }

    private async Task<IReadOnlyList<EventSeatSale>> SalesOfAsync(
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        var tickets = await _dbContext.Tickets
            .AsNoTracking()
            .Where(t => EF.Property<Guid?>(t, PurchaseSaleKeyPropertyName) == idempotencyKey)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);

        var eventSeatIds = tickets.Select(t => t.EventSeatId).ToList();
        var seats = await _dbContext.EventSeats
            .AsNoTracking()
            .Where(s => eventSeatIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        return tickets.Select(t => new EventSeatSale(seats[t.EventSeatId], t)).ToList();
    }

    private static List<FailedEventSeat> FailedSeatsOf(
        IReadOnlyList<Guid> heldEventSeatIds,
        IReadOnlyCollection<EventSeat> seats)
    {
        var failedSeats = new List<FailedEventSeat>();
        foreach (var eventSeatId in heldEventSeatIds)
        {
            var seat = seats.FirstOrDefault(s => s.Id == eventSeatId);
            if (seat is null)
            {
                failedSeats.Add(new FailedEventSeat(eventSeatId, SeatSaleFailure.NotFound));
            }
            else if (seat.Status == EventSeatStatus.Sold)
            {
                failedSeats.Add(new FailedEventSeat(eventSeatId, SeatSaleFailure.AlreadySold));
            }
        }

        return failedSeats;
    }

    /// <summary>
    /// After losing a race for some seat, reads the committed state again to report every seat that is gone.
    /// A concurrency error only happens once another sale committed, so at least one seat shows as sold.
    /// </summary>
    private async Task<IReadOnlyList<FailedEventSeat>> FailedSeatsAfterLostRaceAsync(
        PurchaseSession purchase,
        IReadOnlyList<Guid> heldEventSeatIds,
        CancellationToken cancellationToken)
    {
        var seats = await _dbContext.EventSeats
            .AsNoTracking()
            .Where(s => s.EventId == purchase.EventId && heldEventSeatIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        var failedSeats = FailedSeatsOf(heldEventSeatIds, seats);
        return failedSeats.Count > 0
            ? failedSeats
            : heldEventSeatIds.Select(id => new FailedEventSeat(id, SeatSaleFailure.AlreadySold)).ToList();
    }

    /// <summary>
    /// Rolls back and forgets what was tracked, so the context can be used again (for a replay lookup, or by the
    /// rest of the request). Never cancelled: a rollback must always finish. Does nothing to a transaction that
    /// already ended (Npgsql clears its connection once it commits or rolls back).
    /// </summary>
    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        if (transaction.GetDbTransaction().Connection is not null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }

        _dbContext.ChangeTracker.Clear();
    }

    private static bool IsUniqueViolation(DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgres && postgres.ConstraintName == constraintName;
}
