using System.Reflection;
using System.Text.Json;
using BookingService.Domain.Purchases;

namespace BookingService.Infrastructure.Caching;

/// <summary>
/// Converts <see cref="PurchaseSession"/> to and from the JSON kept in Redis. Domain has no
/// persistence factory, so rebuilding uses its private members; the reflection stays here, and the
/// round-trip tests fail loudly if that private shape changes.
/// </summary>
internal static class PurchaseSessionSnapshot
{
    private static readonly PropertyInfo IdProperty = Property(nameof(PurchaseSession.Id));
    private static readonly PropertyInfo EventIdProperty = Property(nameof(PurchaseSession.EventId));
    private static readonly PropertyInfo NameProperty = Property(nameof(PurchaseSession.Name));
    private static readonly PropertyInfo EmailProperty = Property(nameof(PurchaseSession.Email));
    private static readonly PropertyInfo ExpiresAtUtcProperty = Property(nameof(PurchaseSession.ExpiresAtUtc));
    private static readonly PropertyInfo PaymentTokenProperty = Property(nameof(PurchaseSession.PaymentToken));

    private static readonly FieldInfo HeldEventSeatIdsField =
        typeof(PurchaseSession).GetField("_heldEventSeatIds", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PurchaseSession._heldEventSeatIds is missing.");

    public static byte[] Serialize(PurchaseSession purchase)
    {
        ArgumentNullException.ThrowIfNull(purchase);

        return JsonSerializer.SerializeToUtf8Bytes(new PurchaseSessionData
        {
            Id = purchase.Id,
            EventId = purchase.EventId,
            Name = purchase.Name,
            Email = purchase.Email,
            ExpiresAtUtc = purchase.ExpiresAtUtc,
            HeldEventSeatIds = purchase.HeldEventSeatIds.ToList(),
            PaymentToken = purchase.PaymentToken
        });
    }

    /// <summary>
    /// Returns null for a value that is not a session at all. A value that is JSON but cannot be
    /// rebuilt throws <see cref="InvalidOperationException"/>: that is a server-side shape mismatch,
    /// not an expired session.
    /// </summary>
    public static PurchaseSession? Deserialize(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        PurchaseSessionData? data;
        try
        {
            data = JsonSerializer.Deserialize<PurchaseSessionData>(value);
        }
        catch (JsonException)
        {
            return null;
        }

        if (data is null)
        {
            return null;
        }

        try
        {
            return Restore(data);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("The stored purchase session could not be rebuilt.", exception);
        }
    }

    private static PropertyInfo Property(string name) =>
        typeof(PurchaseSession).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException($"PurchaseSession.{name} is missing.");

    private static void Set(PurchaseSession purchase, PropertyInfo property, object? value)
    {
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException($"PurchaseSession.{property.Name} has no setter.");
        setter.Invoke(purchase, [value]);
    }

    private static PurchaseSession Restore(PurchaseSessionData data)
    {
        var purchase = (PurchaseSession)Activator.CreateInstance(typeof(PurchaseSession), nonPublic: true)!;
        Set(purchase, IdProperty, data.Id);
        Set(purchase, EventIdProperty, data.EventId);
        Set(purchase, NameProperty, data.Name);
        Set(purchase, EmailProperty, data.Email);
        Set(purchase, ExpiresAtUtcProperty, data.ExpiresAtUtc);
        Set(purchase, PaymentTokenProperty, data.PaymentToken);

        var heldEventSeatIds = (List<Guid>)HeldEventSeatIdsField.GetValue(purchase)!;
        heldEventSeatIds.AddRange(data.HeldEventSeatIds ?? []);

        return purchase;
    }

    private sealed class PurchaseSessionData
    {
        public Guid Id { get; set; }

        public Guid EventId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public DateTime ExpiresAtUtc { get; set; }

        public List<Guid> HeldEventSeatIds { get; set; } = [];

        public string? PaymentToken { get; set; }
    }
}
