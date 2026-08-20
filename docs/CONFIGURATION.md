# Referência de configuração

Todas as opções do sistema, em um lugar só. Antes disto, elas estavam espalhadas entre
`appsettings.json`, o `DEPLOYMENT.md` e o código — e a pergunta "o que esse valor faz mesmo?"
custava uma leitura de fonte.

Há **duas** camadas de configuração, e a diferença importa:

| Camada | Onde vive | Quem muda | Quando vale |
|---|---|---|---|
| **Do servidor** | `appsettings.json`, variáveis de ambiente, User Secrets | quem opera a instalação | na subida do servidor |
| **Da instituição** | banco do servidor, tela de Administração | o administrador, pelo aplicativo | sincronizada para os aparelhos |

A primeira exige reiniciar o servidor. A segunda chega aos aparelhos na sincronização seguinte.

---

## Precedência e formato

A configuração do servidor segue a ordem padrão do ASP.NET Core — **o último vence**:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. User Secrets (só em Development)
4. Variáveis de ambiente
5. Argumentos de linha de comando

Em variável de ambiente, o separador de seção é **dois sublinhados**:

```bash
Database__Path=/dados/checklistplantao.db
```

```bash
Jwt__SigningKey=cole-aqui-uma-chave-aleatoria-de-no-minimo-32-caracteres
```

## Validação na subida

Configuração inválida **derruba o servidor na inicialização**, com o nome da chave na mensagem.
É deliberado: falhar ao subir é barato, falhar na primeira requisição é caro — nesse momento
alguém já depende do serviço.

Coberto por `ConfigurationValidationTests`, que sobe servidores com valores inválidos e exige a
recusa.

---

## `Jwt` — autenticação

| Chave | Padrão | O que faz | Quando mexer |
|---|---|---|---|
| `SigningKey` | **obrigatório**, mín. 32 caracteres | Assina os tokens | Sempre defina, e **nunca no repositório**. Ver [SECURITY.md](SECURITY.md) |
| `Issuer` | `ChecklistPlantao` | Emissor esperado no token | Só se houver outro emissor no ambiente |
| `Audience` | `ChecklistPlantao.Client` | Público esperado | Idem |
| `AccessTokenMinutes` | `30` (1–1440) | Validade do token de acesso | Reduzir encurta a janela em que um usuário desativado ainda entra |
| `RefreshTokenDays` | `30` (1–365) | Validade do token de renovação | Reduzir obriga a digitar a senha com mais frequência |

> Sem `SigningKey` o servidor **não sobe**. Não existe chave padrão no código, por decisão do
> enunciado.

## `Lockout` — bloqueio por tentativas

| Chave | Padrão | O que faz | Quando mexer |
|---|---|---|---|
| `MaxFailedAttempts` | `5` (1–20) | Erros de senha até bloquear | Reduzir aperta a segurança e aumenta o incômodo no plantão |
| `LockoutMinutes` | `15` (1–1440) | Duração do bloqueio | — |

## `Database` — banco central

| Chave | Padrão | O que faz | Quando mexer |
|---|---|---|---|
| `Path` | `data/checklistplantao.db` | Arquivo SQLite | **Sempre**, em produção: precisa apontar para um volume persistente, nunca para pasta temporária |
| `EnableWriteAheadLogging` | `true` | WAL, que melhora leitura concorrente | Desligar só para diagnosticar |
| `MigrateOnStartup` | `true` | Aplica migrations na subida | Desligar se as migrations forem aplicadas por outro processo |
| `SeedOnStartup` | `true` | Aplica os dados iniciais | Desligar depois da primeira subida não é necessário: o seed é idempotente |
| `BusyTimeoutSeconds` | `15` (1–300) | Espera quando o banco está travado por outra escrita | Aumentar se houver muita escrita concorrente |

> Caminho vazio **derruba a subida**.

## `Bootstrap` — administrador inicial

| Chave | Padrão | O que faz |
|---|---|---|
| `AdminUserName` | — | Usuário do administrador criado na primeira subida |
| `AdminPassword` | — | Senha dele |
| `AdminDisplayName` | — | Nome exibido |

Os dois primeiros precisam ser informados **juntos, ou nenhum dos dois** — preencher só metade
derruba a subida. Antes disso, meia configuração não criava administrador nenhum, o servidor subia
normalmente, e a pessoa descobria no primeiro login.

Sem nenhum dos dois, o servidor sobe, registra um aviso e não cria usuário. É o caminho legítimo
para uma instalação que já tem administrador.

```bash
dotnet user-secrets set "Bootstrap:AdminPassword" "SuaSenhaForte1" --project src/ChecklistPlantao.Server
```

## `Maintenance` — limpeza periódica

