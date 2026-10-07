using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookingService.Infrastructure.Tests;

/// <summary>
/// End-to-end HTTP tests: API -> Application -> Infrastructure -> PostgreSQL + Redis.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public class BookingApiTests(InfrastructureFixture infrastructure)
{
    private readonly HttpClient _client = infrastructure.Api.CreateClient();

    [Fact]
    public async Task Health_check_is_ok()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Available_seats_of_the_default_event_come_from_the_database()
    {
        var response = await _client.GetAsync($"/events/{InfrastructureFixture.DefaultEventId}/tickets/available");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var seats = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.Equal(50, seats!.Count);
        Assert.All(seats, seat => Assert.Equal("Available", seat.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Available_seats_of_an_unknown_event_is_not_found()
    {
        var response = await _client.GetAsync($"/events/{Guid.NewGuid()}/tickets/available");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reservation_then_purchase_flow()
    {
        var seeded = await infrastructure.CreateEventAsync(2);
        var ticketId = seeded.TicketIds[0];
        var buyer = UniqueEmail();
        var otherBuyer = UniqueEmail();
        var key = Guid.NewGuid();

        // 1. Select the seat: it is locked for the buyer.
        var reserved = await ReserveAsync(seeded.EventId, ticketId, buyer);
        Assert.Equal(HttpStatusCode.OK, reserved.StatusCode);
        var reservation = await reserved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ticketId, reservation.GetProperty("ticketId").GetGuid());
        Assert.Equal("A-1", reservation.GetProperty("seatNumber").GetString());
        var expiresAt = reservation.GetProperty("expiresAtUtc").GetDateTime();
        Assert.InRange(expiresAt, DateTime.UtcNow.AddMinutes(9), DateTime.UtcNow.AddMinutes(11));

        Assert.Equal("Reserved", await StatusOfAsync(seeded.EventId, ticketId));
        Assert.Equal([seeded.TicketIds[1]], await AvailableTicketIdsAsync(seeded.EventId));

        // 2. Nobody else can take or buy it.
        Assert.Equal(HttpStatusCode.Conflict, (await ReserveAsync(seeded.EventId, ticketId, otherBuyer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PurchaseAsync(seeded.EventId, ticketId, Guid.NewGuid(), otherBuyer)).StatusCode);

        // 3. The buyer completes the purchase.
        var created = await PurchaseAsync(seeded.EventId, ticketId, key, buyer);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ticket = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ticketId, ticket.GetProperty("ticketId").GetGuid());
        Assert.Equal(seeded.EventId, ticket.GetProperty("eventId").GetGuid());
        Assert.Equal("A-1", ticket.GetProperty("seatNumber").GetString());
        Assert.Equal("Jane Doe", ticket.GetProperty("fullName").GetString());
        Assert.Equal(buyer, ticket.GetProperty("email").GetString());
        Assert.StartsWith("TK-", ticket.GetProperty("ticketCode").GetString());
        Assert.True(ticket.TryGetProperty("createdAt", out _));

        // 4. Retries are idempotent; the seat is sold for everyone else.
        var replayed = await PurchaseAsync(seeded.EventId, ticketId, key, buyer);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        var replayedTicket = await replayed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ticket.GetProperty("ticketCode").GetString(), replayedTicket.GetProperty("ticketCode").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await PurchaseAsync(seeded.EventId, ticketId, Guid.NewGuid(), buyer)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PurchaseAsync(seeded.EventId, seeded.TicketIds[1], key, buyer)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ReserveAsync(seeded.EventId, ticketId, otherBuyer)).StatusCode);

        Assert.Equal("Sold", await StatusOfAsync(seeded.EventId, ticketId));
        Assert.Equal([seeded.TicketIds[1]], await AvailableTicketIdsAsync(seeded.EventId));
    }

    [Fact]
    public async Task Purchase_without_reservation_is_forbidden()
    {
        var seeded = await infrastructure.CreateEventAsync(1);

        var response = await PurchaseAsync(seeded.EventId, seeded.TicketIds[0], Guid.NewGuid(), UniqueEmail());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Available", await StatusOfAsync(seeded.EventId, seeded.TicketIds[0]));
    }

    [Fact]
    public async Task Unknown_seat_or_event_is_not_found()
    {
        var seeded = await infrastructure.CreateEventAsync(1);

        var unknownSeat = await PurchaseAsync(seeded.EventId, Guid.NewGuid(), Guid.NewGuid(), UniqueEmail());
        var unknownEvent = await PurchaseAsync(Guid.NewGuid(), seeded.TicketIds[0], Guid.NewGuid(), UniqueEmail());
        var unknownReservation = await ReserveAsync(seeded.EventId, Guid.NewGuid(), UniqueEmail());
        var unknownAvailability = await _client.GetAsync($"/events/{seeded.EventId}/tickets/{Guid.NewGuid()}/availability");

        Assert.Equal(HttpStatusCode.NotFound, unknownSeat.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownEvent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownReservation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownAvailability.StatusCode);
    }

    [Fact]
    public async Task Invalid_body_or_missing_key_is_a_bad_request()
    {
        var seeded = await infrastructure.CreateEventAsync(1);
        var url = $"/events/{seeded.EventId}/tickets/{seeded.TicketIds[0]}/purchase";

        var invalidReservation = await ReserveAsync(seeded.EventId, seeded.TicketIds[0], "not-an-email");
        var invalidBody = await PurchaseAsync(seeded.EventId, seeded.TicketIds[0], Guid.NewGuid(), "not-an-email");
        var missingKey = await _client.PostAsJsonAsync(url, new { fullName = "Jane Doe", email = "jane@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, invalidReservation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidBody.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        Assert.Equal("Available", await StatusOfAsync(seeded.EventId, seeded.TicketIds[0]));
    }

    private Task<HttpResponseMessage> ReserveAsync(Guid eventId, Guid ticketId, string email)
    {
        return _client.PostAsJsonAsync(
            $"/events/{eventId}/tickets/{ticketId}/reserve",
            new { fullName = "Jane Doe", email });
    }

    private Task<HttpResponseMessage> PurchaseAsync(Guid eventId, Guid ticketId, Guid key, string email)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/events/{eventId}/tickets/{ticketId}/purchase")
        {
            Content = JsonContent.Create(new { fullName = "Jane Doe", email })
        };
        request.Headers.Add("X-Idempotency-Key", key.ToString());

        return _client.SendAsync(request);
    }

    private async Task<string?> StatusOfAsync(Guid eventId, Guid ticketId)
    {
        var availability = await _client.GetFromJsonAsync<JsonElement>(
            $"/events/{eventId}/tickets/{ticketId}/availability");
        return availability.GetProperty("status").GetString();
    }

    private async Task<List<Guid>> AvailableTicketIdsAsync(Guid eventId)
    {
        var seats = await _client.GetFromJsonAsync<List<JsonElement>>($"/events/{eventId}/tickets/available");
        return seats!.Select(s => s.GetProperty("ticketId").GetGuid()).ToList();
    }

    private static string UniqueEmail() => $"jane-{Guid.NewGuid():N}@example.com";
}
