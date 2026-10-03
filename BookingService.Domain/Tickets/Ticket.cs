namespace BookingService.Domain.Tickets;

/// <summary>
/// Aggregate root representing an individual seat/ticket for an event (SP-05 / DOM-02).
/// Invariants and status mutations are strictly encapsulated.
/// </summary>
public class Ticket
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string SeatNumber { get; private set; } = string.Empty;
    public TicketStatus Status { get; private set; } = TicketStatus.Available;
    public string? FullName { get; private set; }
    public string? Email { get; private set; }
    public string? TicketCode { get; private set; }
    public Guid? IdempotencyKey { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? PurchasedAtUtc { get; private set; }

    /// <summary>
    /// Parameterless constructor required by EF Core for entity materialization.
    /// </summary>
    private Ticket()
    {
    }

    /// <summary>
    /// Creates an available seat ticket within an event's initial inventory.
    /// </summary>
    public Ticket(Guid id, Guid eventId, string seatNumber, DateTime createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be empty.", nameof(id));
        }

        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("EventId must not be empty.", nameof(eventId));
        }

        if (string.IsNullOrWhiteSpace(seatNumber))
        {
            throw new ArgumentException("SeatNumber must not be empty.", nameof(seatNumber));
        }

        Id = id;
        EventId = eventId;
        SeatNumber = seatNumber.Trim();
        CreatedAtUtc = createdAtUtc;
        Status = TicketStatus.Available;
    }

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

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("FullName is required.", nameof(fullName));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("IdempotencyKey must not be empty.", nameof(idempotencyKey));
        }

        FullName = fullName.Trim();
        Email = email.Trim();
        IdempotencyKey = idempotencyKey;
        TicketCode = TicketCodeGenerator.Generate();
        Status = TicketStatus.Sold;
        PurchasedAtUtc = purchasedAtUtc;
    }
}
