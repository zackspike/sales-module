using BookingService.Domain.Events;
using BookingService.Infrastructure.Persistence;

namespace BookingService.Infrastructure.Tests;

public class InMemoryEventCatalogTests
{
    private readonly InMemoryEventCatalog _catalog = new();

    [Fact]
    public async Task Known_event_exists()
    {
        Assert.True(await _catalog.ExistsAsync(
            InMemoryEventCatalog.DefaultEvent.Id,
            CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_event_does_not_exist()
    {
        Assert.False(await _catalog.ExistsAsync(
            Guid.NewGuid(),
            CancellationToken.None));
    }
}
