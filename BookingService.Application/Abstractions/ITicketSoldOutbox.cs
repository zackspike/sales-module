namespace BookingService.Application.Abstractions;

/// <summary>
/// Transactional outbox of the <c>ticketSold</c> event, the only event Booking publishes (consumed by
/// EventService). A relay publishes the stored messages afterwards, at least once.
/// <para>
/// <see cref="AddAsync"/> is only called inside the <c>capture</c> callback of
/// <see cref="IPurchaseSaleRepository.SellOnceAsync"/>, and its writes must join that open transaction
/// (ADR-003): a sale that is rolled back leaves no message, and a committed sale always leaves one per ticket.
/// </para>
/// </summary>
public interface ITicketSoldOutbox
{
    /// <summary>
    /// Stores one <c>ticketSold</c> message per element of <paramref name="messages"/> in the current transaction.
    /// The envelope (<c>messageId</c>, <c>type</c>, <c>occurredAt</c>) is added by the implementation.
    /// </summary>
    Task AddAsync(IReadOnlyList<TicketSoldEvent> messages, CancellationToken cancellationToken);
}

/// <summary>
/// Payload of the <c>ticketSold</c> event: one per ticket issued.
/// </summary>
public sealed record TicketSoldEvent(
    Guid PurchaseId,
    string TicketCode,
    Guid EventId,
    Guid EventSeatId,
    string HolderName,
    string HolderEmail,
    DateTime SoldAtUtc);
