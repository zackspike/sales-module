namespace BookingService.Application.Abstractions;

/// <summary>
/// Fast lookup of the idempotency keys already used by successful purchases (SP-06), so a
/// duplicated request is answered without touching the database. Entries expire; the event seat
/// repository remains the durable guarantee (see <see cref="IEventSeatRepository.GetSaleByIdempotencyKey"/>).
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Returns the purchase made with <paramref name="idempotencyKey"/>, or null if it is not
    /// remembered (never used, or its entry expired).
    /// </summary>
    IdempotencyRecord? GetIdempotencyRecord(Guid idempotencyKey);

    /// <summary>
    /// Remembers that <paramref name="idempotencyKey"/> bought <paramref name="record"/> during
    /// <paramref name="retention"/>. Called only after the sale is committed.
    /// </summary>
    void Remember(Guid idempotencyKey, IdempotencyRecord record, TimeSpan retention);
}

/// <summary>
/// The event seat bought with an idempotency key.
/// </summary>
public sealed record IdempotencyRecord(Guid EventId, Guid EventSeatId);
