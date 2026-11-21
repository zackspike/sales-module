namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// Input of POST /purchase/pay ("Pagar") with the <c>Idempotency-Key</c> header. The purchase comes from the
/// purchase cookie, so the request carries no body.
/// </summary>
public sealed record ConfirmPurchaseCommand(Guid PurchaseId, Guid IdempotencyKey);
