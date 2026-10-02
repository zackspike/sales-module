using BookingService.Domain.Exceptions;

namespace BookingService.Domain;

public class Ticket
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid SeatId { get; private set; } // The seat associated with this ticket
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string TicketCode { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PurchasedAtUtc { get; set; }

    /// <summary>
    /// Sells this seat to the given customer (SP-05 / DOM-02). A seat can only be sold once.
    /// </summary>
    /// <exception cref="TicketAlreadySoldException">The seat is already sold.</exception>
    public void Purchase(string fullName, string email, Guid idempotencyKey, DateTime purchasedAtUtc)
    {
        if (Status == TicketStatus.Sold)
        {
            throw new TicketAlreadySoldException(Id);
        }

        FullName = fullName;
        Email = email;
        IdempotencyKey = idempotencyKey;
        TicketCode = TicketCodeGenerator.Generate();
        Status = TicketStatus.Sold;
        PurchasedAtUtc = purchasedAtUtc;
    }
}
