using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ChecklistPlantao.Application.Abstractions;
using ChecklistPlantao.Contracts.Auth;
using ChecklistPlantao.Domain.Access;
using ChecklistPlantao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ChecklistPlantao.Server.Auth;

/// <summary>
/// Emissão, rotação e revogação de tokens.
///
/// O refresh token é gerado com aleatoriedade criptográfica e persistido apenas como hash
/// SHA-256 — vazar a tabela não permite reutilizar tokens. Cada uso rotaciona: o token antigo
/// é revogado e apontado para o novo.
/// </summary>
public sealed class TokenService(AppDbContext db, IOptions<JwtOptions> options, IClock clock)
{
    private readonly JwtOptions _options = options.Value;

    public async Task<TokenPairDto> IssueAsync(
        Guid userId,
        string userName,
        string displayName,
        EffectiveAccess access,
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);

        var now = clock.UtcNow;
        var accessExpires = now.AddMinutes(_options.AccessTokenMinutes);
        var accessToken = CreateAccessToken(userId, userName, displayName, access, deviceId, now, accessExpires);

        var (refreshToken, refreshHash) = CreateRefreshToken();
        var refreshExpires = now.AddDays(_options.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken(Guid.CreateVersion7(), userId, refreshHash, deviceId, now, refreshExpires));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new TokenPairDto(accessToken, accessExpires, refreshToken, refreshExpires);
    }

    /// <summary>
    /// Valida e rotaciona o refresh token. Reapresentar um token já substituído revoga toda a
    /// cadeia daquele usuário no mesmo dispositivo — é o sinal clássico de token roubado.
    /// </summary>
    public async Task<Guid?> ConsumeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(refreshToken);
        var now = clock.UtcNow;

        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        if (!stored.IsActive(now))
        {
            await RevokeAllForUserAsync(stored.UserId, cancellationToken).ConfigureAwait(false);
            return null;
        }

        stored.Revoke(now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return stored.UserId;
    }

    public async Task<bool> RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            return false;
        }

        stored.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in tokens)
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Remove tokens expirados há mais de um dia. Chamado pelo serviço de manutenção.</summary>
    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var limit = clock.UtcNow.AddDays(-1);
        return db.RefreshTokens.Where(t => t.ExpiresAtUtc < limit).ExecuteDeleteAsync(cancellationToken);
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }

    private string CreateAccessToken(
        Guid userId,
        string userName,
        string displayName,
        EffectiveAccess access,
        string? deviceId,
        DateTime issuedAt,
        DateTime expiresAt)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, userName),
            new(AppClaimTypes.DisplayName, displayName),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };

        claims.AddRange(access.Permissions.Select(p => new Claim(AppClaimTypes.Permission, p)));
        claims.AddRange(access.ExplicitSectorIds.Select(s => new Claim(AppClaimTypes.Sector, s.ToString())));

        if (access.GrantsAllSectors)
        {
            claims.Add(new Claim(AppClaimTypes.AllSectors, "true"));
        }

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            claims.Add(new Claim(AppClaimTypes.DeviceId, deviceId));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static (string Token, string Hash) CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(bytes);
        return (token, Hash(token));
    }
}
