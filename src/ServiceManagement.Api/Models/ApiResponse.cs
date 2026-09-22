namespace ServiceManagement.Api.Models;

/// <summary>A single field-level error.</summary>
public sealed record ApiError(string Field, string Message);

/// <summary>
/// The single response contract of the whole API: clients never have to guess
/// whether a call failed, and validation errors always arrive in the same shape.
/// </summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }

    public T? Data { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<ApiError> Errors { get; init; } = [];

    public string? CorrelationId { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null, string? correlationId = null)
        => new()
        {
            Success = true,
            Data = data,
            Message = message,
            CorrelationId = correlationId
        };

    public static ApiResponse<T> Fail(
        string message,
        IReadOnlyList<ApiError>? errors = null,
        string? correlationId = null)
        => new()
        {
            Success = false,
            Message = message,
            Errors = errors ?? [],
            CorrelationId = correlationId
        };
}
