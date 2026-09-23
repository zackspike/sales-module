using System;

namespace BookingService.Domain;

public class Ticket
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string TicketCode { get; set; } = string.Empty;
    public Guid IdempotencyKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
