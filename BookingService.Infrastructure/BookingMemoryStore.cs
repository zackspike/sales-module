using System.Collections.Concurrent;
using BookingService.Domain;

namespace BookingService.Infrastructure;

public class BookingMemoryStore
{
    public ConcurrentDictionary<Guid, Ticket> Tickets { get; } = new();

    public BookingMemoryStore()
    {
        
    }
}