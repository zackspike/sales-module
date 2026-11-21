namespace BookingService.Application.Purchases.ReadModels;

/// <summary>
/// The payment token the front sends to Payment's <c>/authorize</c>.
/// </summary>
/// <param name="Token">Opaque token issued by Payment's <c>/init</c>.</param>
/// <param name="ExpiresAtUtc">The session's deadline: starting a payment never extends it.</param>
public sealed record StartedPayment(string Token, DateTime ExpiresAtUtc);
