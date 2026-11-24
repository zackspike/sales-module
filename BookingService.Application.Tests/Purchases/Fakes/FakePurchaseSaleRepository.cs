using BookingService.Application.Abstractions;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tests.Purchases.Fakes;

/// <summary>
/// In-memory <see cref="IPurchaseSaleRepository"/> that honours the transaction contract: the sale is kept only
/// if <c>capture</c> succeeds; otherwise the seats go back to available and the outbox rolls back.
/// </summary>
public sealed class FakePurchaseSaleRepository(FakeTicketSoldOutbox outbox, params EventSeat[] seats)
    : IPurchaseSaleRepository
{
    private readonly List<EventSeat> _seats = seats.ToList();
    private readonly Dictionary<Guid, (Guid PurchaseId, IReadOnlyList<EventSeatSale> Sales)> _salesByKey = [];

    public int SellCalls { get; private set; }

    public int CaptureCallbacks { get; private set; }

    public Task<PurchaseSaleRecord?> FindByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(_salesByKey.TryGetValue(idempotencyKey, out var used)
            ? new PurchaseSaleRecord(used.PurchaseId, used.Sales)
            : null);

    public async Task<PurchaseSaleOutcome> SellOnceAsync(
        Guid idempotencyKey,
        PurchaseSession purchase,
        Func<EventSeat, Ticket> sell,
        Func<IReadOnlyList<EventSeatSale>, CancellationToken, Task<PaymentCaptureResult>> capture,
        CancellationToken cancellationToken)
    {
        SellCalls++;

        if (_salesByKey.TryGetValue(idempotencyKey, out var used))
        {
            return used.PurchaseId == purchase.Id
                ? PurchaseSaleOutcome.Replayed(used.Sales)
                : PurchaseSaleOutcome.IdempotencyKeyConflict();
        }

        var heldSeats = new List<EventSeat>();
        var failedSeats = new List<FailedEventSeat>();
        foreach (var eventSeatId in purchase.HeldEventSeatIds)
        {
            var seat = _seats.FirstOrDefault(candidate =>
                candidate.EventId == purchase.EventId && candidate.Id == eventSeatId);
            if (seat is null)
            {
                failedSeats.Add(new FailedEventSeat(eventSeatId, SeatSaleFailure.NotFound));
            }
            else if (seat.Status == EventSeatStatus.Sold)
            {
                failedSeats.Add(new FailedEventSeat(eventSeatId, SeatSaleFailure.AlreadySold));
            }
            else
            {
                heldSeats.Add(seat);
            }
        }

        if (failedSeats.Count > 0)
        {
            return PurchaseSaleOutcome.SeatsUnavailable(failedSeats);
        }

        var sales = heldSeats.Select(seat => new EventSeatSale(seat, sell(seat))).ToList();

        CaptureCallbacks++;
        PaymentCaptureResult captured;
        try
        {
            captured = await capture(sales, cancellationToken);
        }
        catch
        {
            Rollback(heldSeats);
            throw;
        }

        if (captured.Status != PaymentCallStatus.Succeeded)
        {
            Rollback(heldSeats);
            return PurchaseSaleOutcome.NotCaptured(captured.Status);
        }

        outbox.Commit();
        _salesByKey[idempotencyKey] = (purchase.Id, sales);
        return PurchaseSaleOutcome.Sold(sales);
    }

    private void Rollback(IEnumerable<EventSeat> soldSeats)
    {
        outbox.Rollback();

        // A rolled-back transaction leaves the seats as they were stored: available.
        foreach (var seat in soldSeats)
        {
            typeof(EventSeat).GetProperty(nameof(EventSeat.Status))!.SetValue(seat, EventSeatStatus.Available);
        }
    }
}
