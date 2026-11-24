using BookingService.Domain.Purchases;

namespace BookingService.Application.Abstractions;

/// <summary>
/// Store of the purchase sessions that govern a purchase (<c>session:{purchaseId}</c>). A session
/// disappears on its own when <see cref="PurchaseSession.ExpiresAtUtc"/> passes, so the use cases never
/// delete one.
/// </summary>
public interface IPurchaseSessionStore
{
    /// <summary>
    /// Stores <paramref name="purchase"/> until <see cref="PurchaseSession.ExpiresAtUtc"/>. The expiration is
    /// taken from the purchase itself, so the stored session and its deadline can never disagree;
    /// saving an already expired purchase stores nothing. Saving again never extends the deadline (ADR-001).
    /// <para>
    /// The implementation must be safe against concurrent writes to the same session (a stored version,
    /// <c>WATCH</c> or a Lua script): two holds of one purchase in flight at the same time each read the
    /// session, add their own seat and save, so a last-write-wins store loses one of the seats. That seat
    /// stays locked in Redis under this purchase id but outside <see cref="PurchaseSession.HeldEventSeatIds"/>,
    /// where nobody can buy or take it until the session expires, and the same gap lets a purchase go
    /// past its seat limit.
    /// </para>
    /// </summary>
    Task SaveAsync(PurchaseSession purchase, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the purchase session <paramref name="purchaseId"/>, or null if it never existed or expired.
    /// A returned session may still be expired, so callers check <see cref="PurchaseSession.IsExpired"/>.
    /// </summary>
    Task<PurchaseSession?> GetAsync(Guid purchaseId, CancellationToken cancellationToken);
}
