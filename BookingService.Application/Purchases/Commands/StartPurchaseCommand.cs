namespace BookingService.Application.Purchases.Commands;

/// <summary>
/// Input of the first step of a purchase: POST /purchase. The buyer identifies itself once here and
/// the resulting purchase id travels in the purchase cookie from then on.
/// </summary>
public sealed record StartPurchaseCommand(string Name, string Email);
