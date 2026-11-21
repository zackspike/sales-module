using BookingService.Application.Abstractions;
using BookingService.Domain.Purchases;

namespace BookingService.Application.Tests.Purchases.Fakes;

public sealed class FakePurchaseSessionStore(params Purchase[] sessions) : IPurchaseSessionStore
{
    private readonly Dictionary<Guid, Purchase> _sessions = sessions.ToDictionary(session => session.Id);

    public int SaveCalls { get; private set; }

    public IReadOnlyDictionary<Guid, Purchase> Sessions => _sessions;

    public Task SaveAsync(Purchase purchase, CancellationToken cancellationToken)
    {
        SaveCalls++;
        _sessions[purchase.Id] = purchase;
        return Task.CompletedTask;
    }

    public Task<Purchase?> GetAsync(Guid purchaseId, CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.GetValueOrDefault(purchaseId));
}
