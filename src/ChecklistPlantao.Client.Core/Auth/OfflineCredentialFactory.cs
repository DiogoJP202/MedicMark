using System.Security.Cryptography;
using System.Text;
using ChecklistPlantao.Client.Core.Persistence;

namespace ChecklistPlantao.Client.Core.Auth;

/// <summary>Parâmetros do verificador offline. Configuráveis para poderem crescer com o tempo.</summary>
public sealed class OfflineAuthOptions
{
    public const string SectionName = "OfflineAuth";

    /// <summary>
    /// Iterações do PBKDF2. O valor precisa ser alto o bastante para atrapalhar um ataque de
    /// dicionário e baixo o bastante para o login não travar um aparelho antigo.
    /// </summary>
    public int Iterations { get; set; } = 210_000;

    public int SaltBytes { get; set; } = 16;

    public int VerifierBytes { get; set; } = 32;

    /// <summary>Quanto tempo a conta fica bloqueada após estourar as tentativas locais.</summary>
    public int LockMinutes { get; set; } = 15;
}

/// <summary>
/// Deriva e verifica o segredo local que permite entrar sem servidor.
///
/// Pontos inegociáveis:
///   • a senha nunca é armazenada, nem em texto puro nem cifrada de forma reversível;
///   • o <c>PasswordHash</c> do ASP.NET Identity nunca chega ao dispositivo;
///   • cada dispositivo tem sal próprio, então o mesmo usuário gera verificadores diferentes;
///   • a comparação é em tempo fixo, para não vazar informação por tempo de resposta.
///
/// Consequência aceita: quem consegue ler o banco do aparelho pode tentar força bruta offline.
/// É por isso que existem iterações altas, limite de tentativas e validade curta.
/// </summary>
public static class OfflineCredentialFactory
{
    public static (byte[] Salt, byte[] Verifier, int Iterations) Derive(string password, OfflineAuthOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentNullException.ThrowIfNull(options);

        var salt = RandomNumberGenerator.GetBytes(options.SaltBytes);
        var verifier = Compute(password, salt, options.Iterations, options.VerifierBytes);

        return (salt, verifier, options.Iterations);
    }

    public static bool Verify(string password, LocalCredential credential, int verifierBytes = 32)
    {
        ArgumentNullException.ThrowIfNull(credential);

        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        var candidate = Compute(password, credential.Salt, credential.Iterations, verifierBytes);

        return CryptographicOperations.FixedTimeEquals(candidate, credential.Verifier);
    }

    private static byte[] Compute(string password, byte[] salt, int iterations, int length) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, length);
}
