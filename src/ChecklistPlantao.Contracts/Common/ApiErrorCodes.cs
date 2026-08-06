namespace ChecklistPlantao.Contracts.Common;

/// <summary>
/// Códigos de erro estáveis, enviados em <c>ProblemDetails.extensions["codigo"]</c>.
/// O cliente decide a mensagem para o usuário a partir do código; o texto do servidor é
/// para diagnóstico, não para exibição direta.
/// </summary>
public static class ApiErrorCodes
{
    public const string InvalidCredentials = "credenciais.invalidas";
    public const string AccountLocked = "conta.bloqueada";
    public const string AccountInactive = "conta.inativa";
    public const string TokenInvalid = "token.invalido";
    public const string PermissionDenied = "permissao.negada";
    public const string SectorAccessDenied = "setor.semAcesso";
    public const string NotFound = "recurso.naoEncontrado";
    public const string ValidationFailed = "validacao.falhou";
    public const string VersionConflict = "versao.conflito";
    public const string SessionClosed = "sessao.encerrada";
    public const string SessionAlreadyOpen = "sessao.jaAberta";
    public const string DuplicateValue = "valor.duplicado";
}

/// <summary>Erro de validação de um campo específico.</summary>
public sealed record ValidationError(string Field, string Message);
