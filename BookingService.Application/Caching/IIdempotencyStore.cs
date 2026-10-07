namespace BookingService.Application.Caching;

/// <summary>
/// Fast lookup of the idempotency keys already used by successful purchases (SP-06), so a
/// duplicated request is answered without opening a database transaction. Entries expire;
/// the unique <c>purchase_idempotency_key</c> column remains the durable guarantee.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> GetAsync(Guid idempotencyKey, CancellationToken cancellationToken = default);

    Task RememberAsync(
        Guid idempotencyKey,
        IdempotencyRecord record,
        TimeSpan retention,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The seat bought with an idempotency key.
/// </summary>
public sealed record IdempotencyRecord(Guid EventId, Guid TicketId);
