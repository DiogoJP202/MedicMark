using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChecklistPlantao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EsquemaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracaoNotificacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SoundEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    VibrationEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    EnabledOnAndroid = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnabledOnWindows = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowFullScreenIntent = table.Column<bool>(type: "INTEGER", nullable: false),
                    TitleTemplate = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BodyTemplate = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracaoNotificacoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Configuracoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuracoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Credenciais",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Credenciais", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dispositivos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AppVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NotificationsPermissionGranted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExactAlarmPermissionGranted = table.Column<bool>(type: "INTEGER", nullable: true),
                    BatteryOptimizationIgnored = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotificationHealth = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LastNotificationTestAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CurrentUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dispositivos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Grupos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    GrantsAllSectors = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grupos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogAlteracoes",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    SectorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogAlteracoes", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "Marcadores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Marcadores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperacoesProcessadas",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ResultingVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacoesProcessadas", x => x.OperationId);
                });

            migrationBuilder.CreateTable(
                name: "Permissoes",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissoes", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Setores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ShiftStart = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    ShiftEnd = table.Column<TimeOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Setores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TiposChecklist",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposChecklist", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CredenciaisClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredenciaisClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CredenciaisClaims_Credenciais_UserId",
                        column: x => x.UserId,
                        principalTable: "Credenciais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CredenciaisLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredenciaisLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_CredenciaisLogins_Credenciais_UserId",
                        column: x => x.UserId,
                        principalTable: "Credenciais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CredenciaisTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredenciaisTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_CredenciaisTokens_Credenciais_UserId",
                        column: x => x.UserId,
                        principalTable: "Credenciais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Credenciais_UserId",
                        column: x => x.UserId,
                        principalTable: "Credenciais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrupoPermissoes",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PermissionKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrupoPermissoes", x => new { x.GroupId, x.PermissionKey });
                    table.ForeignKey(
                        name: "FK_GrupoPermissoes_Grupos_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Grupos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrupoSetores",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SectorId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrupoSetores", x => new { x.GroupId, x.SectorId });
                    table.ForeignKey(
                        name: "FK_GrupoSetores_Grupos_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Grupos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GrupoSetores_Setores_SectorId",
                        column: x => x.SectorId,
                        principalTable: "Setores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Leitos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SectorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leitos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leitos_Setores_SectorId",
                        column: x => x.SectorId,
                        principalTable: "Setores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sessoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SectorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessoes_Setores_SectorId",
                        column: x => x.SectorId,
                        principalTable: "Setores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ColunasChecklist",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChecklistTemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TriggerTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotificationEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LeadTimeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    GracePeriodMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    RepeatIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    MaximumRepeats = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowSnooze = table.Column<bool>(type: "INTEGER", nullable: false),
                    SnoozeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColunasChecklist", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ColunasChecklist_TiposChecklist_ChecklistTemplateId",
                        column: x => x.ChecklistTemplateId,
                        principalTable: "TiposChecklist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TipoChecklistSetores",
                columns: table => new
                {
                    ChecklistTemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SectorId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TipoChecklistSetores", x => new { x.ChecklistTemplateId, x.SectorId });
                    table.ForeignKey(
                        name: "FK_TipoChecklistSetores_Setores_SectorId",
                        column: x => x.SectorId,
                        principalTable: "Setores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TipoChecklistSetores_TiposChecklist_ChecklistTemplateId",
                        column: x => x.ChecklistTemplateId,
                        principalTable: "TiposChecklist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsuarioGrupos",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuarioGrupos", x => new { x.UserId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_UsuarioGrupos_Grupos_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Grupos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UsuarioGrupos_Usuarios_UserId",
                        column: x => x.UserId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessaoLeitoMarcadores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BedId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MarkerDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessaoLeitoMarcadores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessaoLeitoMarcadores_Leitos_BedId",
                        column: x => x.BedId,
                        principalTable: "Leitos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessaoLeitoMarcadores_Marcadores_MarkerDefinitionId",
                        column: x => x.MarkerDefinitionId,
                        principalTable: "Marcadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessaoLeitoMarcadores_Sessoes_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessaoLeitos",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BedId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActiveInSession = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessaoLeitos", x => new { x.SessionId, x.BedId });
                    table.ForeignKey(
                        name: "FK_SessaoLeitos_Leitos_BedId",
                        column: x => x.BedId,
                        principalTable: "Leitos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessaoLeitos_Sessoes_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Marcacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BedId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChecklistTemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChecklistColumnId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Marcacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Marcacoes_ColunasChecklist_ChecklistColumnId",
                        column: x => x.ChecklistColumnId,
                        principalTable: "ColunasChecklist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Marcacoes_Leitos_BedId",
                        column: x => x.BedId,
                        principalTable: "Leitos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Marcacoes_Sessoes_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColunasChecklist_ChecklistTemplateId_DisplayName",
                table: "ColunasChecklist",
                columns: new[] { "ChecklistTemplateId", "DisplayName" },
                unique: true,
                filter: "\"IsActive\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ColunasChecklist_ChecklistTemplateId_SortOrder",
                table: "ColunasChecklist",
                columns: new[] { "ChecklistTemplateId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Configuracoes_Key",
                table: "Configuracoes",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "Credenciais",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "Credenciais",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CredenciaisClaims_UserId",
                table: "CredenciaisClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CredenciaisLogins_UserId",
                table: "CredenciaisLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispositivos_LastSeenAtUtc",
                table: "Dispositivos",
                column: "LastSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Grupos_Name",
                table: "Grupos",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrupoSetores_SectorId",
                table: "GrupoSetores",
                column: "SectorId");

            migrationBuilder.CreateIndex(
                name: "IX_Leitos_SectorId_Code",
                table: "Leitos",
                columns: new[] { "SectorId", "Code" },
                unique: true,
                filter: "\"IsActive\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_LogAlteracoes_SectorId_Sequence",
                table: "LogAlteracoes",
                columns: new[] { "SectorId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_LogAlteracoes_Sequence",
                table: "LogAlteracoes",
                column: "Sequence");

            migrationBuilder.CreateIndex(
                name: "IX_Marcacoes_BedId",
                table: "Marcacoes",
                column: "BedId");

            migrationBuilder.CreateIndex(
                name: "IX_Marcacoes_Celula",
                table: "Marcacoes",
                columns: new[] { "SessionId", "BedId", "ChecklistTemplateId", "ChecklistColumnId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Marcacoes_ChecklistColumnId",
                table: "Marcacoes",
                column: "ChecklistColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_Marcacoes_SessionId_ChecklistColumnId",
                table: "Marcacoes",
                columns: new[] { "SessionId", "ChecklistColumnId" });

            migrationBuilder.CreateIndex(
                name: "IX_Marcadores_Code",
                table: "Marcadores",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperacoesProcessadas_ProcessedAtUtc",
                table: "OperacoesProcessadas",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAtUtc",
                table: "RefreshTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessaoLeitoMarcadores_BedId",
                table: "SessaoLeitoMarcadores",
                column: "BedId");

            migrationBuilder.CreateIndex(
                name: "IX_SessaoLeitoMarcadores_MarkerDefinitionId",
                table: "SessaoLeitoMarcadores",
                column: "MarkerDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessaoLeitoMarcadores_Unico",
                table: "SessaoLeitoMarcadores",
                columns: new[] { "SessionId", "BedId", "MarkerDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessaoLeitos_BedId",
                table: "SessaoLeitos",
                column: "BedId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessoes_Setor_Data_Aberta",
                table: "Sessoes",
                columns: new[] { "SectorId", "ServiceDate" },
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_Setores_Name",
                table: "Setores",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TipoChecklistSetores_SectorId",
                table: "TipoChecklistSetores",
                column: "SectorId");

            migrationBuilder.CreateIndex(
                name: "IX_TiposChecklist_Code",
                table: "TiposChecklist",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsuarioGrupos_GroupId",
                table: "UsuarioGrupos",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_UserName",
                table: "Usuarios",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracaoNotificacoes");

            migrationBuilder.DropTable(
                name: "Configuracoes");

            migrationBuilder.DropTable(
                name: "CredenciaisClaims");

            migrationBuilder.DropTable(
                name: "CredenciaisLogins");

            migrationBuilder.DropTable(
                name: "CredenciaisTokens");

            migrationBuilder.DropTable(
                name: "Dispositivos");

            migrationBuilder.DropTable(
                name: "GrupoPermissoes");

            migrationBuilder.DropTable(
                name: "GrupoSetores");

            migrationBuilder.DropTable(
                name: "LogAlteracoes");

            migrationBuilder.DropTable(
                name: "Marcacoes");

            migrationBuilder.DropTable(
                name: "OperacoesProcessadas");

            migrationBuilder.DropTable(
                name: "Permissoes");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "SessaoLeitoMarcadores");

            migrationBuilder.DropTable(
                name: "SessaoLeitos");

            migrationBuilder.DropTable(
                name: "TipoChecklistSetores");

            migrationBuilder.DropTable(
                name: "UsuarioGrupos");

            migrationBuilder.DropTable(
                name: "ColunasChecklist");

            migrationBuilder.DropTable(
                name: "Credenciais");

            migrationBuilder.DropTable(
                name: "Marcadores");

            migrationBuilder.DropTable(
                name: "Leitos");

            migrationBuilder.DropTable(
                name: "Sessoes");

            migrationBuilder.DropTable(
                name: "Grupos");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "TiposChecklist");

            migrationBuilder.DropTable(
                name: "Setores");
        }
    }
}
