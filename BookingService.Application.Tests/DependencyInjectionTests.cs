using BookingService.Application.Purchases;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Application.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void Invalid_payment_options_fail_at_startup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddApplication(payment: new PaymentOptions { CaptureTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddApplication(payment: new PaymentOptions { CaptureAttempts = 0 }));
    }
}
