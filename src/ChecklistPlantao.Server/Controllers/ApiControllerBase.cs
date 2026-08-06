using ChecklistPlantao.Application.Common;
using ChecklistPlantao.Contracts.Common;
using Microsoft.AspNetCore.Mvc;

namespace ChecklistPlantao.Server.Controllers;

/// <summary>
/// Base dos controllers da API. Concentra a tradução de <see cref="Result"/> para resposta HTTP,
/// de modo que nenhum controller escreva <c>StatusCode(409)</c> à mão.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult FromResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Problem(result.Error!);

    protected IActionResult FromResult(Result result) =>
        result.IsSuccess ? NoContent() : Problem(result.Error!);

    protected IActionResult CreatedFromResult<T>(Result<T> result, string routeName, object routeValues) =>
        result.IsSuccess ? CreatedAtRoute(routeName, routeValues, result.Value) : Problem(result.Error!);

    /// <summary>
    /// Converte o erro em <c>ProblemDetails</c>. O código estável vai em <c>extensions.codigo</c>
    /// para o cliente decidir o texto; nunca enviamos stack trace.
    /// </summary>
    protected IActionResult Problem(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = StatusFor(error.Code);

        var problem = new ProblemDetails
        {
            Title = TitleFor(status),
            Detail = error.Message,
            Status = status,
            Instance = HttpContext.Request.Path,
        };

        problem.Extensions["codigo"] = error.Code;

        if (error.Details is { Count: > 0 })
        {
            problem.Extensions["campos"] = error.Details;
        }

        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    private static int StatusFor(string code) => code switch
    {
        ApiErrorCodes.InvalidCredentials => StatusCodes.Status401Unauthorized,
        ApiErrorCodes.TokenInvalid => StatusCodes.Status401Unauthorized,
        ApiErrorCodes.AccountLocked => StatusCodes.Status423Locked,
        ApiErrorCodes.AccountInactive => StatusCodes.Status403Forbidden,
        ApiErrorCodes.PermissionDenied => StatusCodes.Status403Forbidden,
        ApiErrorCodes.SectorAccessDenied => StatusCodes.Status403Forbidden,
        ApiErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ApiErrorCodes.VersionConflict => StatusCodes.Status409Conflict,
        ApiErrorCodes.SessionAlreadyOpen => StatusCodes.Status409Conflict,
        ApiErrorCodes.DuplicateValue => StatusCodes.Status409Conflict,
        ApiErrorCodes.SessionClosed => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status400BadRequest,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => "Não autenticado",
        StatusCodes.Status403Forbidden => "Acesso negado",
        StatusCodes.Status404NotFound => "Não encontrado",
        StatusCodes.Status409Conflict => "Conflito",
        StatusCodes.Status422UnprocessableEntity => "Operação não permitida no estado atual",
        StatusCodes.Status423Locked => "Conta bloqueada",
        _ => "Requisição inválida",
    };
}
