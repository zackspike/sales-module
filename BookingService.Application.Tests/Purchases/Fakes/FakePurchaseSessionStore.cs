using BookingService.Application.Abstractions;
using BookingService.Domain.Purchases;

namespace BookingService.Application.Tests.Purchases.Fakes;

public sealed class FakePurchaseSessionStore(params PurchaseSession[] sessions) : IPurchaseSessionStore
{
    private readonly Dictionary<Guid, PurchaseSession> _sessions = sessions.ToDictionary(session => session.Id);

    public int SaveCalls { get; private set; }

    public IReadOnlyDictionary<Guid, PurchaseSession> Sessions => _sessions;

    public Task SaveAsync(PurchaseSession purchase, CancellationToken cancellationToken)
    {
        SaveCalls++;
        _sessions[purchase.Id] = purchase;
        return Task.CompletedTask;
    }

    public Task<PurchaseSession?> GetAsync(Guid purchaseId, CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.GetValueOrDefault(purchaseId));
}
