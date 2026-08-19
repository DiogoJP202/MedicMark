# Estado da implementação

Última atualização: 2026-08-18.

## Estados usados

| Estado | Significado |
|---|---|
| **Implementado** | Código escrito e compilando |
| **Parcialmente implementado** | Funciona, com ressalva declarada |
| **Não iniciado** | Não existe |
| **Bloqueado pelo ambiente** | Escrito, mas impossível de exercitar aqui |
| **Validado por teste automatizado** | Há teste cobrindo, e ele passa |
| **Validado manualmente** | Exercitado à mão neste ambiente |
| **Não validado** | Compila, mas ninguém executou |

---

## Builds e testes executados

| Comando | Resultado |
|---|---|
| `dotnet build ChecklistPlantao.sln -c Release` | ✅ 0 erros, **0 avisos** — inclui os dois heads MAUI |
| `dotnet build ChecklistPlantao.NoMaui.slnf -c Release` | ✅ 0 erros, 0 avisos |
| `dotnet test ChecklistPlantao.NoMaui.slnf -c Release` | ✅ **412 testes, 0 falhas** |
| `dotnet build -f net10.0-android` | ✅ compila |
| `dotnet build -f net10.0-windows10.0.19041.0` | ✅ compila |
| `dotnet restore` | ✅ sem avisos de vulnerabilidade |

### Testes por projeto

| Projeto | Testes | O que cobre |
|---|---|---|
| Domain.Tests | 114 | Turno, permissões, retenção, conflito, agendamento, seeds, **nome único de coluna** |
| UI.Tests (bUnit) | 121 | Componentes, filtros, faixas, desvio da primeira execução, estado da conexão no login, contenção de falha de tela, **modais de cadastro e hierarquia da administração** |
| Client.Core.Tests | 86 | Persistência offline, fila, idempotência, conflito, auth offline, recusa do servidor, grafo de dependências real, sessão entre escopos e **versão do esquema local** |
| Server.IntegrationTests | 58 | API de ponta a ponta com servidor e SQLite reais, lote com repetição na mesma célula, cadastro de estrutura, o cliente HTTP real contra o servidor real e **o aviso em tempo real do hub até o cliente** |
| Application.Tests | 33 | Casos de uso, sessão, retenção, administração, **criação de coluna e restrição de tipo a setor** |

O `Server.IntegrationTests` passou a referenciar o `Client.Core`. Era o último ponto cego da
suíte: todo teste de cliente substituía `IServerApi` por um duplo, então o `HttpServerApi` nunca
falava com um servidor de verdade — montagem de URL, serialização de `TimeOnly`, envio do bearer e
a tradução de `ProblemDetails` na mensagem exibida na tela não tinham cobertura nenhuma.

### Validado em aparelho real — Xiaomi 23122PCD1G, Android 13 (API 33)

Primeira execução em dispositivo físico, com o servidor alcançado por túnel USB (`adb reverse`).

| Verificação | Resultado |
|---|---|
| Instalação e abertura do aplicativo | ✅ sem erro de inicialização |
| Composição de dependências no aparelho | ✅ **nenhum erro de dependência circular** |
| Configuração do servidor e "Testar conexão" | ✅ |
| Login online contra o servidor | ✅ |
| Bloqueio por tentativas (5 falhas → 423) | ✅ observado no servidor |
| HTTP em rede local a partir do Android 13 | ✅ após `network_security_config` |

**Cinco defeitos encontrados em campo** — nenhum deles aparecia nos testes automatizados, porque
todos dependiam do ambiente real (WebView, teclado do Android, barra de status, políticas do
fabricante):

| Defeito | Sintoma no aparelho | Correção |
|---|---|---|
| HTTP em texto claro bloqueado | App não alcançava o servidor local | `network_security_config.xml` permitindo texto claro na rede local |
| Conteúdo desenhado sob a barra de status | Título escondido e **o ✕ do aviso de notificações intocável** | `SetDecorFitsSystemWindows(true)` + área segura sem o gate exclusivo do iOS |
| Sem saída para a configuração | Endereço salvo, e nenhum caminho de volta a partir do login | Endereço e botão "Alterar" sempre visíveis na entrada |
| Motivo da recusa descartado | Servidor dizia "conta bloqueada" e o app dizia "você nunca entrou neste aparelho" | `ServerLoginResult` distingue "servidor recusou" de "servidor não respondeu" |
| Teclado alterando credenciais | "Usuário ou senha inválidos" sem causa aparente | `autocapitalize`/`autocorrect`/`spellcheck` desligados + botão "Mostrar senha" |

