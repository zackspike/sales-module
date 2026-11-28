namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// Sends one outbox message to the broker. Returns only once the broker confirmed it; throws otherwise, and
/// the relay retries the message in its next cycle.
/// </summary>
public interface IOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
