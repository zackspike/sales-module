using System;

namespace BookingService.Domain;

public class Reservation
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public List<Guid> SeatIds { get; set; } = new();
    public required string UserEmail { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsConfirmed { get; set; }
}
