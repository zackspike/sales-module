using System;

namespace BookingService.Domain;

public class Seat
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public required string Section { get; set; }
    public required string SeatNumber { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = "Available"; 
    // 3 possible values: "Available", "Reserved", "Sold"
}
