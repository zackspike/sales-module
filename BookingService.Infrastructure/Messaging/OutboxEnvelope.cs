using System.Text.Json;
using System.Text.Json.Nodes;

namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// The envelope every published message travels in (contract, section 3):
/// <c>{ "messageId", "type", "occurredAt", "data" }</c>.
/// </summary>
public static class OutboxEnvelope
{
    // Keeps accents and other non-ASCII characters readable (José, not José). The message is never embedded
    // in HTML, so the stricter default escaping buys nothing; JSON quotes and control characters are still escaped.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static byte[] Serialize(OutboxMessage message)
    {
        var envelope = new JsonObject
        {
            ["messageId"] = message.Id,
            ["type"] = message.Type,
            ["occurredAt"] = DateTime.SpecifyKind(message.OccurredAtUtc, DateTimeKind.Utc),
            ["data"] = JsonNode.Parse(message.Payload)
        };

        return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
    }
}
