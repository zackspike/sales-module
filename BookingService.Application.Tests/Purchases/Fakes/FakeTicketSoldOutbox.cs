using BookingService.Application.Abstractions;

namespace BookingService.Application.Tests.Purchases.Fakes;

/// <summary>
/// Outbox that joins the transaction of <see cref="FakePurchaseSaleRepository"/>: added messages stay pending
/// until the repository commits or rolls back.
/// </summary>
public sealed class FakeTicketSoldOutbox : ITicketSoldOutbox
{
    private readonly List<TicketSold> _pending = [];
    private readonly List<TicketSold> _committed = [];

    public IReadOnlyList<TicketSold> Committed => _committed;

    public Task AddAsync(IReadOnlyList<TicketSold> messages, CancellationToken cancellationToken)
    {
        _pending.AddRange(messages);
        return Task.CompletedTask;
    }

    public void Commit()
    {
        _committed.AddRange(_pending);
        _pending.Clear();
    }

    public void Rollback() => _pending.Clear();
}
