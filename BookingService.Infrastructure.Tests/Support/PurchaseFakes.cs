using System.Reflection;
using BookingService.Application.Abstractions;
using BookingService.Domain.Purchases;

namespace BookingService.Infrastructure.Tests.Support;

/// <summary>
/// Session store that, like Redis, hands out a fresh copy on every read: concurrent handlers never share one
/// <see cref="PurchaseSession"/> instance.
/// </summary>
public sealed class CopyingPurchaseSessionStore : IPurchaseSessionStore
{
    private readonly Dictionary<Guid, PurchaseSession> _sessions = [];
    private readonly Lock _gate = new();

    public Task SaveAsync(PurchaseSession purchase, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _sessions[purchase.Id] = Copy(purchase);
        }

        return Task.CompletedTask;
    }

    public Task<PurchaseSession?> GetAsync(Guid purchaseId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_sessions.TryGetValue(purchaseId, out var session) ? Copy(session) : null);
        }
    }

    private static PurchaseSession Copy(PurchaseSession source)
    {
        var nowUtc = DateTime.UtcNow;
        var copy = PurchaseSession.Start(source.EventId, source.Name, source.Email, nowUtc, TimeSpan.FromMinutes(1));
        Set(copy, nameof(PurchaseSession.Id), source.Id);
        foreach (var seat in source.HeldEventSeatIds)
        {
            copy.Hold(source.EventId, seat, int.MaxValue, nowUtc);
        }

        Set(copy, nameof(PurchaseSession.ExpiresAtUtc), source.ExpiresAtUtc);
        if (source.PaymentToken is { } token)
        {
            copy.SetPaymentToken(token);
        }

        return copy;
    }

    private static void Set(PurchaseSession session, string property, object value) =>
        typeof(PurchaseSession).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(session, value);
}

/// <summary>Payment double whose capture answer is set by the test; <c>/init</c> is not used by "Pagar".</summary>
public sealed class StubPaymentGateway : IPaymentGateway
{
    private int _captures;

    public PaymentCallStatus CaptureStatus { get; set; } = PaymentCallStatus.Succeeded;

    public TimeSpan CaptureDelay { get; init; } = TimeSpan.Zero;

    public int Captures => Volatile.Read(ref _captures);

    public Task<PaymentInitResult> InitAsync(
        Guid purchaseId,
        DateTime expiresAtUtc,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("\"Pagar\" never calls /init.");

    public async Task<PaymentCaptureResult> CaptureAsync(string token, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _captures);
        if (CaptureDelay > TimeSpan.Zero)
        {
            await Task.Delay(CaptureDelay, cancellationToken);
        }

        return new PaymentCaptureResult(CaptureStatus);
    }
}
