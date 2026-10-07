using BookingService.Domain.Exceptions;

namespace BookingService.Domain;

/// <summary>
/// Ticket generated for a seat. It starts <see cref="TicketStatus.Available"/> and becomes
/// <see cref="TicketStatus.Sold"/> once a <see cref="Domain.User"/> buys it.
/// </summary>
public class Ticket
{
    public Guid Id { get; set; }
    public Guid SeatId { get; set; }
    public Seat? Seat { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Available;
    public string? TicketCode { get; set; }
    public Guid? IdempotencyKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PurchasedAtUtc { get; set; }

    /// <summary>
    /// Sells this seat to <paramref name="buyer"/> (SP-05 / DOM-02). A seat can only be sold once.
    /// </summary>
    /// <exception cref="TicketAlreadySoldException">The seat is already sold.</exception>
    public void Purchase(User buyer, Guid idempotencyKey, DateTime purchasedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(buyer);

        if (Status == TicketStatus.Sold)
        {
            throw new TicketAlreadySoldException(Id);
        }

        User = buyer;
        UserId = buyer.Id;
        IdempotencyKey = idempotencyKey;
        TicketCode = TicketCodeGenerator.Generate();
        Status = TicketStatus.Sold;
        PurchasedAtUtc = purchasedAtUtc;
    }
}
