using BookingService.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Persistence;

public static class BookingDbSeeder
{
    public static void Seed(BookingDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Events.Any())
        {
            var defaultEvent = InMemoryEventCatalog.DefaultEvent;
            context.Events.Add(defaultEvent);
            context.SaveChanges();

            var initialSeats = EventInventoryFactory.CreateInitialInventory(defaultEvent);
            context.Tickets.AddRange(initialSeats);
            context.SaveChanges();
        }
    }
}
