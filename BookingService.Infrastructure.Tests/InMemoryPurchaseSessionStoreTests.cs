using BookingService.Domain.Purchases;
using BookingService.Infrastructure.Caching;

namespace BookingService.Infrastructure.Tests;

public class InMemoryPurchaseSessionStoreTests
{
    private static PurchaseSession NewSession(TimeSpan duration) =>
        PurchaseSession.Start(Guid.NewGuid(), "Jane Doe", "jane.doe@example.com", DateTime.UtcNow, duration);

    private static async Task<bool> TrySaveAsync(InMemoryPurchaseSessionStore store, PurchaseSession purchase)
    {
        try
        {
            await store.SaveAsync(purchase, CancellationToken.None);
            return true;
        }
        catch (PurchaseSessionConcurrencyException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Session_round_trips_and_expires()
    {
        var store = new InMemoryPurchaseSessionStore();
        var purchase = NewSession(TimeSpan.FromSeconds(3));
        var seat = Guid.NewGuid();
        purchase.Hold(purchase.EventId, seat, 3, DateTime.UtcNow);
        purchase.SetPaymentToken("pay_tok_test");

        await store.SaveAsync(purchase, CancellationToken.None);

        var loaded = await store.GetAsync(purchase.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(purchase.Id, loaded.Id);
        Assert.Equal(purchase.EventId, loaded.EventId);
        Assert.Equal(purchase.Name, loaded.Name);
        Assert.Equal(purchase.Email, loaded.Email);
        Assert.Equal(purchase.ExpiresAtUtc, loaded.ExpiresAtUtc);
        Assert.Equal([seat], loaded.HeldEventSeatIds);
        Assert.Equal("pay_tok_test", loaded.PaymentToken);

        await Task.Delay(TimeSpan.FromSeconds(3.5));
        Assert.Null(await store.GetAsync(purchase.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_writes_to_one_session_conflict()
    {
        var store = new InMemoryPurchaseSessionStore();
        var purchase = NewSession(TimeSpan.FromMinutes(1));

        await store.SaveAsync(purchase, CancellationToken.None);

        var first = await store.GetAsync(purchase.Id, CancellationToken.None);
        var second = await store.GetAsync(purchase.Id, CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(second);

        first.Hold(first.EventId, Guid.NewGuid(), 3, DateTime.UtcNow);
        second.Hold(second.EventId, Guid.NewGuid(), 3, DateTime.UtcNow);

        var results = await Task.WhenAll(
            TrySaveAsync(store, first),
            TrySaveAsync(store, second));

        Assert.Single(results, saved => saved);
        Assert.Single(results, saved => !saved);

        var stored = await store.GetAsync(purchase.Id, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Single(stored.HeldEventSeatIds);
    }
}
