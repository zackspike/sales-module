namespace BookingService.Domain;

/// <summary>
/// Domain factory that generates the initial seat/ticket inventory of an event zone (SP-03 / DOM-03).
/// </summary>
public static class EventInventoryFactory
{
    /// <summary>
    /// Creates <paramref name="totalSeats"/> seats ("{rowPrefix}-1".."{rowPrefix}-N") in
    /// <paramref name="zone"/>, each with its own available ticket.
    /// </summary>
    public static IReadOnlyList<Ticket> CreateInitialInventory(Zone zone, int totalSeats, string rowPrefix = "A")
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (zone.Id == Guid.Empty)
        {
            throw new ArgumentException("ZoneId must not be empty.", nameof(zone));
        }

        if (zone.EventId == Guid.Empty)
        {
            throw new ArgumentException("EventId must not be empty.", nameof(zone));
        }

        if (totalSeats <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalSeats), "Total seats must be greater than zero.");
        }

        var tickets = new List<Ticket>(totalSeats);
        var now = DateTime.UtcNow;

        for (var i = 1; i <= totalSeats; i++)
        {
            var seat = new Seat
            {
                Id = Guid.NewGuid(),
                ZoneId = zone.Id,
                Zone = zone,
                SeatNumber = $"{rowPrefix}-{i}"
            };

            tickets.Add(new Ticket
            {
                Id = Guid.NewGuid(),
                SeatId = seat.Id,
                Seat = seat,
                Status = TicketStatus.Available,
                CreatedAtUtc = now
            });
        }

        return tickets;
    }
}
