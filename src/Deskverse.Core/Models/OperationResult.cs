namespace Deskverse.Core.Models;

/// <summary>Uniform success/failure result for service-level operations.</summary>
public sealed record OperationResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public static OperationResult Ok() => new() { Success = true };

    public static OperationResult Fail(string error) => new() { Success = false, Error = error };
}

public sealed record OperationResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public string? Error { get; init; }

    public static OperationResult<T> Ok(T value) => new() { Success = true, Value = value };

    public static OperationResult<T> Fail(string error) => new() { Success = false, Error = error };
}
