namespace BookingService.Infrastructure.Caching;

/// <summary>
/// A save lost a race with another writer of the same purchase session. It inherits
/// <see cref="InvalidOperationException"/> so the API keeps treating it as a server-side 500.
/// </summary>
public sealed class PurchaseSessionConcurrencyException : InvalidOperationException
{
    public PurchaseSessionConcurrencyException(Guid purchaseId)
        : base($"Purchase session {purchaseId} was modified concurrently.")
    {
        PurchaseId = purchaseId;
    }

    public Guid PurchaseId { get; }
}
