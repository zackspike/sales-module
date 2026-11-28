using System.Text.Json;
using BookingService.Infrastructure.Messaging;

namespace BookingService.Infrastructure.Tests;

public class OutboxEnvelopeTests
{
    [Fact]
    public void The_envelope_carries_the_data_as_an_object_and_the_time_in_utc()
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = PostgresTicketSoldOutbox.TicketSoldType,
            OccurredAtUtc = new DateTime(2026, 11, 16, 8, 7, 30, DateTimeKind.Unspecified),
            Payload = """{"ticketCode":"TK-1","holderName":"José \"Pepe\""}"""
        };

        var envelope = JsonDocument.Parse(OutboxEnvelope.Serialize(message)).RootElement;

        Assert.Equal(message.Id, envelope.GetProperty("messageId").GetGuid());
        Assert.Equal("ticketSold", envelope.GetProperty("type").GetString());
        Assert.Equal("2026-11-16T08:07:30Z", envelope.GetProperty("occurredAt").GetString());
        var data = envelope.GetProperty("data");
        Assert.Equal(JsonValueKind.Object, data.ValueKind);
        Assert.Equal("José \"Pepe\"", data.GetProperty("holderName").GetString());
        Assert.Equal(["messageId", "type", "occurredAt", "data"], envelope.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Accents_travel_readable_while_quotes_stay_escaped()
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "ticketSold",
            Payload = """{"holderName":"José \"Pepe\" Ñúñez 🎟️"}"""
        };

        var json = System.Text.Encoding.UTF8.GetString(OutboxEnvelope.Serialize(message));

        Assert.Contains("José \\\"Pepe\\\" Ñúñez", json);
        Assert.Equal(
            "José \"Pepe\" Ñúñez 🎟️",
            JsonDocument.Parse(json).RootElement.GetProperty("data").GetProperty("holderName").GetString());
    }

    [Fact]
    public void A_payload_that_is_not_json_fails_instead_of_publishing_garbage()
    {
        var message = new OutboxMessage { Id = Guid.NewGuid(), Type = "ticketSold", Payload = "not json" };

        Assert.ThrowsAny<JsonException>(() => OutboxEnvelope.Serialize(message));
    }
}
