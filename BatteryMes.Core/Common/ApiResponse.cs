namespace BatteryMes.Core.Common;

/// <summary>Standard API envelope — success payloads use Data, failures use Error.</summary>
public record ApiResponse<T>(
    bool Success,
    T? Data,
    ApiError? Error,
    DateTime TimestampUtc)
{
    public static ApiResponse<T> Ok(T data) =>
        new(true, data, null, DateTime.UtcNow);

    public static ApiResponse<T> Fail(ApiError error) =>
        new(false, default, error, DateTime.UtcNow);
}

/// <summary>Non-generic variant for endpoints with no payload.</summary>
public record ApiResponse(bool Success, ApiError? Error, DateTime TimestampUtc)
{
    public static ApiResponse Ok() => new(true, null, DateTime.UtcNow);
    public static ApiResponse Fail(ApiError error) => new(false, error, DateTime.UtcNow);
}

public record ApiError(
    string Code,
    string Message,
    string? TraceId = null,
    IDictionary<string, string[]>? Details = null);
