namespace BookingService.Domain;

/// <summary>
/// Domain factory that generates the initial seat/ticket inventory for an event (SP-03 / DOM-03).
/// </summary>
public static class EventInventoryFactory
{
    public static IReadOnlyList<Ticket> CreateInitialInventory(Event @event, string rowPrefix = "A")
    {
        ArgumentNullException.ThrowIfNull(@event);
        return CreateInitialInventory(@event.Id, @event.TotalSeats, rowPrefix);
    }

    public static IReadOnlyList<Ticket> CreateInitialInventory(Guid eventId, int totalSeats, string rowPrefix = "A")
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("EventId must not be empty.", nameof(eventId));
        }

        if (totalSeats <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalSeats), "Total seats must be greater than zero.");
        }

        var tickets = new List<Ticket>(totalSeats);
        var now = DateTime.UtcNow;

        for (var i = 1; i <= totalSeats; i++)
        {
            tickets.Add(new Ticket
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                SeatNumber = $"{rowPrefix}-{i}",
                Status = TicketStatus.Available,
                CreatedAtUtc = now
            });
        }

        return tickets;
    }
}
