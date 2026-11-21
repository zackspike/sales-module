using BookingService.Application.EventSeats.ReadModels;

namespace BookingService.Application.Purchases.ReadModels;

/// <summary>
/// A paid purchase and the tickets issued for its seats.
/// </summary>
/// <param name="PurchaseId">Unique identifier of the purchase.</param>
/// <param name="Tickets">One ticket per seat the purchase held.</param>
public sealed record ConfirmedPurchase(Guid PurchaseId, IReadOnlyList<IssuedTicket> Tickets);
