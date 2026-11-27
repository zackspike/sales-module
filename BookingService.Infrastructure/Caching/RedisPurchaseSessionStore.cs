using BookingService.Application.Abstractions;
using BookingService.Domain.Purchases;
using StackExchange.Redis;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Purchase sessions stored as Redis strings <c>booking:session:{purchaseId}</c> with a TTL taken
/// from <see cref="PurchaseSession.ExpiresAtUtc"/>. Saving is a Lua compare-and-swap: a session that
/// changed since it was read cannot be overwritten by a stale writer.
/// </summary>
public sealed class RedisPurchaseSessionStore : IPurchaseSessionStore
{
    // ARGV: expected value (empty string means "insert only"), new value, TTL in milliseconds.
    // Returns 1 when the value was written, 0 when the session changed or already existed.
    private const string SaveScript = """
        local current = redis.call('GET', KEYS[1])
        if ARGV[1] == '' then
            if current then return 0 end
        elseif current ~= ARGV[1] then
            return 0
        end
        redis.call('PSETEX', KEYS[1], ARGV[3], ARGV[2])
        return 1
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly PurchaseSessionSnapshotRegistry _snapshots = new();

    public RedisPurchaseSessionStore(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _redis = redis;
    }

    private IDatabase Database => _redis.GetDatabase();

    public async Task<PurchaseSession?> GetAsync(Guid purchaseId, CancellationToken cancellationToken)
    {
        var value = await Database.StringGetAsync(RedisKeys.Session(purchaseId));
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        var bytes = (byte[])value!;
        var purchase = PurchaseSessionSnapshot.Deserialize(bytes);
        if (purchase is not null)
        {
            _snapshots.Remember(purchase, bytes);
        }

        return purchase;
    }

    public async Task SaveAsync(PurchaseSession purchase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(purchase);

        // The TTL always comes from the session deadline, so saving never buys more time (ADR-001).
        var ttlMilliseconds = (long)Math.Ceiling((purchase.ExpiresAtUtc - DateTime.UtcNow).TotalMilliseconds);
        if (ttlMilliseconds <= 0)
        {
            return;
        }

        var bytes = PurchaseSessionSnapshot.Serialize(purchase);
        var expected = _snapshots.ExpectedValueOf(purchase);

        var outcome = (int)await Database.ScriptEvaluateAsync(
            SaveScript,
            [RedisKeys.Session(purchase.Id)],
            [expected is null ? RedisValue.EmptyString : expected, bytes, ttlMilliseconds]);

        if (outcome != 1)
        {
            throw new PurchaseSessionConcurrencyException(purchase.Id);
        }

        _snapshots.Remember(purchase, bytes);
    }
}
