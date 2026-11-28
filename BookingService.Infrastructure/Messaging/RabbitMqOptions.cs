namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// <c>RabbitMq</c> configuration section. The connection itself is <c>ConnectionStrings:RabbitMq</c>
/// (an AMQP URI, e.g. <c>amqp://guest:guest@localhost:5672/</c>).
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>
    /// Durable topic exchange Booking publishes to. Each message's routing key is its type (<c>ticketSold</c>);
    /// consumers (EventService) bind their own queue to it.
    /// </summary>
    public string Exchange { get; init; } = "booking.events";
}
