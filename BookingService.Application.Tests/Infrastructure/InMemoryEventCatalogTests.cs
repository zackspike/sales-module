using BookingService.Domain.Events;
using BookingService.Infrastructure.Persistence;

namespace BookingService.Application.Tests.Infrastructure;

public class InMemoryEventCatalogTests
{
    private readonly InMemoryEventCatalog _catalog = new();

    [Fact]
    public void Default_event_is_pre_seeded()
    {
        var defaultId = InMemoryEventCatalog.DefaultEvent.Id;

        Assert.True(_catalog.Exists(defaultId));
        var defaultEvent = _catalog.GetById(defaultId);
        Assert.NotNull(defaultEvent);
        Assert.Equal("Rock Fest 2026", defaultEvent.Name);
        Assert.Equal(50, defaultEvent.TotalSeats);
    }

    [Fact]
    public void Unknown_event_does_not_exist()
    {
        Assert.False(_catalog.Exists(Guid.NewGuid()));
        Assert.Null(_catalog.GetById(Guid.NewGuid()));
    }

    [Fact]
    public void Adding_event_makes_it_available()
    {
        var customId = Guid.NewGuid();
        var customEvent = new Event
        {
            Id = customId,
            Name = "Jazz Night",
            Artist = "The Smooth",
            VenueId = Guid.NewGuid(),
            VenueName = "Teatro Principal",
            Date = DateTime.UtcNow.AddDays(10),
            TotalSeats = 100
        };

        _catalog.Add(customEvent);

        Assert.True(_catalog.Exists(customId));
        Assert.Same(customEvent, _catalog.GetById(customId));
    }
}
