using BookingService.Domain.Exceptions;

namespace BookingService.Domain;

public class Ticket
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string SeatNumber { get; set; } = string.Empty;
    public TicketStatus Status { get; set; } = TicketStatus.Available;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string TicketCode { get; set; } = string.Empty;
    public Guid IdempotencyKey { get; set; }
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
