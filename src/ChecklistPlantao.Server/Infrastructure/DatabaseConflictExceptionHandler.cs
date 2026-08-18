using ChecklistPlantao.Contracts.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ChecklistPlantao.Server.Infrastructure;

/// <summary>
/// Traduz violação de restrição do banco em 409, em vez de deixar virar 500.
///
/// As restrições de unicidade são impostas por índice, e não só por código (decisão D-016) — o que
/// está certo: duas requisições simultâneas não conseguem burlar a regra. O problema é que nada
/// tratava a exceção resultante, então uma colisão de nome saía como "erro inesperado", sem
/// nenhuma informação útil para quem estava preenchendo o formulário.
///
/// Os serviços de aplicação continuam checando duplicidade antes de gravar, com mensagem específica.
/// Este manipulador é a rede embaixo: cobre a corrida entre duas gravações concorrentes e qualquer
/// restrição que ninguém tenha lembrado de checar.
/// </summary>
public sealed class DatabaseConflictExceptionHandler(ILogger<DatabaseConflictExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Códigos de erro do SQLite para violação de restrição (19 e seus derivados).</summary>
    private const int SqliteConstraint = 19;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not DbUpdateException dbUpdate || !IsConstraintViolation(dbUpdate))
        {
            return false;
        }

        // Nível de aviso, não de erro: é uma colisão legítima de dados, não uma falha do servidor.
        logger.LogWarning(dbUpdate, "Restrição do banco violada em {Caminho}.", httpContext.Request.Path);

        var problem = new ProblemDetails
        {
            Title = "Conflito",
            Detail = "Este valor já está em uso. Atualize a tela e confira os dados antes de salvar novamente.",
            Status = StatusCodes.Status409Conflict,
            Instance = httpContext.Request.Path,
        };

        problem.Extensions["codigo"] = ApiErrorCodes.DuplicateValue;

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// A mensagem do SQLite nunca vai para o cliente: ela nomeia tabelas e colunas internas.
    /// Aqui ela serve apenas para distinguir violação de restrição de uma falha real de gravação.
    /// </summary>
    private static bool IsConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException sqlite
        && (sqlite.SqliteErrorCode == SqliteConstraint || sqlite.SqliteExtendedErrorCode / 256 == SqliteConstraint);
}
