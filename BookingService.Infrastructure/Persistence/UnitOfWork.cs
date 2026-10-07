using System.Data;
using BookingService.Application.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingService.Infrastructure.Persistence;

/// <summary>
/// Transaction boundary shared by the repositories of a request (they all use the same scoped
/// <see cref="BookingDbContext"/>). Uses READ COMMITTED plus explicit row locks
/// (<c>SELECT ... FOR UPDATE</c>) and unique constraints to keep concurrent purchases consistent.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly BookingDbContext _context;

    public UnitOfWork(BookingDbContext context)
    {
        _context = context;
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        TResult result;
        try
        {
            result = await operation(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);

            // The rolled back changes must not leak into a later SaveChanges of this context.
            _context.ChangeTracker.Clear();
            throw;
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgresException)
        {
            throw new UniqueConstraintViolationException(postgresException.ConstraintName, exception);
        }
    }
}
