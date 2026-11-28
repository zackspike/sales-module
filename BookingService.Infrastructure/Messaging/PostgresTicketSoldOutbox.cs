using System.Text.Json;
using BookingService.Application.Abstractions;
using BookingService.Infrastructure.Persistence;

namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// Writes <c>ticketSold</c> messages to the <c>outbox_messages</c> table through the request's
/// <see cref="BookingDbContext"/>, so they join the transaction <see cref="PostgresPurchaseSaleRepository"/> has
/// open (ADR-003). <see cref="OutboxRelay"/> publishes them afterwards.
/// </summary>
public sealed class PostgresTicketSoldOutbox : ITicketSoldOutbox
{
    public const string TicketSoldType = "ticketSold";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BookingDbContext _dbContext;

    public PostgresTicketSoldOutbox(BookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <exception cref="InvalidOperationException">No transaction is open: the messages would outlive a rolled-back sale.</exception>
    public async Task AddAsync(IReadOnlyList<TicketSoldEvent> messages, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "ticketSold messages must be written inside the sale's transaction (ADR-003).");
        }

        foreach (var message in messages)
        {
            var soldAtUtc = ToPostgresPrecision(message.SoldAtUtc);
            _dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = TicketSoldType,
                OccurredAtUtc = soldAtUtc,
                Payload = JsonSerializer.Serialize(
                    new TicketSoldData(
                        message.PurchaseId,
                        message.TicketCode,
                        message.EventId,
                        message.EventSeatId,
                        message.HolderName,
                        message.HolderEmail,
                        soldAtUtc),
                    JsonOptions)
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// PostgreSQL keeps microseconds and .NET keeps 100 ns ticks, so the stored ticket loses the last digit. The
    /// event uses the stored value, so <c>soldAt</c> matches what <c>GET /tickets/{code}</c> returns.
    /// </summary>
    private static DateTime ToPostgresPrecision(DateTime value)
    {
        var utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    /// <summary>The <c>data</c> of <c>ticketSold</c>, with the names of the contract (section 3).</summary>
    private sealed record TicketSoldData(
        Guid PurchaseId,
        string TicketCode,
        Guid EventId,
        Guid EventSeatId,
        string HolderName,
        string HolderEmail,
        DateTime SoldAt);
}
