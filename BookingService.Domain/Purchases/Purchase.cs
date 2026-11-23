using BookingService.Domain.Tickets;

namespace BookingService.Domain.Purchases;

/// <summary>
/// A fan's purchase session: who buys, until when, which seats of a single event they hold and
/// the payment token. Held seats expire with the session (ADR-001): holding again never renews it.
/// </summary>
public sealed class Purchase
{
    private readonly List<Guid> _heldEventSeatIds = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>
    /// Event of the first held seat. A purchase belongs to a single event; null until a seat is held.
    /// </summary>
    public Guid? EventId { get; private set; }

    public IReadOnlyList<Guid> HeldEventSeatIds => _heldEventSeatIds;

    /// <summary>
    /// Token returned by Payment's <c>/init</c>. Cleared whenever the held seats change, so a new one is
    /// requested for the new selection (ADR-005).
    /// </summary>
    public string? PaymentToken { get; private set; }

    private Purchase()
    {
    }

    public static Purchase Start(string name, string email, DateTime nowUtc, TimeSpan sessionDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sessionDuration, TimeSpan.Zero);

        // Name and email go on the ticket as they are, so they must fit its limits.
        if (name.Length > Ticket.MaxHolderFullNameLength)
        {
            throw new ArgumentException($"Name cannot exceed {Ticket.MaxHolderFullNameLength} characters.", nameof(name));
        }

        if (email.Length > Ticket.MaxHolderEmailLength)
        {
            throw new ArgumentException($"Email cannot exceed {Ticket.MaxHolderEmailLength} characters.", nameof(email));
        }

        return new Purchase
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            ExpiresAtUtc = nowUtc + sessionDuration
        };
    }

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>
    /// Adds the given seats of <paramref name="eventId"/> to the purchase and returns the ones it did not
    /// hold yet, which the caller must lock until <see cref="ExpiresAtUtc"/>. Seats already held are
    /// ignored. If a rule fails nothing is held. A returned seat that cannot be locked (another purchase
    /// holds it) must be dropped with <see cref="Release"/>; the others stay held (partial hold).
    /// </summary>
    /// <exception cref="PurchaseExpiredException">The session has expired.</exception>
    /// <exception cref="ArgumentException">The seats belong to another event than the held ones.</exception>
    /// <exception cref="SeatLimitReachedException">Held plus new seats would exceed <paramref name="maxSeats"/>.</exception>
    public IReadOnlyList<Guid> Hold(Guid eventId, IEnumerable<Guid> eventSeatIds, int maxSeats, DateTime nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSeats);

        if (IsExpired(nowUtc))
        {
            throw new PurchaseExpiredException(Id);
        }

        if (EventId is { } heldEventId && heldEventId != eventId)
        {
            throw new ArgumentException("A purchase can only hold seats of a single event.", nameof(eventId));
        }

        var newSeatIds = eventSeatIds.Distinct().Except(_heldEventSeatIds).ToList();

        if (_heldEventSeatIds.Count + newSeatIds.Count > maxSeats)
        {
            throw new SeatLimitReachedException(maxSeats);
        }

        if (newSeatIds.Count > 0)
        {
            EventId ??= eventId;
            _heldEventSeatIds.AddRange(newSeatIds);
            PaymentToken = null;
        }

        return newSeatIds;
    }

    /// <summary>
    /// Stops holding the seat. Does nothing if the purchase does not hold it. Releasing the last seat
    /// frees the purchase from its event, so it can hold seats of another one.
    /// </summary>
    public void Release(Guid eventSeatId)
    {
        if (_heldEventSeatIds.Remove(eventSeatId))
        {
            PaymentToken = null;

            if (_heldEventSeatIds.Count == 0)
            {
                EventId = null;
            }
        }
    }

    public void SetPaymentToken(string paymentToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentToken);
        PaymentToken = paymentToken;
    }
}
