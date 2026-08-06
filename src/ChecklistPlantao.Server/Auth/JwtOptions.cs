using System.ComponentModel.DataAnnotations;

namespace ChecklistPlantao.Server.Auth;

/// <summary>
/// Parâmetros do JWT. A chave NUNCA fica no repositório: vem de variável de ambiente
/// (<c>Jwt__SigningKey</c>) ou de User Secrets em desenvolvimento. Ver docs/DEPLOYMENT.md.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Mínimo de 32 bytes para HMAC-SHA256; validado na inicialização.</summary>
    [Required]
    [MinLength(32, ErrorMessage = "A chave de assinatura precisa ter pelo menos 32 caracteres.")]
    public string SigningKey { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "ChecklistPlantao";

    [Required]
    public string Audience { get; set; } = "ChecklistPlantao.Client";

    /// <summary>
    /// Access token curto: se um usuário for desativado, a janela em que ele continua entrando
    /// com um token já emitido é no máximo esta.
    /// </summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>
    /// Refresh token longo o bastante para o aparelho ficar dias sem rede e ainda reconectar
    /// sem exigir nova digitação de senha.
    /// </summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 30;
}

/// <summary>Opções de bloqueio por tentativas de login.</summary>
public sealed class LoginLockoutOptions
{
    public const string SectionName = "Lockout";

    [Range(1, 20)]
    public int MaxFailedAttempts { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;
}

/// <summary>Criação do administrador inicial. Sem estes valores nenhum usuário é criado.</summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string? AdminUserName { get; set; }

    public string? AdminDisplayName { get; set; }

    public string? AdminPassword { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AdminUserName) && !string.IsNullOrWhiteSpace(AdminPassword);
}
