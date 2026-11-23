using BookingService.Domain.Purchases;
using BookingService.Domain.Tickets;

namespace BookingService.Domain.Tests.Purchases;

public class PurchaseTests
{
    private const int MaxSeats = 3;
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherEventId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTime Now = new(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(10);

    private static Purchase Start() => Purchase.Start("Juan Perez", "juan.perez@example.com", Now, SessionDuration);

    private static Guid[] Seats(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

    [Fact]
    public void Start_opens_a_session_without_seats_that_expires_after_its_duration()
    {
        var purchase = Start();

        Assert.NotEqual(Guid.Empty, purchase.Id);
        Assert.Equal("Juan Perez", purchase.Name);
        Assert.Equal("juan.perez@example.com", purchase.Email);
        Assert.Equal(Now + SessionDuration, purchase.ExpiresAtUtc);
        Assert.Null(purchase.EventId);
        Assert.Empty(purchase.HeldEventSeatIds);
        Assert.Null(purchase.PaymentToken);
    }

    [Fact]
    public void Start_accepts_name_and_email_at_the_ticket_limits()
    {
        var name = new string('a', Ticket.MaxHolderFullNameLength);
        var email = new string('a', Ticket.MaxHolderEmailLength - "@example.com".Length) + "@example.com";

        var purchase = Purchase.Start(name, email, Now, SessionDuration);

        Assert.Equal(name, purchase.Name);
        Assert.Equal(email, purchase.Email);
    }

    [Fact]
    public void Start_rejects_a_name_longer_than_the_ticket_allows()
    {
        var name = new string('a', Ticket.MaxHolderFullNameLength + 1);

        Assert.Throws<ArgumentException>(() => Purchase.Start(name, "juan.perez@example.com", Now, SessionDuration));
    }

    [Fact]
    public void Start_rejects_an_email_longer_than_the_ticket_allows()
    {
        var email = new string('a', Ticket.MaxHolderEmailLength - "@example.com".Length + 1) + "@example.com";

        Assert.Throws<ArgumentException>(() => Purchase.Start("Juan Perez", email, Now, SessionDuration));
    }

    [Fact]
    public void Hold_holds_new_seats_and_takes_the_event_of_the_first_hold()
    {
        var purchase = Start();
        var seats = Seats(2);

        var newSeats = purchase.Hold(EventId, seats, MaxSeats, Now);

        Assert.Equal(seats, newSeats);
        Assert.Equal(seats, purchase.HeldEventSeatIds);
        Assert.Equal(EventId, purchase.EventId);
    }

    [Fact]
    public void Repeating_a_hold_returns_no_new_seats_and_does_not_renew_the_session()
    {
        var purchase = Start();
        var seats = Seats(2);
        purchase.Hold(EventId, seats, MaxSeats, Now);

        var newSeats = purchase.Hold(EventId, [seats[0], seats[1], seats[1]], MaxSeats, Now.AddMinutes(5));

        Assert.Empty(newSeats);
        Assert.Equal(seats, purchase.HeldEventSeatIds);
        Assert.Equal(Now + SessionDuration, purchase.ExpiresAtUtc);
    }

    [Fact]
    public void Hold_beyond_the_limit_holds_nothing()
    {
        var purchase = Start();
        var held = Seats(2);
        purchase.Hold(EventId, held, MaxSeats, Now);

        var exception = Assert.Throws<SeatLimitReachedException>(() =>
            purchase.Hold(EventId, [held[0], .. Seats(2)], MaxSeats, Now));

        Assert.Equal("SEAT_LIMIT_REACHED", exception.Code);
        Assert.Equal(held, purchase.HeldEventSeatIds);
    }

    [Fact]
    public void Hold_up_to_the_limit_is_allowed()
    {
        var purchase = Start();
        purchase.Hold(EventId, Seats(2), MaxSeats, Now);

        purchase.Hold(EventId, Seats(1), MaxSeats, Now);

        Assert.Equal(MaxSeats, purchase.HeldEventSeatIds.Count);
    }

    [Fact]
    public void Hold_of_another_event_holds_nothing()
    {
        var purchase = Start();
        var held = Seats(1);
        purchase.Hold(EventId, held, MaxSeats, Now);

        Assert.Throws<ArgumentException>(() => purchase.Hold(OtherEventId, Seats(1), MaxSeats, Now));

        Assert.Equal(held, purchase.HeldEventSeatIds);
        Assert.Equal(EventId, purchase.EventId);
    }

    [Fact]
    public void Releasing_every_seat_allows_holding_seats_of_another_event()
    {
        var purchase = Start();
        var held = Seats(2);
        purchase.Hold(EventId, held, MaxSeats, Now);

        purchase.Release(held[0]);
        Assert.Equal(EventId, purchase.EventId);
        purchase.Release(held[1]);
        Assert.Null(purchase.EventId);

        var seats = Seats(1);
        purchase.Hold(OtherEventId, seats, MaxSeats, Now);

        Assert.Equal(OtherEventId, purchase.EventId);
        Assert.Equal(seats, purchase.HeldEventSeatIds);
    }

    [Fact]
    public void Partial_hold_keeps_the_seats_that_did_not_conflict()
    {
        var purchase = Start();
        var seats = Seats(3);
        purchase.Hold(EventId, seats, MaxSeats, Now);

        // seats[1] could not be locked: another purchase holds it.
        purchase.Release(seats[1]);

        Assert.Equal([seats[0], seats[2]], purchase.HeldEventSeatIds);
    }

    [Fact]
    public void Expired_session_cannot_hold_seats()
    {
        var purchase = Start();
        var expiresAt = purchase.ExpiresAtUtc;

        Assert.False(purchase.IsExpired(expiresAt.AddTicks(-1)));
        Assert.True(purchase.IsExpired(expiresAt));
        var exception = Assert.Throws<PurchaseExpiredException>(() =>
            purchase.Hold(EventId, Seats(1), MaxSeats, expiresAt));
        Assert.Equal("SESSION_EXPIRED", exception.Code);
        Assert.Empty(purchase.HeldEventSeatIds);
    }

    [Fact]
    public void Changing_the_held_seats_clears_the_payment_token()
    {
        var purchase = Start();
        var seats = Seats(2);
        purchase.Hold(EventId, seats, MaxSeats, Now);
        purchase.SetPaymentToken("token-1");

        purchase.Hold(EventId, seats, MaxSeats, Now);
        Assert.Equal("token-1", purchase.PaymentToken);

        purchase.Release(seats[0]);
        Assert.Null(purchase.PaymentToken);

        purchase.SetPaymentToken("token-2");
        purchase.Hold(EventId, Seats(1), MaxSeats, Now);
        Assert.Null(purchase.PaymentToken);
    }
}
