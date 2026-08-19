# Registro de decisões arquiteturais

Formato: cada decisão tem contexto, decisão, consequências e status. Decisões revogadas não são
apagadas — recebem status `Substituída por D-xxx` para preservar o histórico do raciocínio.

---

## D-001 — Plataforma única .NET 10

**Contexto.** O ambiente tem os SDKs 9.0.309 e 10.0.201 instalados, com workloads MAUI
(`android` 36.1.30, `maui-windows` 10.0.20) no banda de manifesto `10.0.100`.

**Decisão.** Todos os projetos usam `net10.0`; o head MAUI usa `net10.0-android` e
`net10.0-windows10.0.19041.0`. `global.json` fixa o SDK em `10.0.201` com `rollForward: latestFeature`.

**Consequências.** Sem mistura de TFMs. Máquinas de desenvolvimento precisam do SDK 10.0.2xx.

**Status.** Aceita.

---

## D-002 — Horários reais das colunas (confirmado com o cliente)

**Contexto.** A folha original nomeia colunas como "Jantar", "Café", "PM" e "AM", sem hora. Notificações
precisam de hora real. O enunciado proíbe presumir esses valores silenciosamente.

**Decisão.** Seeds explícitos e editáveis no painel administrativo:

| Template  | Coluna | Horário |
|-----------|--------|---------|
| Gelo      | 20H    | 20:00   |
| Gelo      | 22H    | 22:00   |
| Gelo      | 00H    | 00:00   |
| Gelo      | 02H    | 02:00   |
| Gelo      | 04H    | 04:00   |
| Gelo      | 06H    | 06:00   |
| Glicemia  | Jantar | 19:30   |
| Glicemia  | Café   | 07:00   |
| SSVV      | PM     | 20:00   |
| SSVV      | AM     | 06:00   |

**Consequências.** As notificações funcionam desde a primeira execução. O administrador pode alterar
qualquer horário; a alteração é sincronizada e os dispositivos reagendam.

**Status.** Aceita (confirmada pelo cliente).

---

## D-003 — Janela do plantão 19:00 → 07:00, configurável (confirmado com o cliente)

**Contexto.** O checklist Gelo atravessa a meia-noite (20H … 06H). Sem uma janela de turno definida, não
há como decidir a que dia pertence a coluna "02H".

**Decisão.** A sessão operacional representa um plantão com início e fim configuráveis (padrão
19:00 → 07:00). `ServiceDate` é o dia em que o plantão começou. Uma coluna cujo `TriggerTime` é anterior
ao horário de início do turno resolve para `ServiceDate + 1 dia`.

**Consequências.** 20H e 22H caem no dia da sessão; 00H, 02H, 04H, 06H caem no dia seguinte. A regra é
genérica: vale para qualquer coluna criada depois, sem código específico por nome.

**Status.** Aceita (confirmada pelo cliente).

---

## D-004 — Projeto adicional `ChecklistPlantao.Client.Core`

**Contexto.** A estrutura mínima exigida coloca banco local, Outbox, sincronização e notificações dentro
do projeto MAUI. Os TFMs de MAUI não executam `dotnet test` nem as ferramentas do EF Core.

**Decisão.** Criar `src/ChecklistPlantao.Client.Core` em `net10.0` puro com: `LocalDbContext` + migrations,
Outbox, motor de push/pull, autenticação offline, snapshot de permissões e resolução de horários de
notificação. O projeto MAUI fica responsável apenas pelo que é específico de plataforma.

**Consequências.** Toda a regra crítica de offline/sincronização/notificação é testável neste ambiente
mesmo que os heads MAUI não compilem. O enunciado pede a estrutura "no mínimo" listada — acrescentar é
permitido; nenhum projeto exigido foi removido.

**Status.** Aceita.

---

## D-005 — Identity confinado ao servidor

**Contexto.** O enunciado coloca Identity em `Infrastructure`. Se o cliente MAUI referenciasse
`Infrastructure`, o aplicativo Android carregaria `Microsoft.AspNetCore.Identity.EntityFrameworkCore`.

**Decisão.** `Infrastructure` é referenciado apenas por `Server` e pelos testes de integração. A
persistência local do cliente vive em `Client.Core`, com seu próprio `DbContext` e sem Identity.

**Consequências.** O hash do Identity nunca sai do servidor (requisito de segurança do item 8). O
aplicativo fica menor. Duas configurações de EF Core coexistem, com o modelo de domínio compartilhado.

