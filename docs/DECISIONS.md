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

## D-012 — Administrador inicial sem senha no repositório

**Contexto.** O enunciado proíbe senha padrão no código.

**Decisão.** O seed cria o grupo Administradores e as permissões, mas só cria o usuário administrador se
`Bootstrap:AdminUserName` e `Bootstrap:AdminPassword` estiverem presentes na configuração (variáveis de
ambiente ou User Secrets). Sem esses valores o servidor sobe e registra um aviso explicando o que fazer.

**Consequências.** Nenhuma credencial no repositório. O procedimento está em `docs/DEPLOYMENT.md`.

**Status.** Aceita.
