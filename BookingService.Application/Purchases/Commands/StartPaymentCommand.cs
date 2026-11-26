namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// Input of POST /startpayment. The purchase comes from the purchase cookie, so the request carries no body.
/// </summary>
public sealed record StartPaymentCommand(Guid PurchaseId);
