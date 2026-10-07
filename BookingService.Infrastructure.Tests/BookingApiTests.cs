using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookingService.Infrastructure.Tests;

/// <summary>
/// End-to-end HTTP tests: API -> Application -> Infrastructure -> PostgreSQL.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class BookingApiTests(PostgreSqlFixture database)
{
    private readonly HttpClient _client = database.Api.CreateClient();

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
        var response = await _client.GetAsync($"/events/{PostgreSqlFixture.DefaultEventId}/tickets/available");

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
    public async Task Purchase_flow_creates_replays_and_rejects_resale()
    {
        var seeded = await database.CreateEventAsync(2);
        var ticketId = seeded.TicketIds[0];
        var key = Guid.NewGuid();

        var created = await PurchaseAsync(seeded.EventId, ticketId, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ticket = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ticketId, ticket.GetProperty("ticketId").GetGuid());
        Assert.Equal(seeded.EventId, ticket.GetProperty("eventId").GetGuid());
        Assert.Equal("A-1", ticket.GetProperty("seatNumber").GetString());
        Assert.Equal("Jane Doe", ticket.GetProperty("fullName").GetString());
        Assert.StartsWith("TK-", ticket.GetProperty("ticketCode").GetString());
        Assert.True(ticket.TryGetProperty("createdAt", out _));

        var replayed = await PurchaseAsync(seeded.EventId, ticketId, key);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        var replayedTicket = await replayed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ticket.GetProperty("ticketCode").GetString(), replayedTicket.GetProperty("ticketCode").GetString());

        var resale = await PurchaseAsync(seeded.EventId, ticketId, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, resale.StatusCode);

        var keyReuse = await PurchaseAsync(seeded.EventId, seeded.TicketIds[1], key);
        Assert.Equal(HttpStatusCode.Conflict, keyReuse.StatusCode);

        var availability = await _client.GetFromJsonAsync<JsonElement>(
            $"/events/{seeded.EventId}/tickets/{ticketId}/availability");
        Assert.Equal("Sold", availability.GetProperty("status").GetString());

        var available = await _client.GetFromJsonAsync<List<JsonElement>>(
            $"/events/{seeded.EventId}/tickets/available");
        Assert.Equal(seeded.TicketIds[1], Assert.Single(available!).GetProperty("ticketId").GetGuid());
    }

    [Fact]
    public async Task Unknown_seat_or_event_is_not_found()
    {
        var seeded = await database.CreateEventAsync(1);

        var unknownSeat = await PurchaseAsync(seeded.EventId, Guid.NewGuid(), Guid.NewGuid());
        var unknownEvent = await PurchaseAsync(Guid.NewGuid(), seeded.TicketIds[0], Guid.NewGuid());
        var unknownAvailability = await _client.GetAsync($"/events/{seeded.EventId}/tickets/{Guid.NewGuid()}/availability");

        Assert.Equal(HttpStatusCode.NotFound, unknownSeat.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownEvent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownAvailability.StatusCode);
    }

    [Fact]
    public async Task Invalid_body_or_missing_key_is_a_bad_request()
    {
        var seeded = await database.CreateEventAsync(1);
        var url = $"/events/{seeded.EventId}/tickets/{seeded.TicketIds[0]}/purchase";

        var invalidBody = await PurchaseAsync(seeded.EventId, seeded.TicketIds[0], Guid.NewGuid(), email: "not-an-email");
        var missingKey = await _client.PostAsJsonAsync(url, new { fullName = "Jane Doe", email = "jane@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, invalidBody.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);

        var availability = await _client.GetFromJsonAsync<JsonElement>(
            $"/events/{seeded.EventId}/tickets/{seeded.TicketIds[0]}/availability");
        Assert.Equal("Available", availability.GetProperty("status").GetString());
    }

    private Task<HttpResponseMessage> PurchaseAsync(
        Guid eventId,
        Guid ticketId,
        Guid key,
        string? email = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/events/{eventId}/tickets/{ticketId}/purchase")
        {
            Content = JsonContent.Create(new
            {
                fullName = "Jane Doe",
                email = email ?? $"jane-{Guid.NewGuid():N}@example.com"
            })
        };
        request.Headers.Add("X-Idempotency-Key", key.ToString());

        return _client.SendAsync(request);
    }
}
