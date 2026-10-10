namespace BookingService.Domain.Tickets;

public static class TicketCodeGenerator
{
    public static string GenerateTicketCode() => $"TK-{Guid.NewGuid():N}";
}
