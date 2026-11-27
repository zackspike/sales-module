namespace BookingService.Infrastructure.Persistence;

/// <summary>
/// The idempotency key of a paid purchase ("Pagar") and the purchase that used it. Its primary key makes
/// concurrent calls with the same key wait for each other, and every ticket sold with the key points back to it.
/// </summary>
public sealed class PurchaseSaleRow
{
    public Guid IdempotencyKey { get; init; }

    public Guid PurchaseId { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}
