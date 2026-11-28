namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// <c>Outbox</c> configuration section, read by <see cref="OutboxRelay"/>.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>How often the relay looks for pending messages (the "X seconds" of the plan, decision 6).</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Most messages published in one cycle.</summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>How long one publication may take before it is left pending for the next cycle.</summary>
    public TimeSpan PublishTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
