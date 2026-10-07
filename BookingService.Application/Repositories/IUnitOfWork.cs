namespace BookingService.Application.Repositories;

/// <summary>
/// Commits the changes tracked by the repositories as one atomic unit. A use case that touches
/// several repositories (e.g. buying a ticket creates the user and sells the seat) runs inside
/// <see cref="ExecuteInTransactionAsync{TResult}"/> so either everything is persisted or nothing is.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="operation"/> inside a database transaction. The transaction is committed
    /// when the operation returns and rolled back (discarding pending changes) when it throws.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
