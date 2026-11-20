using BookingService.Domain.Common;

namespace BookingService.Domain.Purchases;

public sealed class PurchaseExpiredException : DomainException
{
    public PurchaseExpiredException(Guid purchaseId)
        : base("SESSION_EXPIRED", "The purchase session has expired.")
    {
        PurchaseId = purchaseId;
    }

    public Guid PurchaseId { get; }
}
