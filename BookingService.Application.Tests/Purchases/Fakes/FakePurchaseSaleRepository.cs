using BookingService.Application.Abstractions;
using BookingService.Domain.EventSeats;
using BookingService.Domain.Purchases;
using BookingService.Domain.Tickets;

namespace BookingService.Application.Tests.Purchases.Fakes;

/// <summary>
/// In-memory <see cref="IPurchaseSaleRepository"/> that honours the transaction contract: the sale is kept only
/// if <c>confirm</c> returns true; otherwise the seats go back to available and the outbox rolls back.
/// </summary>
public sealed class FakePurchaseSaleRepository(FakeTicketSoldOutbox outbox, params EventSeat[] seats)
    : IPurchaseSaleRepository
{
    private readonly List<EventSeat> _seats = seats.ToList();
    private readonly Dictionary<Guid, (Guid PurchaseId, IReadOnlyList<EventSeatSale> Sales)> _salesByKey = [];

    public int SellCalls { get; private set; }

    public int ConfirmCalls { get; private set; }

    public async Task<PurchaseSaleOutcome> SellOnceAsync(
        Guid idempotencyKey,
        PurchaseSession purchase,
        Func<EventSeat, Ticket> sell,
        Func<IReadOnlyList<EventSeatSale>, CancellationToken, Task<bool>> confirm,
        CancellationToken cancellationToken)
    {
        SellCalls++;

        if (_salesByKey.TryGetValue(idempotencyKey, out var used))
        {
            return used.PurchaseId == purchase.Id
                ? new PurchaseSaleOutcome(PurchaseSaleStatus.Replayed, used.Sales)
                : Nothing(PurchaseSaleStatus.IdempotencyKeyConflict);
        }

        var heldSeats = new List<EventSeat>();
        foreach (var eventSeatId in purchase.HeldEventSeatIds)
        {
            var seat = _seats.FirstOrDefault(candidate =>
                candidate.EventId == purchase.EventId && candidate.Id == eventSeatId);
            if (seat is null)
            {
                return Nothing(PurchaseSaleStatus.EventSeatNotFound);
            }

            if (seat.Status == EventSeatStatus.Sold)
            {
                return Nothing(PurchaseSaleStatus.AlreadySold);
            }

            heldSeats.Add(seat);
        }

        var sales = heldSeats.Select(seat => new EventSeatSale(seat, sell(seat))).ToList();

        ConfirmCalls++;
        bool confirmed;
        try
        {
            confirmed = await confirm(sales, cancellationToken);
        }
        catch
        {
            Rollback(heldSeats);
            throw;
        }

        if (!confirmed)
        {
            Rollback(heldSeats);
            return Nothing(PurchaseSaleStatus.NotConfirmed);
        }

        outbox.Commit();
        _salesByKey[idempotencyKey] = (purchase.Id, sales);
        return new PurchaseSaleOutcome(PurchaseSaleStatus.Sold, sales);
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

    private static PurchaseSaleOutcome Nothing(PurchaseSaleStatus status) => new(status, []);
}
