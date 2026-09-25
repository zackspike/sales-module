using System;

namespace BookingService.Domain;

public class Event
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
