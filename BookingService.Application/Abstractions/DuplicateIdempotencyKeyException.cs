namespace BookingService.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="ITicketRepository.Update"/> when the ticket's idempotency key is already
/// claimed by another ticket, e.g. a concurrent purchase of a different seat with the same key.
/// </summary>
public sealed class DuplicateIdempotencyKeyException : Exception
{
    public DuplicateIdempotencyKeyException(Guid idempotencyKey, Exception? innerException = null)
        : base($"Idempotency key '{idempotencyKey}' is already used by another ticket.", innerException)
    {
        IdempotencyKey = idempotencyKey;
    }

    public Guid IdempotencyKey { get; }
}