| Chave | Padrão | O que faz | Quando mexer |
|---|---|---|---|
| `IntervalMinutes` | `30` (1–1440) | Intervalo entre as passagens | Aumentar em instalações pequenas |
| `ChangeLogRetentionDays` | `30` (0–3650) | Dias que o log de alterações é mantido | Reduzir economiza espaço; **aparelhos offline além deste prazo refazem o bootstrap** |
| `ProcessedOperationRetentionDays` | `7` (0–3650) | Dias que o registro de idempotência é mantido | Reduzir abaixo do tempo que um aparelho pode ficar offline arrisca reprocessar operações |
| `DeviceInactivityDays` | `30` (1–3650) | Dias sem contato até marcar o dispositivo como inativo | — |

> `ChangeLogRetentionDays` é a chave que mais merece atenção: ela define **por quanto tempo um
> aparelho pode ficar sem sincronizar** antes de precisar refazer o bootstrap. Ver
> [OFFLINE_SYNC.md](OFFLINE_SYNC.md).

## `RateLimit` — limites de chamada

| Chave | Padrão | O que faz |
|---|---|---|
| `LoginPerMinute` | `20` | Tentativas de login por minuto, por origem |
| `SyncPerMinute` | `30` | Sincronizações por minuto |
| `SyncBurst` | `60` | Rajada permitida acima do limite por minuto |

Em rede hospitalar com NAT, muitos aparelhos compartilham o mesmo IP de origem — aumentar pode ser
necessário. Números baixos demais aparecem como falha de sincronização intermitente.

## `Cors` — origens permitidas

| Chave | Padrão | O que faz |
|---|---|---|
| `AllowedOrigins` | `[]` | Origens liberadas para o navegador |

Vazio é o correto para o aplicativo instalado, que não é um site. Só preencha se houver um cliente
web de verdade.

## `Logging`

Segue o padrão do ASP.NET Core. O que o projeto define:

| Categoria | Nível |
|---|---|
| `Default` | `Information` |
| `Microsoft.AspNetCore` | `Warning` |
| `Microsoft.EntityFrameworkCore.Database.Command` | `Warning` |
| `ChecklistPlantao` | `Information` |

Subir `Microsoft.EntityFrameworkCore.Database.Command` para `Information` mostra todo o SQL — útil
para diagnosticar, ruidoso para deixar ligado.

> **Nunca são registrados:** senhas, tokens, verificadores de acesso offline e qualquer dado
> pessoal. Ver [SECURITY.md](SECURITY.md).

---

## Configurações da instituição

Estas **não** ficam em arquivo: vivem no banco do servidor, são editadas em
**Administração → Configurações** e chegam aos aparelhos pela sincronização.

| Configuração | Padrão | O que faz |
|---|---|---|
| Fuso horário | `America/Sao_Paulo` | Base de todo cálculo de horário e de data de serviço |
| Início do turno | `19:00` | Começo da janela do plantão |
| Fim do turno | `07:00` | Fim da janela |
| Retenção após encerrar | `24 h` | Tempo que uma sessão encerrada continua consultável antes de as marcações serem apagadas |
| Validade do acesso offline | `7 dias` | Quanto tempo o aparelho aceita entrada sem servidor |
| Tentativas offline | `5` | Erros de senha no aparelho até bloquear localmente |
| Abrir plantão automaticamente | `sim` | Cria a sessão do dia sem intervenção |

A janela do turno é a regra mais consequente do sistema: é ela que decide que **00H a 06H caem no
dia seguinte**. Ver [DOMAIN.md](DOMAIN.md) e a decisão D-003 em [DECISIONS.md](DECISIONS.md).

Cada setor pode sobrescrever a janela do turno, para plantões com horário próprio.

---

## Configuração do aparelho

O aplicativo guarda localmente apenas o que é dele:

| Item | Onde | Como muda |
|---|---|---|
| Endereço do servidor | banco local | Tela de configuração, no primeiro uso ou pelo botão "Alterar" |
| Nome do dispositivo | banco local | Mesma tela |
| Setor atual | banco local | Nome do setor, no topo da tela |
| Tema (claro, escuro ou seguir o aparelho) | armazenamento do WebView | Interruptor no menu do rodapé, ou Estado do dispositivo → Aparência |

Tudo o mais — horários, leitos, marcadores, permissões — vem do servidor e não é editável no
aparelho. Ver [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md), item 10.

## Por que o tema não está no banco local

Subir a versão do esquema local **apaga o banco** — não há migrations no aparelho (D-017), então
`EnsureLocalSchema` recria o arquivo quando a versão muda. Junto com o banco iria a fila de envio.

Uma preferência de aparência não pode custar as marcações de um plantão. Por isso ela vive no
armazenamento do WebView, sob a chave `checklistplantao.tema`. Se os dados do aplicativo forem
limpos, ela volta a ser "seguir o aparelho" — que é o padrão de qualquer forma. Ver
[DECISIONS.md](DECISIONS.md), D-024.
