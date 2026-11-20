using BookingService.Application.Abstractions;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeIdempotencyStore : IIdempotencyStore
{
    public Dictionary<Guid, (IdempotencyRecord Record, TimeSpan Retention)> Records { get; } = [];

    public bool ThrowOnRemember { get; init; }

    public Task<IdempotencyRecord?> GetIdempotencyRecordAsync(Guid idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(Records.TryGetValue(idempotencyKey, out var value) ? value.Record : null);

    public Task RememberAsync(
        Guid idempotencyKey,
        IdempotencyRecord record,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        if (ThrowOnRemember)
        {
            return Task.FromException(new InvalidOperationException("store unavailable"));
        }

        Records[idempotencyKey] = (record, retention);
        return Task.CompletedTask;
    }
}
