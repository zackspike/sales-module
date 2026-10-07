namespace BookingService.Application.Repositories;

/// <summary>
/// Raised by <see cref="IUnitOfWork.SaveChangesAsync"/> when a concurrent transaction already
/// stored a row with the same unique value (e.g. the same idempotency key or email).
/// Lets Application react to the race without depending on the persistence technology.
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(string? constraintName, Exception innerException)
        : base($"Unique constraint '{constraintName}' was violated.", innerException)
    {
        ConstraintName = constraintName;
    }

    public string? ConstraintName { get; }
}
