namespace BookingService.Application.Purchases.ReadModels;

/// <summary>
/// The purchase session opened by <see cref="Commands.StartPurchaseHandler"/>.
/// </summary>
/// <param name="PurchaseId">Identifier of the purchase; it travels in the purchase cookie.</param>
/// <param name="ExpiresAtUtc">UTC time when the session and every seat it holds expire.</param>
public sealed record StartedPurchase(Guid PurchaseId, DateTime ExpiresAtUtc);
