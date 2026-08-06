namespace ChecklistPlantao.Contracts.Auth;

public sealed record LoginRequest(string UserName, string Password, string? DeviceId, string? DeviceName);

public sealed record RefreshRequest(string RefreshToken, string? DeviceId);

public sealed record LogoutRequest(string RefreshToken);

/// <summary>
/// Par de tokens. O access token é curto; o refresh é rotacionado a cada uso e pode ser revogado.
/// Nenhum hash de senha do Identity acompanha a resposta — o login offline é derivado no dispositivo.
/// </summary>
public sealed record TokenPairDto(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

public sealed record AuthenticatedUserDto(
    Guid UserId,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Permissions,
    bool GrantsAllSectors,
    IReadOnlyList<Guid> SectorIds,
    IReadOnlyList<Guid> GroupIds);

public sealed record LoginResponse(TokenPairDto Tokens, AuthenticatedUserDto User);