Também corrigido o texto do diagnóstico de bateria, que afirmava *"a economia de bateria está
ativa para este aplicativo"* quando a API apenas informa que o app **não está na lista de isenção**
— o estado padrão de qualquer instalação. O botão "Corrigir agora" passou a escolher a tela do
sistema pela pendência mais grave, incluindo o diálogo de isenção de bateria.

#### Segunda rodada em campo — defeitos de uso

Encontrados percorrendo o roteiro de teste no aparelho. Os dois primeiros são de conforto; os três
últimos impediam trabalho.

| Defeito | Sintoma no aparelho | Correção |
|---|---|---|
| Falha de tela derrubava a navegação | *"Ocorreu um erro inesperado"* com link simples, "Carregando…" infinito, e o menu sumia — sem saída a não ser reiniciar | `<ErrorBoundary>` no layout, **fora** da região que falha: a navegação permanece, com "Tentar de novo" e "Voltar ao início" |
| Toque repetido na mesma caixa | Marcava e desmarcava em sequência ao clicar rápido | Bloqueio de 1 s **apenas na célula tocada** — percorrer os leitos em sequência continua fluido |
| Sem filtro por marcador no checklist | C.I., Sondas e Drenos só existiam na tela de classificações | Linha de filtros no checklist, com a contagem de cada marcador |
| Classificações voltavam ao topo | Marcar um leito recarregava a lista inteira e perdia a rolagem | Atualização local da linha; recarga completa só em caso de conflito |
| Aviso de notificações a cada abertura | *"O estado das notificações ainda não foi verificado"* aparecia sempre | O estado passou a distinguir **não medido** de **com problema**; a faixa só aparece com problema real |
| Sessão perdida entre telas | Tocar em "Painel" às vezes voltava para a entrada, com o usuário logado | Estado de autenticação movido para `AuthenticatedSessionState` (singleton); a sessão por escopo deixou de ter verdade própria |

Um sexto defeito, no servidor, bloqueava a sincronização por completo:

| Defeito | Sintoma | Correção |
|---|---|---|
| `UNIQUE constraint failed` → **500** em `/api/sync/push` | Lote com várias operações para a **mesma célula**: a consulta ao banco não enxergava a entidade criada momentos antes na mesma unidade de trabalho, e o segundo `INSERT` colidia | Consulta a `.Local` antes do banco, em `ChecklistEntry` e `SessionBedMarker` |

Cobertos por `BatchSameCellTests`, `ErrorBoundaryTests`, `ServerConfigurationReachableTests`,
`LoginRefusalTests` e os dois casos de sessão entre escopos em `ServiceGraphTests`.

#### Pendente de validação manual

| Item | Como validar | Por que não foi feito |
|---|---|---|
| As seis correções da segunda rodada, no aparelho | Roteiro de teste, etapas 1–6 | Aparelho desconectado no momento da correção; compila e passa nos testes, **não reexecutado em campo** |
| Dois dispositivos ao mesmo tempo | Marcar em um e ver aparecer no outro | O cliente do hub foi implementado e tem teste de integração; falta ver acontecer entre dois aparelhos |
| Mensagem de conta bloqueada na tela | Errar a senha 5 vezes | Bloqueia a conta por 15 min; adiado a pedido |
| Isenção de bateria concedida | "Corrigir agora" → confirmar → "Verificar novamente" | Aguardando execução |
| Implantação em Docker e ciclo de backup | MANUAL_TEST_PLAN, seção 11 | Docker nunca executado neste ambiente |
| HTTPS com certificado confiável nos aparelhos | Seção 11 | Nunca exercitado |

### Terceira rodada validada em execução real — 18/08

Percorrido no **aparelho e no Windows**, com o servidor no ar:

| Verificação | Resultado |
|---|---|
| Cadastrar coluna, setor, leito e marcador pelos modais | ✅ |
| Três níveis do menu de administração | ✅ |
| Barra sem "Dispositivo"; cartão do Painel abre a tela | ✅ |
| Checklist em uso: marcar e desmarcar, celular e desktop | ✅ critérios 7, 8 e 9 |
| Botão "Testar alerta" | ✅ critério 21 |
| **Alerta disparando no horário da coluna** | ✅ critérios 20 e 25 — era o risco nº 1 do projeto |

