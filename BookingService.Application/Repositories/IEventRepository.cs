using BookingService.Domain;

namespace BookingService.Application.Repositories;

/// <summary>
/// Read access to the events persisted in the database (SP-03 / APP-03).
/// </summary>
public interface IEventRepository
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<Event?> GetByIdAsync(Guid eventId, CancellationToken cancellationToken = default);

    void Add(Event @event);
}
