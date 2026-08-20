# Modelo de dados

Dois bancos SQLite, com as **mesmas entidades de domínio** e o que é específico de cada lado.

## Servidor — `AppDbContext`

| Tabela | Entidade | Observações |
|---|---|---|
| `Credenciais` (+ Claims/Logins/Tokens) | `AppIdentityUser` | ASP.NET Identity. Só credenciais e bloqueio |
| `Usuarios` | `AppUser` | Autorização. Mesmo `Id` da credencial. `UserName` único |
| `Grupos` | `AccessGroup` | Nome único; `GrantsAllSectors` |
| `UsuarioGrupos` | `UserGroup` | PK composta |
| `GrupoPermissoes` | `GroupPermission` | PK composta |
| `GrupoSetores` | `GroupSectorAccess` | PK composta |
| `Permissoes` | `PermissionDefinition` | Catálogo semeado |
| `Setores` | `Sector` | Nome único; turno próprio opcional |
| `Leitos` | `Bed` | **Único filtrado:** `(SectorId, Code)` onde `IsActive = 1` |
| `TiposChecklist` | `ChecklistTemplate` | `Code` único e estável |
| `TipoChecklistSetores` | `ChecklistTemplateSector` | Sem linhas = vale para todos os setores |
| `ColunasChecklist` | `ChecklistColumn` | **Único filtrado:** `(TemplateId, DisplayName)` onde ativo |
| `Marcadores` | `BedMarkerDefinition` | `Code` único |
| `Sessoes` | `OperationalSession` | **Único filtrado:** `SectorId` onde `Status = 'Open'` |
| `SessaoLeitos` | `SessionBed` | PK composta; snapshot operacional dos leitos na abertura |
| `Marcacoes` | `ChecklistEntry` | **Único:** `(SessionId, BedId, TemplateId, ColumnId)`. **Sem coluna de usuário** |
| `SessaoLeitoMarcadores` | `SessionBedMarker` | **Único:** `(SessionId, BedId, MarkerDefinitionId)` |
| `ConfiguracaoNotificacoes` | `NotificationConfiguration` | Uma linha, Id fixo |
| `Dispositivos` | `DeviceRegistration` | Último estado **sincronizado** |
| `Configuracoes` | `AppSetting` | Chave/valor; `Key` único |
| `RefreshTokens` | `RefreshToken` | Só o hash; índice em `TokenHash` e `ExpiresAtUtc` |
| `LogAlteracoes` | `ChangeLogEntry` | `Sequence` autoincremento — é o cursor |
| `OperacoesProcessadas` | `ProcessedOperation` | `OperationId` PK — base da idempotência |

## Aparelho — `LocalDbContext`

Espelha a configuração e a operação, e acrescenta:

| Tabela | Entidade | Para quê |
|---|---|---|
| `FilaEnvio` | `SyncOutboxItem` | Alterações aguardando envio. `OperationId` único |
| `EstadoSincronizacao` | `SyncState` | Cursor, últimos contatos, bootstrap concluído |
| `CredenciaisLocais` | `LocalCredential` | Sal + verificador PBKDF2 + snapshot de permissões |
| `EstadoDispositivo` | `DeviceState` | Id, nome, servidor, setor atual, estado das notificações |

O banco local **não** tem tabelas de Identity, log de alterações nem operações processadas: essas
são responsabilidades do servidor.

## Índices que impõem regra

| Índice | Impede |
|---|---|
| `Leitos (SectorId, Code)` filtrado por ativo | Dois leitos ativos com o mesmo código no setor |
| `ColunasChecklist (TemplateId, DisplayName)` filtrado por ativo | Duas colunas ativas homônimas no mesmo tipo |
| `IX_Marcacoes_Celula` | Duas entradas para a mesma célula |
| `IX_SessaoLeitoMarcadores_Unico` | Marcador duplicado no mesmo leito e sessão |
| `IX_Sessoes_Setor_Aberta` em `SectorId`, filtrado por `Status = 'Open'` | Duas sessões abertas no mesmo setor, mesmo com datas diferentes |
| `Usuarios (UserName)` | Usuário duplicado |
| `UsuarioGrupos (UserId, GroupId)` | Associação duplicada |

Filtrados por estado ativo porque um item desativado não deve travar o cadastro de um novo com o
mesmo código.

## Convenções

- **UTC** em toda persistência técnica. Conversão para o fuso da instituição só na exibição e no
  agendamento.
- **`DateOnly`** para `ServiceDate`, **`TimeOnly`** para `TriggerTime` e para a janela do plantão.
- **`Guid.CreateVersion7()`** em tempo de execução — ordenável no tempo, melhor para índice.
- **`DeterministicGuid`** só para dados de seed: servidor e aparelhos chegam ao mesmo Id sem
  negociar, e rodar o seed duas vezes não duplica nada.
- Enums persistidos como **texto**: o log continua legível em diagnóstico e acrescentar um valor
  não renumera os demais.

## Migrations

Servidor: EF Core Migrations em `Infrastructure/Persistence/Migrations`, aplicadas na subida.

Aparelho: `EnsureCreated`. O banco local é reconstituível a partir do bootstrap; carregar histórico
de migrations no aparelho não se justifica (D-017).

## SQLite no servidor

Chaves estrangeiras ligadas, WAL habilitado na subida, `synchronous=NORMAL`, timeout de comando
configurável, e o arquivo em volume persistente — **nunca** em pasta temporária.