**Status.** Aceita.

---

## D-006 — Central Package Management, com exceção para o head MAUI

**Contexto.** O SDK do MAUI injeta `PackageReference` implícitos usando `$(MauiVersion)`, o que conflita
com Central Package Management (erro NU1008).

**Decisão.** `Directory.Packages.props` na raiz fixa todas as versões. O projeto
`ChecklistPlantao.Client` define `ManagePackageVersionsCentrally=false` e deixa o workload escolher a
versão do MAUI.

**Consequências.** Uma única exceção, documentada, em vez de espalhar `VersionOverride`.

**Status.** Aceita.

---

## D-007 — Pinos de segurança sobre dependências transitivas

**Contexto.** O restore inicial reportou dois avisos NU1903 de severidade alta:
`SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (GHSA-2m69-gcr7-jv3q, SQLite vulnerável em `<= 2.1.11`) e
`Microsoft.OpenApi` 2.0.0 (GHSA-v5pm-xwqc-g5wc, corrigido em 2.7.5), ambos trazidos transitivamente
por EF Core 10.0.10 e ASP.NET Core OpenApi 10.0.10.

**Decisão.** Ligar `CentralPackageTransitivePinningEnabled` e fixar
`SQLitePCLRaw.*` em `2.1.12` e `Microsoft.OpenApi` em `2.11.0`.

**Consequências.** Restore limpo, sem avisos. Reavaliar os pinos quando o EF Core / ASP.NET Core
passarem a trazer versões já corrigidas — nesse momento os pinos podem ser removidos.

**Status.** Aceita.

---

## D-008 — Sem `GenericRepository`

**Contexto.** O enunciado proíbe explicitamente abstrações sem utilidade.

**Decisão.** Serviços de aplicação conversam com o `DbContext` através de interfaces estreitas
(`IServerDataContext`, `ILocalDataContext`) que expõem apenas os conjuntos necessários. Repositórios
específicos só onde a consulta justifique (leitura agregada do checklist).

**Consequências.** Menos indireção, testes de aplicação usam SQLite em memória em vez de mocks de
repositório.

**Status.** Aceita.

---

## D-009 — Dependências externas mínimas

**Contexto.** O enunciado exige justificar cada pacote e evitar CDN e bibliotecas visuais pesadas.

**Decisão.** Não usar FluentValidation (validadores próprios na Application), FluentAssertions (a v8
passou a exigir licença comercial — usar os asserts do xUnit), Polly (backoff exponencial próprio),
AutoMapper (projeções explícitas) nem biblioteca de mock (fakes escritos à mão). Bootstrap do template
foi removido: o design system é CSS próprio, servido localmente.

**Consequências.** Menos superfície de atualização e nenhum asset remoto — requisito de funcionamento
offline. Um pouco mais de código próprio, todo ele coberto por teste.

**Status.** Aceita.

---

## D-010 — Notificações no Windows dependem do app em execução (confirmado com o cliente)

**Contexto.** Agendar toasts no Windows para disparar com o aplicativo fechado exige empacotamento MSIX,
que por sua vez exige o Windows 10 SDK — ausente nesta máquina — além de certificado de assinatura.

**Decisão.** No Windows, um agendador *in-process* dispara toasts nativos enquanto o aplicativo estiver
aberto (inclusive minimizado). A limitação aparece na tela "Estado do dispositivo" e em
`docs/KNOWN_LIMITATIONS.md`. A interface `ILocalNotificationScheduler` foi desenhada para receber uma
implementação MSIX depois sem alterar o restante.

**Consequências.** No Windows o alerta é confiável apenas com o aplicativo aberto. No Android, o
agendamento é real (AlarmManager) e sobrevive ao fechamento do app e ao reinício do aparelho.

**Status.** Aceita (confirmada pelo cliente).

---

## D-011 — Formato de solução `.sln` clássico

**Contexto.** O SDK 10 gera `.slnx` por padrão; o enunciado pede `ChecklistPlantao.sln`.

**Decisão.** Usar o formato clássico. Adicionalmente existe `ChecklistPlantao.NoMaui.slnf`, um filtro de
solução sem o head MAUI, para acelerar o ciclo de build e teste do servidor e das bibliotecas.

**Consequências.** Compatível com toda a tooling. O filtro é conveniência, não substitui a solução.

**Status.** Aceita.

---

## D-013 — Credenciais e autorização em tabelas separadas

**Contexto.** O enunciado pede ASP.NET Identity, mas o domínio precisa ficar livre de infraestrutura
e o `PasswordHash` do Identity nunca pode sair do servidor.

**Decisão.** Duas entidades com o mesmo `Id`: `AppIdentityUser` (tabela `Credenciais`, só credenciais e
bloqueio) e `AppUser` (tabela `Usuarios`, nome de exibição, ativo e grupos). São escritas na mesma
unidade de trabalho por `AccessAdminService`. A camada de aplicação enxerga apenas
`IUserCredentialStore`, que **não expõe nenhum método para ler o hash**.

**Consequências.** O domínio permanece sem dependência de Identity e o cliente MAUI nunca carrega
`Microsoft.AspNetCore.Identity`. O custo é manter as duas linhas em sincronia, o que acontece em um
único ponto do código.

**Status.** Aceita.

---

## D-014 — EF Core na camada Application

**Contexto.** Os casos de uso precisam consultar o banco. Sem acesso ao `DbContext` seria necessário
um repositório por consulta — exatamente a abstração inútil que o enunciado proíbe.

**Decisão.** `Application` referencia `Microsoft.EntityFrameworkCore` (nenhum provedor) e define
`IAppDataContext`, a superfície reduzida do `DbContext`. Consultas que o provedor relacional não
traduz (log de alterações, idempotência) ficam em `Infrastructure`, atrás de métodos da interface.

**Consequências.** Consultas LINQ legíveis e testáveis contra SQLite real. A RCL **não** referencia
`Application`, então a interface continua livre de EF Core; ela declara suas próprias abstrações de
apresentação, que `Client.Core` implementa.

**Status.** Aceita.

---

## D-015 — Configuração sempre pelo contêiner, nunca lida antes de `Build()`

**Contexto.** A primeira versão do `Program.cs` fazia
`builder.Configuration.GetSection("Jwt").Get<JwtOptions>()` na hora de registrar os serviços, e
`AddChecklistInfrastructure` fazia o mesmo com `DatabaseOptions`. Fontes de configuração
acrescentadas depois desse ponto — o que `WebApplicationFactory` faz, e o que qualquer provedor
tardio faria — eram simplesmente ignoradas.

O sintoma foi grave e silencioso: o servidor **assinava** os tokens com a chave das opções resolvidas
pelo contêiner e os **validava** com a chave lida antecipadamente, devolvendo 401 em todo endpoint
protegido; e todos os testes de integração abriam o mesmo arquivo de banco em vez de bancos
temporários isolados, contaminando-se entre execuções.

**Decisão.** Nenhum ponto do servidor lê a configuração antes de `Build()`.
- JWT: `ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>` recebe `IOptions<JwtOptions>`.
- Banco: `AddDbContext` usa a sobrecarga com `IServiceProvider` e resolve `IOptions<DatabaseOptions>`.
- Bloqueio do Identity: `AddOptions<IdentityOptions>().Configure<IOptions<LoginLockoutOptions>>(...)`.
- Limites de requisição: lidos por requisição, de `context.RequestServices`.

**Consequências.** As opções passam a valer independentemente de quando a fonte foi registrada. É a
regra a seguir ao acrescentar qualquer configuração nova.

**Status.** Aceita.

---

## D-016 — Índices únicos filtrados por estado ativo

**Contexto.** Impedir dois leitos com o mesmo código no mesmo setor é requisito. Mas um leito
desativado não deve travar o cadastro de um novo com o mesmo código.

**Decisão.** Índices únicos parciais (`HasFilter("\"IsActive\" = 1")`) em `Leitos (SectorId, Code)` e
`ColunasChecklist (ChecklistTemplateId, DisplayName)`; e em `Sessoes (SectorId, ServiceDate)` filtrado
por `Status = 'Open'`, o que garante no máximo uma sessão aberta por setor.

**Consequências.** A regra é imposta pelo banco, não só pelo código de aplicação — duas requisições
concorrentes não conseguem burlar.

**Status.** Aceita.

---

## D-017 — Banco local do aparelho sem migrations

**Contexto.** O servidor usa EF Core Migrations. Repetir isso no aparelho significaria carregar o
histórico de migrations dentro do aplicativo.

**Decisão.** O banco local usa `EnsureCreated`. Quando o esquema mudar entre versões do aplicativo,
o banco é recriado e repovoado pelo bootstrap.

**Como a mudança é detectada.** `EnsureCreated` cria o esquema se o arquivo não existir e **não faz
nada** se ele já existir — sozinho, deixaria o aparelho já instalado com a tabela velha, falhando em
uso com erro obscuro. A detecção usa `PRAGMA user_version`, que mora no cabeçalho do arquivo SQLite
e não numa tabela, evitando o problema circular de guardar a versão dentro do esquema que se quer
versionar. A constante é `LocalDbContext.LocalSchemaVersion`, **incrementada à mão** a cada mudança
nas entidades locais; a subida compara e recria quando divergem.

**Consequências.** Aplicativo menor e mais simples. O custo é que uma atualização com mudança de
esquema descarta o que ainda estiver na fila de envio — por isso a atualização deve ser feita com
os aparelhos sincronizados, o que está registrado em `docs/DEPLOYMENT.md`. Cadastros e marcações
já sincronizados voltam do servidor. Quantas alterações se perderam vai para o log, em nível de
aviso.

**O que foi considerado e recusado.** Tentar enviar a fila antes de descartar. A subida do cliente é
deliberadamente síncrona — bloquear nela para esperar rede contraria a própria regra do projeto
sobre `.Result`/`.Wait()`, e um aplicativo que demora para abrir por causa de um servidor lento é
pior que a perda registrada. O caminho seguro continua sendo atualizar com os aparelhos
sincronizados.

**Status.** Aceita.

---

## D-018 — Sessão local com identificador determinístico

**Contexto.** Sem servidor, o aparelho precisa poder abrir o plantão para o checklist funcionar.
Se cada aparelho gerasse um Id aleatório, dois aparelhos offline criariam duas sessões diferentes
para o mesmo setor e a mesma data.

**Decisão.** A sessão criada offline usa `DeterministicGuid.From("session:{setor}:{data}")`. Dois
aparelhos offline chegam ao mesmo identificador, e ao sincronizar convergem para a mesma sessão.

**Consequências.** Nenhum plantão duplicado. Se o servidor já tiver criado a sessão com outro Id, o
aparelho adota a do servidor na primeira sincronização — ele é a autoridade.

**Status.** Aceita.

---

## D-019 — Quem está usando o aplicativo é estado de aplicativo, não de escopo

**Contexto.** `ClientSession` depende do `LocalDbContext`, que tem tempo de vida por escopo — logo a
sessão também precisa ser por escopo. Só que ela guardava o estado de autenticação nos próprios
campos. Como os serviços singleton (sincronização, reagendamento de notificações) abrem escopos
próprios, cada escopo passou a ter a sua verdade: resolver `IAppSession` fora do escopo do WebView
devolvia uma instância **nova, não autenticada**. No aparelho isso aparecia como "tocar em Painel
volta para a tela de entrada", com o usuário logado o tempo todo, e a navegação sumindo junto.

**Decisão.** O estado — identidade, permissões, setor escolhido, momento da última validação pelo
servidor — vive em `AuthenticatedSessionState`, registrado como **singleton**. `ClientSession`
continua por escopo, faz o trabalho que depende do banco e delega todo o estado. O evento `Changed`
também mora no singleton: quem assina é a interface, cujo escopo pode não ser o de quem fez a
entrada — assinar na instância errada seria assinar o silêncio.

**Consequências.** O escopo deixa de importar para a pergunta "quem está usando o aplicativo".
`ServiceGraphTests` fixa a regra com dois casos: a sessão vista por dois escopos é a mesma, e o
aviso de mudança atravessa escopos. O singleton guarda apenas o que já estava em memória — nenhum
segredo novo, e nada de token, que continua no armazenamento seguro da plataforma.

**Status.** Aceita.

---

## D-020 — "Não verificado" não é "com problema"

**Contexto.** A faixa de saúde das notificações aparecia com o texto *"O estado das notificações
ainda não foi verificado"* a cada abertura de tela, porque a ausência de medição era tratada como
defeito. Alarme constante e sem ação possível ensina o plantão a ignorar a faixa — inclusive quando
ela estiver certa.

**Decisão.** `NotificationStatus` passou a carregar `HasBeenChecked`. `IsHealthy` exige medição e
ausência de problemas; `HasProblems` exige medição e problema real. A faixa aparece só em
`HasProblems`. O estado inicial (`Unknown`) não é nem saudável nem problemático — é silêncio.

**Consequências.** Coerente com a regra que o sistema segue em toda parte: não afirmar o que não foi
medido (a mesma correção feita no rótulo "Offline" da tela de entrada e no texto da economia de
bateria). O usuário pediu originalmente um cache de 1 h para o aviso; distinguir os estados resolve
a causa em vez do sintoma, e o cache permanece disponível como recurso caso a faixa ainda incomode.

**Status.** Aceita.

---

## D-021 — O rastreador do EF antes do banco, e limpo quando a gravação falha

**Contexto.** Duas falhas no aparelho tinham a mesma origem. A primeira: *"The instance of entity
type 'ChecklistEntry' cannot be tracked because another instance with the same key value is already
being tracked"* ao abrir o setor. A segunda: `DbUpdateException` ao entrar, em **toda** tentativa,
até reinstalar o aplicativo.

A causa comum tem duas metades:

1. **A consulta ao banco não enxerga o que só existe no rastreador.** Uma entidade adicionada
   momentos antes, ainda não gravada, não aparece num `SELECT`; o código conclui que ela não existe
   e adiciona outra com a mesma chave. É o mesmo defeito que derrubava a sincronização em lote no
   servidor, corrigido em `ChecklistMutationService`.

2. **No MAUI Blazor Hybrid o escopo do `BlazorWebView` dura a vida inteira do aplicativo.** Serviços
   registrados como *scoped* — incluindo o `LocalDbContext` — nunca são descartados. Então uma
   gravação que falha deixa as entidades presas no rastreador **para sempre**, e a falha passa a se
   repetir em toda operação seguinte, mesmo nas que nada têm a ver com a primeira. No aparelho isso
   apareceu como o arquivo `-wal` parado no mesmo tamanho por horas: nenhuma escrita concluía.

**Decisão.** Duas regras, uma para cada metade:

- **`IDbContextFactory<LocalDbContext>` no lugar de `AddDbContext`.** Cada unidade de trabalho abre
  e descarta o seu próprio contexto: marcar um leito, entrar, abrir o quadro, um ciclo de
  sincronização. É o padrão que a documentação do Blazor recomenda, exatamente por causa deste
  tempo de vida de escopo. `OutboxWriter`, `SyncEngine`, `ClientSession`, `LocalChecklistStore`,
  `ServerConfigurationService`, `NotificationStatusService` e `DeviceDiagnosticsService` passaram a
  recebê-la.
- **`DbSet.Local` antes do banco** em toda busca que antecede um `Add`. Um contexto novo não
  resolve isto sozinho: dentro de uma mesma unidade de trabalho, uma entidade adicionada momentos
  antes continua invisível para um `SELECT`.

Essas classes ganharam um segundo construtor que recebe um contexto **emprestado**, para quem já
abriu uma unidade de trabalho (o motor de sincronização, e os testes). O contêiner não sabe escolher
entre construtores de mesma aridade, então o registro em `DependencyInjection` é explícito — e é bom
que a escolha fique visível ali, e não escondida numa regra de resolução.

**Consequências.** Uma falha morre junto com o contexto dela: deixou de contaminar as operações
seguintes. O rastreador não acumula mais o plantão inteiro, e duas operações simultâneas não
compartilham mais a mesma instância — que nunca foi segura para isso, e era a origem do
`Unexpected entry.EntityState: Detached`.

Um cuidado que veio junto: entidades **não podem ser guardadas em campo** entre operações, porque
pertencem ao contexto que as leu. `ClientSession` deixou de cachear o `DeviceState`, e
`ServerConfigurationService` passou a cachear um registro de valores em vez da entidade.

**Status.** Aceita.

---

## D-012 — Administrador inicial sem senha no repositório

**Contexto.** O enunciado proíbe senha padrão no código.

**Decisão.** O seed cria o grupo Administradores e as permissões, mas só cria o usuário administrador se
`Bootstrap:AdminUserName` e `Bootstrap:AdminPassword` estiverem presentes na configuração (variáveis de
ambiente ou User Secrets). Sem esses valores o servidor sobe e registra um aviso explicando o que fazer.

**Consequências.** Nenhuma credencial no repositório. O procedimento está em `docs/DEPLOYMENT.md`.

**Status.** Aceita.
