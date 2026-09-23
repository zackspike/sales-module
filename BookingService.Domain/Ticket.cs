using System;

namespace BookingService.Domain;

public class Ticket
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid SeatId { get; set; }
    public required string TicketNumber { get; set; }
    public required string Section { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
