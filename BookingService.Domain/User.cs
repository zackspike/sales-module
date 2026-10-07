namespace BookingService.Domain;

/// <summary>
/// Fan who buys tickets, identified by email (User 1-N Ticket).
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
