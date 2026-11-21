using BookingService.Application.Abstractions;

namespace BookingService.Application.Tests.Purchases.Fakes;

public sealed class FakePaymentGateway : IPaymentGateway
{
    public PaymentInitResult InitResult { get; set; } = new(PaymentCallStatus.Succeeded, "pay_tok_1");

    public PaymentCaptureResult CaptureResult { get; set; } = new(PaymentCallStatus.Succeeded);

    public int InitCalls { get; private set; }

    public int CaptureCalls { get; private set; }

    public Guid? LastInitPurchaseId { get; private set; }

    public DateTime? LastInitExpiresAtUtc { get; private set; }

    public IReadOnlyList<Guid> LastInitEventSeatIds { get; private set; } = [];

    public string? LastCapturedToken { get; private set; }

    public Task<PaymentInitResult> InitAsync(
        Guid purchaseId,
        DateTime expiresAtUtc,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken)
    {
        InitCalls++;
        LastInitPurchaseId = purchaseId;
        LastInitExpiresAtUtc = expiresAtUtc;
        LastInitEventSeatIds = eventSeatIds.ToArray();
        return Task.FromResult(InitResult);
    }

    public Task<PaymentCaptureResult> CaptureAsync(string token, CancellationToken cancellationToken)
    {
        CaptureCalls++;
        LastCapturedToken = token;
        return Task.FromResult(CaptureResult);
    }
}
