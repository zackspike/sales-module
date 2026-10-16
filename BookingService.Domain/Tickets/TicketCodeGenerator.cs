namespace BookingService.Domain.Tickets;

internal static class TicketCodeGenerator
{
    public static string GenerateTicketCode() => $"TK-{Guid.NewGuid():N}";
}
