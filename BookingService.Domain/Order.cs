using System;

namespace BookingService.Domain;

public class Order
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid ReservationId { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "PendingPayment"; 
    // 2 possible values: "PendingPayment", "Paid"
    public DateTime CreatedAtUtc { get; set; }
}
