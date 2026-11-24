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
    /// The event the session was opened for. A purchase only holds seats of this event; to buy for another
    /// event the fan opens a new session.
    /// </summary>
    public Guid EventId { get; private set; }

    public IReadOnlyList<Guid> HeldEventSeatIds => _heldEventSeatIds;

    /// <summary>
    /// Token returned by Payment's <c>/init</c>. Cleared whenever the held seats change, so a new one is
    /// requested for the new selection (ADR-005).
    /// </summary>
    public string? PaymentToken { get; private set; }

    private Purchase()
    {
    }

    public static Purchase Start(Guid eventId, string name, string email, DateTime nowUtc, TimeSpan sessionDuration)
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
            EventId = eventId,
            Name = name,
            Email = email,
            ExpiresAtUtc = nowUtc + sessionDuration
        };
    }

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>
    /// Holds a seat of <paramref name="eventId"/>. Returns true if the purchase did not hold it yet: the
    /// caller must then lock it until <see cref="ExpiresAtUtc"/>, and <see cref="Release"/> it if the lock
    /// fails (another purchase holds it). Returns false if the purchase already holds it.
    /// </summary>
    /// <exception cref="PurchaseExpiredException">The session has expired.</exception>
    /// <exception cref="ArgumentException">The seat belongs to another event than the purchase.</exception>
    /// <exception cref="SeatLimitReachedException">The purchase already holds <paramref name="maxSeats"/> seats.</exception>
    public bool Hold(Guid eventId, Guid eventSeatId, int maxSeats, DateTime nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSeats);

        if (IsExpired(nowUtc))
        {
            throw new PurchaseExpiredException(Id);
        }

        if (eventId != EventId)
        {
            throw new ArgumentException("A purchase can only hold seats of a single event.", nameof(eventId));
        }

        if (_heldEventSeatIds.Contains(eventSeatId))
        {
            return false;
        }

        if (_heldEventSeatIds.Count >= maxSeats)
        {
            throw new SeatLimitReachedException(maxSeats);
        }

        _heldEventSeatIds.Add(eventSeatId);
        PaymentToken = null;

        return true;
    }

    /// <summary>
    /// Stops holding the seat. Does nothing if the purchase does not hold it.
    /// </summary>
    public void Release(Guid eventSeatId)
    {
        if (_heldEventSeatIds.Remove(eventSeatId))
        {
            PaymentToken = null;
        }
    }

    public void SetPaymentToken(string paymentToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentToken);
        PaymentToken = paymentToken;
    }
}
