namespace BookingService.Domain;

public static class TicketCodeGenerator
{
    public static string Generate() => $"TK-{Guid.NewGuid():N}";
}
