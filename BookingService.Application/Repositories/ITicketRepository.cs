using BookingService.Domain;

namespace BookingService.Application.Repositories;

/// <summary>
/// Access to the ticket inventory. Every returned ticket has its <see cref="Ticket.Seat"/>
/// (with <see cref="Seat.Zone"/>) and, when sold, its <see cref="Ticket.User"/> loaded.
/// Changes are persisted by <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface ITicketRepository
{
    /// <summary>
    /// Adds the ticket inventory (seats) of event <paramref name="eventId"/> (SP-03 / INF-01).
    /// Every ticket must have a seat whose zone belongs to that event; otherwise nothing is
    /// added and an <see cref="ArgumentException"/> is thrown.
    /// </summary>
    void AddRange(Guid eventId, IEnumerable<Ticket> tickets);

    /// <summary>
    /// Returns the tickets of event <paramref name="eventId"/> that are still available; empty if none.
    /// </summary>
    Task<IReadOnlyList<Ticket>> GetAvailableByEventAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns ticket <paramref name="ticketId"/> only if it belongs to event <paramref name="eventId"/>.
    /// </summary>
    Task<Ticket?> GetByIdAsync(Guid eventId, Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="GetByIdAsync"/> but locks the ticket row until the current transaction
    /// ends, so two concurrent buyers of the same seat are serialized. Must be called inside
    /// <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>.
    /// </summary>
    Task<Ticket?> GetForPurchaseAsync(Guid eventId, Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the ticket bought with <paramref name="idempotencyKey"/>, or null if the key was never used.
    /// </summary>
    Task<Ticket?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken = default);
}
