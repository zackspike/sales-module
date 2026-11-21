using BookingService.Application.Purchases;
using BookingService.Application.Purchases.Commands;
using BookingService.Application.Tests.Purchases.Fakes;

namespace BookingService.Application.Tests.Purchases.Commands;

public class StartPurchaseHandlerTests
{
    private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(7);

    [Fact]
    public async Task Session_is_opened_for_the_configured_duration_and_stored()
    {
        var sessions = new FakePurchaseSessionStore();

        var before = DateTime.UtcNow;
        var result = await Handler(sessions).HandleAsync(Command(), CancellationToken.None);
        var after = DateTime.UtcNow;

        Assert.Equal(StartPurchaseStatus.Started, result.Status);
        Assert.NotEqual(Guid.Empty, result.Purchase!.PurchaseId);
        Assert.InRange(result.Purchase.ExpiresAtUtc, before + SessionDuration, after + SessionDuration);

        var stored = Assert.Single(sessions.Sessions).Value;
        Assert.Equal(result.Purchase.PurchaseId, stored.Id);
        Assert.Equal("Juan Perez", stored.Name);
        Assert.Equal("juan.perez@example.com", stored.Email);
        Assert.Empty(stored.HeldEventSeatIds);
    }

    [Fact]
    public async Task Name_and_email_are_trimmed_before_they_reach_the_session()
    {
        var sessions = new FakePurchaseSessionStore();

        await Handler(sessions).HandleAsync(
            Command("  Juan Perez  ", "  juan.perez@example.com  "),
            CancellationToken.None);

        var stored = Assert.Single(sessions.Sessions).Value;
        Assert.Equal("Juan Perez", stored.Name);
        Assert.Equal("juan.perez@example.com", stored.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_name_is_rejected_without_opening_a_session(string name)
    {
        var sessions = new FakePurchaseSessionStore();

        var result = await Handler(sessions).HandleAsync(Command(name), CancellationToken.None);

        Assert.Equal(StartPurchaseStatus.Invalid, result.Status);
        Assert.Equal("name", Assert.Single(result.Errors.Keys));
        Assert.Empty(sessions.Sessions);
    }

    [Fact]
    public async Task Name_longer_than_the_limit_is_rejected_as_name()
    {
        var result = await Handler(new FakePurchaseSessionStore())
            .HandleAsync(Command(new string('a', 65)), CancellationToken.None);

        Assert.Equal(StartPurchaseStatus.Invalid, result.Status);
        Assert.Equal("name", Assert.Single(result.Errors.Keys));
    }

    [Fact]
    public async Task Name_at_the_limit_is_accepted()
    {
        var result = await Handler(new FakePurchaseSessionStore())
            .HandleAsync(Command(new string('a', 64)), CancellationToken.None);

        Assert.Equal(StartPurchaseStatus.Started, result.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("jane@localhost")]
    public async Task Invalid_email_is_rejected_without_opening_a_session(string email)
    {
        var sessions = new FakePurchaseSessionStore();

        var result = await Handler(sessions).HandleAsync(Command(email: email), CancellationToken.None);

        Assert.Equal(StartPurchaseStatus.Invalid, result.Status);
        Assert.Equal("email", Assert.Single(result.Errors.Keys));
        Assert.Empty(sessions.Sessions);
    }

    [Fact]
    public async Task Both_fields_are_reported_together()
    {
        var result = await Handler(new FakePurchaseSessionStore())
            .HandleAsync(Command("", ""), CancellationToken.None);

        Assert.Equal(StartPurchaseStatus.Invalid, result.Status);
        Assert.Equal(["email", "name"], result.Errors.Keys.Order());
    }

    private static StartPurchaseCommand Command(
        string name = "Juan Perez",
        string email = "juan.perez@example.com") => new(name, email);

    private static StartPurchaseHandler Handler(FakePurchaseSessionStore sessions) =>
        new(sessions, new PurchaseSessionOptions { SessionDuration = SessionDuration });
}
