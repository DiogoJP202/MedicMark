using ChecklistPlantao.Contracts.Common;

namespace ChecklistPlantao.Application.Common;

/// <summary>
/// Falha de um caso de uso. O <see cref="Code"/> é estável e vira <c>ProblemDetails.extensions</c>;
/// a <see cref="Message"/> já está em português e pode ser exibida ao usuário.
/// </summary>
public sealed record OperationError(string Code, string Message, IReadOnlyList<ValidationError>? Details = null)
{
    public static OperationError NotFound(string message) => new(ApiErrorCodes.NotFound, message);

    public static OperationError Validation(string message, IReadOnlyList<ValidationError>? details = null) =>
        new(ApiErrorCodes.ValidationFailed, message, details);

    public static OperationError PermissionDenied(string message = "Você não tem permissão para esta ação.") =>
        new(ApiErrorCodes.PermissionDenied, message);

    public static OperationError SectorAccessDenied() =>
        new(ApiErrorCodes.SectorAccessDenied, "Você não tem acesso a este setor.");

    public static OperationError Conflict(string message) => new(ApiErrorCodes.VersionConflict, message);

    public static OperationError Duplicate(string message) => new(ApiErrorCodes.DuplicateValue, message);
}

/// <summary>Resultado sem valor de retorno.</summary>
public readonly record struct Result
{
    private Result(bool isSuccess, OperationError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public OperationError? Error { get; }

    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(true, null);

    public static Result Failure(OperationError error) => new(false, error);

    public static implicit operator Result(OperationError error) => Failure(error);
}

/// <summary>Resultado com valor de retorno.</summary>
public readonly record struct Result<T>
{
    private Result(bool isSuccess, T? value, OperationError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public OperationError? Error { get; }

    public bool IsFailure => !IsSuccess;

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(OperationError error) => new(false, default, error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(OperationError error) => Failure(error);

    /// <summary>Valor quando houve sucesso; lança se usado sobre uma falha. Usar após checar <see cref="IsSuccess"/>.</summary>
    public T Required => IsSuccess
        ? Value!
        : throw new InvalidOperationException($"Resultado sem valor: {Error?.Code} - {Error?.Message}");
}
