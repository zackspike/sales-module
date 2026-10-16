using BookingService.Application.Abstractions;

namespace BookingService.Application.Tests.EventSeats.Fakes;

public sealed class FakeIdempotencyStore : IIdempotencyStore
{
    public Dictionary<Guid, (IdempotencyRecord Record, TimeSpan Retention)> Records { get; } = [];

    public bool ThrowOnRemember { get; init; }

    public IdempotencyRecord? GetIdempotencyRecord(Guid idempotencyKey) =>
        Records.TryGetValue(idempotencyKey, out var value) ? value.Record : null;

    public void Remember(Guid idempotencyKey, IdempotencyRecord record, TimeSpan retention)
    {
        if (ThrowOnRemember)
        {
            throw new InvalidOperationException("store unavailable");
        }

        Records[idempotencyKey] = (record, retention);
    }
}
