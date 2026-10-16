using BookingService.Application.EventSeats.Commands;

namespace BookingService.Application.Tests.EventSeats.Commands;

public class TicketHolderValidatorTests
{
    [Fact]
    public void Valid_data_returns_no_errors()
    {
        var errors = TicketHolderValidator.Validate(" Jane Doe ", " jane@example.com ");

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_full_name_returns_fullName_error(string? fullName)
    {
        var errors = TicketHolderValidator.Validate(fullName!, "jane@example.com");

        Assert.Contains("fullName", errors.Keys);
    }

    [Fact]
    public void Full_name_at_limit_is_valid()
    {
        Assert.Empty(TicketHolderValidator.Validate(new string('a', 64), "jane@example.com"));
    }

    [Fact]
    public void Full_name_length_is_measured_after_trim()
    {
        Assert.Empty(TicketHolderValidator.Validate($"  {new string('a', 64)}  ", "jane@example.com"));
    }

    [Fact]
    public void Full_name_over_limit_is_invalid()
    {
        Assert.Contains("fullName", TicketHolderValidator.Validate(new string('a', 65), "jane@example.com").Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    [InlineData("@b.com")]
    [InlineData("Jane <jane@example.com>")]
    public void Invalid_email_returns_email_error(string? email)
    {
        var errors = TicketHolderValidator.Validate("Jane Doe", email!);

        Assert.Contains("email", errors.Keys);
    }

    [Fact]
    public void Email_at_254_characters_is_valid_after_trim()
    {
        var email = MaxLengthEmail();
        Assert.Equal(254, email.Length);
        Assert.Empty(TicketHolderValidator.Validate("Jane Doe", $"  {email}  "));
    }

    [Fact]
    public void Email_over_254_characters_is_invalid()
    {
        Assert.Contains("email", TicketHolderValidator.Validate("Jane Doe", $"a{MaxLengthEmail()}").Keys);
    }

    [Fact]
    public void Both_invalid_fields_are_reported()
    {
        var errors = TicketHolderValidator.Validate(" ", "not-an-email");

        Assert.Contains("fullName", errors.Keys);
        Assert.Contains("email", errors.Keys);
    }

    private static string MaxLengthEmail()
    {
        var localPart = new string('a', 64);
        var domain = $"{new string('b', 63)}.{new string('b', 63)}.{new string('b', 57)}.com";
        return $"{localPart}@{domain}";
    }
}
