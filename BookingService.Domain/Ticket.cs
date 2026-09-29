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
}
