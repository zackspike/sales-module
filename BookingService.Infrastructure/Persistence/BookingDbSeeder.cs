using BookingService.Domain.Events;

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
            context.Tickets.AddRange(EventInventoryFactory.CreateInitialInventory(defaultEvent));
            context.SaveChanges();
        }
    }
}
