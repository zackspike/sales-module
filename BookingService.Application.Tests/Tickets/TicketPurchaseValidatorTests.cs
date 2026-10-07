using BookingService.Application.Tests.TestDoubles;
using BookingService.Application.Tickets;

namespace BookingService.Application.Tests.Tickets;

public class TicketPurchaseValidatorTests
{
    private static readonly Guid KnownEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly TicketPurchaseValidator _validator =
        new(new FakeEventRepository(KnownEventId));

    [Fact]
    public async Task Valid_request_for_existing_event_is_valid()
    {
        var result = await _validator.ValidateAsync(Command(KnownEventId, "Juan Perez", "juan.perez@example.com"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_full_name_is_invalid(string? fullName)
    {
        var result = await _validator.ValidateAsync(Command(KnownEventId, fullName, "juan.perez@example.com"));

        Assert.Equal(TicketPurchaseValidationStatus.Invalid, result.Status);
        Assert.Equal(["fullName"], result.Errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_email_is_invalid(string? email)
    {
        var result = await _validator.ValidateAsync(Command(KnownEventId, "Juan Perez", email));

        Assert.Equal(TicketPurchaseValidationStatus.Invalid, result.Status);
        Assert.Equal(["email"], result.Errors.Keys);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("a@")]
    [InlineData("@b.com")]
    [InlineData("a b@c.com")]
    [InlineData("Jane <jane@example.com>")]
    [InlineData("a@b.com, c@d.com")]
    public async Task Malformed_email_is_invalid(string email)
    {
        var result = await _validator.ValidateAsync(Command(KnownEventId, "Juan Perez", email));

        Assert.Equal(TicketPurchaseValidationStatus.Invalid, result.Status);
        Assert.Equal(["email"], result.Errors.Keys);
    }

    [Fact]
    public async Task Email_longer_than_254_characters_is_invalid()
    {
        var email = new string('a', 250) + "@b.co";

        var result = await _validator.ValidateAsync(Command(KnownEventId, "Juan Perez", email));

        Assert.Equal(TicketPurchaseValidationStatus.Invalid, result.Status);
        Assert.Contains("email", result.Errors.Keys);
    }

    [Fact]
    public async Task Email_with_surrounding_whitespace_is_accepted()
    {
        var result = await _validator.ValidateAsync(Command(KnownEventId, "Juan Perez", " juan.perez@example.com "));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task All_failing_fields_are_reported_together()
    {
        var result = await _validator.ValidateAsync(Command(KnownEventId, null, "not-an-email"));

        Assert.Equal(TicketPurchaseValidationStatus.Invalid, result.Status);
        Assert.Equal(["email", "fullName"], result.Errors.Keys.Order());
    }

    [Fact]
    public async Task Unknown_event_with_valid_body_is_not_found()
    {
        var result = await _validator.ValidateAsync(Command(UnknownEventId, "Juan Perez", "juan.perez@example.com"));

        Assert.Equal(TicketPurchaseValidationStatus.EventNotFound, result.Status);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Invalid_body_wins_over_unknown_event()
    {
        var result = await _validator.ValidateAsync(Command(UnknownEventId, "", "juan.perez@example.com"));

        Assert.Equal(TicketPurchaseValidationStatus.Invalid, result.Status);
    }

    private static PurchaseTicketCommand Command(Guid eventId, string? fullName, string? email) =>
        new(eventId, Guid.NewGuid(), fullName, email, Guid.NewGuid());
}
