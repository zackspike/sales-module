namespace BookingService.Application.Abstractions;

/// <summary>
/// Fast lookup of the idempotency keys already used by successful purchases (SP-06), so a
/// duplicated request is answered without touching the database. Entries expire; the ticket
/// repository remains the durable guarantee.
/// </summary>
public interface IIdempotencyStore
{
    IdempotencyRecord? Get(Guid idempotencyKey);

    void Remember(Guid idempotencyKey, IdempotencyRecord record, TimeSpan retention);
}

/// <summary>
/// The seat bought with an idempotency key.
/// </summary>
public sealed record IdempotencyRecord(Guid EventId, Guid TicketId);