### Aviso em tempo real — o hub que existia só de um lado

Descoberto ao preparar o teste de dois dispositivos, e corrigido em seguida.

O servidor tinha o `SyncHub` completo desde o início. O cliente nunca se conectava:
`Microsoft.AspNetCore.SignalR.Client` estava referenciado no `Client.Core.csproj` e **nenhum
arquivo do projeto abria uma `HubConnection`** — `SyncHubEvents` só aparecia no servidor. Marcar em
um aparelho não avisava o outro.

`RealtimeSyncClient` fecha isso: acompanha o estado da sessão, conecta quando há usuário e
endereço, ouve os cinco eventos e manda buscar. O aviso continua sem transportar estado.

A armadilha que o desenho escondia: `OnConnectedAsync` inscreve a conexão apenas nos
`ExplicitSectorIds`, e o grupo **Administradores** é semeado com `GrantsAllSectors: true` e
`SectorNames: []` — um administrador entraria em nenhum grupo de setor. É para isso que o
`SubscribeSector` existe, e o cliente o chama ao entrar no setor.

`RealtimeSyncTests` liga o cliente real ao hub real. O teste da armadilha foi conferido por
mutação: desativando a chamada a `SubscribeSector`, ele falha.

### Trocar de servidor trancava o aparelho para fora

Relatado em campo: o mesmo celular apontado para o servidor de **outra máquina**, e a entrada
passou a falhar com

```
SQLite Error 19: 'UNIQUE constraint failed: CredenciaisLocais.UserName'  [LocalCredential/Added]
```

Cada servidor gera o seu próprio identificador para `admin`. O aparelho procurava a credencial
local **por identificador**, não encontrava a do servidor novo, e inseria — colidindo com o índice
único de nome de usuário. Sem outra saída além de reinstalar o aplicativo.

A contenção funcionou como projetada: em vez de derrubar a tela, a entrada recusou e mostrou a
causa no detalhe técnico. Foi ela que permitiu diagnosticar em uma mensagem.

**Correção.** Quando não há credencial com aquele identificador, o aplicativo procura pelo **nome
de usuário**. Se existir uma, o servidor que acabou de autenticar é a autoridade: a antiga sai e a
nova entra. As credenciais de outros usuários não são tocadas.

Coberto por `LoginStorageFailureTests`, que passou a exercitar a falha de gravação por outro
caminho — um gatilho no banco — justamente porque a colisão de nome deixou de ser um erro.

### Defeitos encontrados depois da entrega inicial

Três falhas que os testes originais não pegavam, porque todos registravam os serviços à mão e
substituíam `IServerApi` por um duplo — a composição real do aplicativo nunca era exercitada:

| Defeito | Sintoma | Correção |
|---|---|---|
| Ciclo de dependência `IServerApi → ITokenStore → IServerApi` | Tela branca no aparelho: *"A circular dependency was detected"* | `IServerApi` resolvido sob demanda, não no construtor |
| `SyncStatusService` (singleton) segurando `IServerApi` (com escopo) | Dependência cativa | Resolvido por escopo em cada uso |
| Painel sem desvio na primeira execução | "Verificando o acesso…" para sempre, sem caminho para configurar o servidor | Desvio para `/configuracao` ou `/entrar` |

Também corrigido: a tela de login afirmava "Offline" a partir do estado inicial, **sem ter medido
nada** — exatamente o tipo de afirmação não verificada que o resto do sistema evita. Agora mede
antes de rotular.

`ServiceGraphTests` fecha a lacuna: constrói o contêiner **real** com `ValidateOnBuild` e
`ValidateScopes`, e resolve cada serviço que a interface injeta.

### Terceira rodada — administração

Relatados em uso real e corrigidos em 18/08.

