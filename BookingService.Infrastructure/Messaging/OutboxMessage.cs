namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// A message waiting in the transactional outbox, or already published by <see cref="OutboxRelay"/>.
/// <see cref="Payload"/> is the <c>data</c> of the envelope (contract, section 3) as JSON.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>The <c>messageId</c> consumers use to deduplicate (delivery is at least once).</summary>
    public Guid Id { get; init; }

    public string Type { get; init; } = string.Empty;

    public DateTime OccurredAtUtc { get; init; }

    public string Payload { get; init; } = string.Empty;

    /// <summary>When the broker confirmed the message; null while it is pending.</summary>
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>Failed publication attempts so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Why the last attempt failed, for diagnosis; null once published.</summary>
    public string? LastError { get; set; }
}
