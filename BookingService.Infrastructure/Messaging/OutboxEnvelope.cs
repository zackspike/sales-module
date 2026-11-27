using System.Text.Json;
using System.Text.Json.Nodes;

namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// The envelope every published message travels in (contract, section 3):
/// <c>{ "messageId", "type", "occurredAt", "data" }</c>.
/// </summary>
public static class OutboxEnvelope
{
    public static byte[] Serialize(OutboxMessage message)
    {
        var envelope = new JsonObject
        {
            ["messageId"] = message.Id,
            ["type"] = message.Type,
            ["occurredAt"] = DateTime.SpecifyKind(message.OccurredAtUtc, DateTimeKind.Utc),
            ["data"] = JsonNode.Parse(message.Payload)
        };

        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }
}