| Defeito | Sintoma | Correção |
|---|---|---|
| **Coluna nova nunca gravava** | HTTP 500 ao salvar. `DbUpdateConcurrencyException`: a coluna era rastreada como `Modified` e o `UPDATE` não achava a linha | `db.ChecklistColumns.Add`. A chave é `Guid` da aplicação mapeada como `ValueGenerated.OnAdd`, e para entidade descoberta por navegação o EF lê "chave preenchida" como "já existe". Era a única entidade criada sem `Add` explícito |
| Renomear coluna para nome de outra ativa | HTTP 500 por violação do índice único | Regra de nome único passou a valer também na edição (`ChecklistTemplate.UpdateColumn`) |
| Violação de restrição virava 500 | Nenhum ponto do servidor tratava `DbUpdateException` | `DatabaseConflictExceptionHandler` traduz em 409 |
| Editar tipo apagava a restrição de setores | A tela enviava `SectorIds` vazio, e vazio significa "vale para todos" | O modal carrega, mostra e reenvia os setores atuais |

O primeiro tinha uma lacuna de cobertura exata: existiam testes de **edição** de coluna e de
**recusa** por nome repetido — os dois caminhos que não chegam ao `INSERT`. Nenhum criava uma
coluna com sucesso.

### Validado manualmente neste ambiente

Servidor executado de verdade, com estas verificações feitas:

- subida com migrations, seed e criação do administrador por variável de ambiente;
- `/health` e `/health/ready` respondendo `Healthy`;
- login retornando as 12 permissões e acesso a todos os setores;
- bootstrap com 1 setor, 16 leitos, 3 tipos, 3 marcadores e os horários corretos;
- sessão criada automaticamente com data de serviço **2026-08-05** às 13h — confirmando na
  prática a regra do plantão 19:00→07:00;
- marcação direta via REST;
- push retornando `Applied`, e o **mesmo `OperationId` retornando `Duplicate`**;
- desmarcar com versão defasada retornando `Conflict` com "A conclusão registrada no servidor
  prevaleceu".

---

## Critérios de aceitação do enunciado

| # | Critério | Estado |
|---|---|---|
| 1 | Administrador cadastra setores e leitos | Implementado · Validado por teste automatizado |
| 2 | Administrador edita os dados iniciais | Implementado · Validado por teste automatizado |
| 3 | Administrador cadastra usuário | Implementado · Validado por teste automatizado |
| 4 | Usuário pertence a vários grupos | Implementado · Validado por teste automatizado |
| 5 | Grupos controlam telas, ações e setores | Implementado · Validado por teste automatizado |
| 6 | Usuário sem acesso não chama o endpoint protegido | Implementado · Validado por teste automatizado |
| 7 | Checklist em matriz no desktop | Implementado · Validado por teste automatizado (bUnit) · **Validado manualmente** no Windows |
| 8 | Checklist no celular sem rolagem horizontal | Implementado · Validado por teste automatizado (bUnit) · **Validado em aparelho** |
| 9 | Um toque marca imediatamente | Implementado · Validado por teste automatizado · **Validado em aparelho** |
| 10 | Marcação gravada localmente | Implementado · Validado por teste automatizado |
| 11 | Marcação permanece após reiniciar o app | Implementado · Validado por teste automatizado (contexto fechado e reaberto sobre o mesmo arquivo) |
| 12 | Funciona sem servidor | Implementado · Validado por teste automatizado |
| 13 | Alterações offline entram na fila | Implementado · Validado por teste automatizado |
| 14 | Sincroniza quando o servidor retorna | Implementado · Validado por teste automatizado |
| 15 | Operação reenviada não duplica | Implementado · Validado por teste automatizado **e manualmente** |
| 16 | Dois dispositivos online recebem atualizações | Implementado · Validado por teste de integração (cliente real contra o hub real) · **Não validado** com dois aparelhos de verdade |
| 17 | Conflito não apaga conclusão mais nova | Implementado · Validado por teste automatizado **e manualmente** |
| 18 | C.I., Sondas e Drenos em qualquer leito | Implementado · Validado por teste automatizado |
| 19 | Classificações são temporárias da sessão | Implementado · Validado por teste automatizado |
| 20 | Notificação local pode ser agendada | Implementado · **Validado em aparelho**: alerta disparou no horário da coluna |
| 21 | Botão de teste dispara notificação | Implementado · **Validado em aparelho** |
| 22 | App informa quando as notificações não estão saudáveis | Implementado · Validado por teste automatizado (bUnit) |
| 23 | Repetições canceladas ao concluir | Implementado · Validado por teste automatizado |
| 24 | Administrador altera horários | Implementado · Validado por teste automatizado |
| 25 | App reagenda após sincronizar configurações | Implementado · **Validado em aparelho**, junto com o critério 20 |
| 26 | Sessões fechadas apagadas após a retenção | Implementado · Validado por teste automatizado |
| 27 | Não existe histórico de usuário por marcação | Implementado · Validado por teste automatizado (inspeciona o modelo do EF) |
| 28 | Não existem dados de paciente | Implementado · Verificável por inspeção do modelo |
| 29 | Build dos projetos compatíveis passa | ✅ **Toda a solução, 0 avisos** |
| 30 | Testes compatíveis passam | ✅ **412 testes** |

