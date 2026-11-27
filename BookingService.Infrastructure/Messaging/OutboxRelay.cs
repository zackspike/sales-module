using BookingService.Infrastructure.Persistence;
using BookingService.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// Publishes the pending outbox messages every <see cref="OutboxOptions.PollInterval"/>, oldest first, and marks
/// each one once the broker confirmed it. Delivery is at least once: a message published right before a crash or
/// a failed update is published again, and consumers deduplicate by <c>messageId</c>.
/// <para>
/// Pending rows are read with <c>FOR UPDATE SKIP LOCKED</c>, so several instances never publish the same batch at
/// the same time. A publication that fails or exceeds <see cref="OutboxOptions.PublishTimeout"/> ends the cycle
/// (the broker is likely down, and the order is kept); the message stays pending for the next one.
/// </para>
/// </summary>
public sealed class OutboxRelay : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxPublisher _publisher;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxRelay> _logger;

    public OutboxRelay(
        IServiceScopeFactory scopeFactory,
        IOutboxPublisher publisher,
        OutboxOptions options,
        ILogger<OutboxRelay> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Publishes up to <see cref="OutboxOptions.BatchSize"/> pending messages and returns how many were published.
    /// </summary>
    public async Task<int> RelayPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var pending = await dbContext.OutboxMessages
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE "PublishedAtUtc" IS NULL
                ORDER BY "OccurredAtUtc", "Id"
                LIMIT {_options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var published = 0;
        foreach (var message in pending)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.PublishTimeout);
            try
            {
                await _publisher.PublishAsync(message, timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The host is stopping: not a failed attempt. The message stays pending for the next run.
                break;
            }
            catch (Exception exception)
            {
                message.Attempts++;
                message.LastError = Truncate(timeout.IsCancellationRequested
                    ? $"Publication timed out after {_options.PublishTimeout}."
                    : exception.Message);
                _logger.LogWarning(
                    exception,
                    "Outbox message {MessageId} ({Type}) could not be published (attempt {Attempts}); it is retried in the next cycle.",
                    message.Id,
                    message.Type,
                    message.Attempts);
                break;
            }

            message.PublishedAtUtc = DateTime.UtcNow;
            message.LastError = null;
            published++;
        }

        // Kept even when the host is stopping: these messages already reached the broker.
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return published;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval);
        try
        {
            do
            {
                try
                {
                    await RelayPendingAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // The database is unreachable or the cycle failed: try again on the next tick.
                    _logger.LogError(exception, "Outbox relay cycle failed; pending messages are retried in the next cycle.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    private static string Truncate(string error) =>
        error.Length <= OutboxMessageConfiguration.MaxLastErrorLength
            ? error
            : error[..OutboxMessageConfiguration.MaxLastErrorLength];
}
