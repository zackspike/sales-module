namespace BookingService.Api.Common;

/// <summary>
/// Standardized JSON response payload for API errors.
/// </summary>
public sealed class ErrorResponse
{
    public int StatusCode { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Details { get; init; }

    public string TraceId { get; init; } = string.Empty;

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public IDictionary<string, string[]>? Errors { get; init; }
}