---

## Por área

### Domínio e regras — Implementado · Validado por teste automatizado
Turno atravessando meia-noite, união de permissões, retenção, conflito, planejamento de
notificações, catálogo de seeds.

### Servidor — Implementado · Validado por teste automatizado e manualmente
EF Core + SQLite, Identity, migrations, seed idempotente, JWT com refresh rotacionado e revogação,
lockout, limite de requisições, políticas por chave de permissão, endpoints de sessão, checklist,
sincronização, administração, dispositivos e health, hub SignalR, manutenção periódica.

### Interface — Implementado · Validado por teste automatizado (bUnit) e em execução real
Design system em CSS próprio sem CDN, todos os componentes pedidos pelo enunciado, matriz no
desktop, lista por coluna no celular, telas de configuração, login, setor, painel, checklist,
classificações, pendências, plantão, estado do dispositivo e administração.

### Offline e sincronização — Implementado · Validado por teste automatizado
Banco local, fila gravada na mesma transação do estado, push/pull, idempotência, adoção de
conflito, backoff com jitter, bootstrap de recuperação, autenticação offline PBKDF2.

### Notificações — Implementado · Validado em aparelho real
Abstrações, cálculo de agendamento (com teste), Android com AlarmManager + BootReceiver + deep
link, Windows com toast nativo e agendador in-process, diagnóstico de saúde.

**Validado em 18/08 num Xiaomi com Android 13**: o botão "Testar alerta" dispara, e o alerta
agendado chegou no horário da coluna. Era o item de maior risco do projeto.

Continua pendente: alerta com o aparelho **reiniciado** (o `BootReceiver`), e a isenção de bateria
concedida pelo usuário.

### Implantação — Implementado · Não validado
`Dockerfile`, `docker-compose.yml` com volume, `.env.example`, `backup.ps1`, `restore.ps1`.
**Docker não estava disponível neste ambiente: nada foi construído nem executado.**

---

## O que falta validar antes de produção

Em ordem de risco:

1. **Implantação em Docker** e o ciclo de backup/restauração. Seção 11.
2. **HTTPS com certificado confiável** nos aparelhos.
3. **Aviso em tempo real entre dois aparelhos de verdade** — o cliente do hub existe e tem teste
   de integração contra o hub real, mas ninguém viu ainda uma marcação aparecer sozinha na outra
   tela.
4. **Alerta após reiniciar o aparelho** (`BootReceiver`) e com o app fechado por horas.
5. **Fabricantes com restrição agressiva** — o Xiaomi usado no teste é um deles; falta confirmar
   o comportamento com a economia de bateria realmente apertada. Cenário 5.15.

Saíram desta lista, agora validados em aparelho: notificação agendada disparando no horário,
botão de teste, reagendamento após sincronizar, e o checklist em uso no celular e no desktop.

## Conclusão

O sistema **ainda não pode ser declarado pronto para produção**, mas por motivos diferentes dos de
antes.

O que mudou: o aplicativo roda em aparelho real, o plantão é operável de ponta a ponta, e a
**notificação agendada dispara no horário** — o requisito mais crítico e, até 18/08, o menos
validado. Três rodadas de uso em campo encontraram dezesseis defeitos que nenhum teste pegava;
todos corrigidos, todos com teste que os fixa.

O que impede a declaração:

- **a implantação nunca foi exercitada.** Docker e HTTPS com certificado confiável seguem como
  no primeiro dia;
- **o aviso em tempo real nunca foi visto entre dois aparelhos.** O cliente do hub foi
  implementado e tem teste de integração contra o hub real, mas teste não substitui ver a
  marcação aparecer sozinha na outra tela;
- **falta um plantão inteiro de uso**, com o aparelho reiniciando, a bateria apertando e o app
  fechado por horas. É onde este sistema falha em silêncio se estiver errado.
