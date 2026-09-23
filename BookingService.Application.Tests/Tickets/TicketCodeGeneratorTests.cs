using BookingService.Domain;

namespace BookingService.Application.Tests.Tickets;

public class TicketCodeGeneratorTests
{
    [Fact]
    public void Generated_code_has_valid_ticket_format()
    {
        var code = TicketCodeGenerator.Generate();

        Assert.StartsWith("TK-", code);
        Assert.True(Guid.TryParseExact(code[3..], "N", out _));
    }

    [Fact]
    public void Many_generated_codes_are_unique()
    {
        var codes = Enumerable.Range(0, 10_000)
            .Select(_ => TicketCodeGenerator.Generate())
            .ToArray();

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    [Fact]
    public void Concurrent_generation_produces_unique_codes()
    {
        var codes = new string[10_000];

        Parallel.For(0, codes.Length, index =>
        {
            codes[index] = TicketCodeGenerator.Generate();
        });

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }
}