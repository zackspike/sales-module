using System.Collections.Concurrent;
using BookingService.Domain;

namespace BookingService.Infrastructure;

public class BookingMemoryStore
{
    public ConcurrentDictionary<Guid, Seat> Seats { get; } = new();
    public ConcurrentDictionary<Guid, Reservation> Reservations { get; } = new();
    public ConcurrentDictionary<Guid, Order> Orders { get; } = new();
    public ConcurrentDictionary<Guid, Ticket> Tickets { get; } = new();

    public BookingMemoryStore()
    {
        // Prueba con un EventId fijo
        var sampleEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        for (int i = 1; i <= 10; i++)
        {
            var seat = new Seat
            {
                Id = Guid.NewGuid(),
                EventId = sampleEventId,
                Section = i <= 3 ? "VIP" : "General",
                SeatNumber = $"A-{i}",
                Price = i <= 3 ? 150m : 60m,
                Status = "Available"
            };
            Seats[seat.Id] = seat;
        }
    }
}