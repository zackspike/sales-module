namespace BookingService.Domain.Exceptions;

/// <summary>
/// Base class for domain exceptions in the booking domain. All domain-specific exceptions should inherit from this class.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
