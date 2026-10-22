using BookingService.Domain.Events;
using BookingService.Infrastructure.Persistence;

namespace BookingService.Infrastructure.Tests;

public class InMemoryEventCatalogTests
{
    private readonly InMemoryEventCatalog _catalog = new();

    [Fact]
    public void Known_event_exists()
    {
        Assert.True(_catalog.Exists(InMemoryEventCatalog.DefaultEvent.Id));
    }

    [Fact]
    public void Unknown_event_does_not_exist()
    {
        Assert.False(_catalog.Exists(Guid.NewGuid()));
    }
}
