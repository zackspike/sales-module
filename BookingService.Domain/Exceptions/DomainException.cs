namespace BookingService.Domain.Exceptions;

/// <summary>
/// Base type for violations of a business rule. <see cref="Code"/> is a stable,
/// machine-readable identifier (e.g. <c>TICKET_ALREADY_SOLD</c>).
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
