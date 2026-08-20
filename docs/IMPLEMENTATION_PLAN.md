# Plano de implementação

Este documento é o plano vivo do projeto. Ele descreve *o que* será construído e em *que ordem*.
O andamento real fica em [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).

## Objetivo

Substituir a folha de papel "CHECKLIST DE PLANTÃO" (ver
[reference/CHECKLIST-OESTE-PM.pdf](reference/CHECKLIST-OESTE-PM.pdf)) por um aplicativo instalado, rápido,
que funciona sem internet e notifica nos horários das colunas.

A folha original tem três grades independentes sobre o mesmo conjunto de 16 leitos:

| Grade    | Colunas                              |
|----------|--------------------------------------|
| Gelo     | 20H · 22H · 00H · 02H · 04H · 06H    |
| Glicemia | Jantar · Café                        |
| SSVV     | PM · AM                              |

E, no rodapé, três listas livres onde se anotam os leitos com **C.I.**, **Sondas** e **Drenos** — que no
sistema viram classificações opcionais do leito *dentro da sessão atual*, não atributos permanentes.

## Ambiente detectado

| Item | Estado |
|---|---|
| .NET SDK | 10.0.201 (e 9.0.309) — alvo único `net10.0` |
| Workloads | `android` 36.1.30, `maui-windows` 10.0.20, `ios`, `maccatalyst` |
| Android SDK | `C:\Program Files (x86)\Android\android-sdk` (platforms 35/36, build-tools 36.0.0), OpenJDK em `C:\Program Files\Android\openjdk` |
| Dispositivo/emulador Android | Nenhum disponível |
| Windows 10 SDK | Ausente — build MSIX empacotado indisponível |
| nuget.org | Acessível |

As consequências dessas ausências estão em [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md) e no
status de validação de [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).

## Estrutura da solução

```
ChecklistPlantao.sln
ChecklistPlantao.NoMaui.slnf        filtro sem o head MAUI (ciclo rápido de build/teste)
src/
  ChecklistPlantao.Domain           entidades, value objects, enums, regras puras
  ChecklistPlantao.Contracts        DTOs, requests/responses, contratos de sync e SignalR
  ChecklistPlantao.Application      casos de uso, abstrações, validação, permissões
  ChecklistPlantao.Infrastructure   EF Core servidor, Identity, migrations, ChangeLog
  ChecklistPlantao.Server           Web API, JWT, SignalR, health, serviços de fundo
  ChecklistPlantao.Client.Abstractions
                                     contratos entre interface e núcleo do cliente
  ChecklistPlantao.UI               Razor Class Library: componentes, páginas, design system
  ChecklistPlantao.Client.Core      SQLite local, Outbox, sync, auth offline, agenda de notificações
  ChecklistPlantao.Client           MAUI Blazor Hybrid (Android + Windows)
tests/
  ChecklistPlantao.Domain.Tests
  ChecklistPlantao.Application.Tests
  ChecklistPlantao.Client.Core.Tests
  ChecklistPlantao.Server.IntegrationTests
  ChecklistPlantao.UI.Tests
deploy/                             Dockerfile, compose, scripts de backup/restore
docs/                               esta documentação
```

Grafo de referências, sem ciclos:

```
Domain ← Contracts(∅)
Application → Domain, Contracts
Infrastructure → Domain, Application, Contracts
Server → Domain, Application, Contracts, Infrastructure
Client.Abstractions → Domain, Contracts
UI → Domain, Contracts, Client.Abstractions
Client.Core → Domain, Application, Contracts, Client.Abstractions
Client → UI, Client.Core
```

`Infrastructure` (e portanto Identity) **não** é alcançável a partir do cliente — ver D-005 em
[DECISIONS.md](DECISIONS.md).

## Fases

Cada fase termina com `dotnet build` e `dotnet test`. Nenhuma fase acumula erro para a seguinte.

| # | Fase | Entrega |
|---|------|---------|
| 1 | Fundação | Solução, projetos, CPM, analisadores, git, documentação inicial |
| 2 | Domínio e contratos | Entidades, regras de turno/pendência/permissão/retenção, DTOs, testes de domínio |
| 3 | Servidor | EF Core, Identity, migrations, seed, JWT, permissões, endpoints, sync, SignalR, testes de integração |
| 4 | RCL e design system | CSS próprio, componentes e páginas, testes bUnit |
| 5 | Client.Core | Banco local, Outbox, push/pull, conflitos, auth offline, agenda de notificações, testes |
| 6 | Cliente MAUI | Heads Android e Windows, serviços de plataforma, notificações nativas |
| 7 | Implantação | Docker, compose, backup/restore, serviço no Windows, documentação completa |
| 8 | Validação | Build e testes completos, tentativa de build Android/Windows, status honesto |

## Critérios de aceitação

Os 30 critérios do enunciado estão rastreados um a um em
[IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md), cada um com o estado de validação real.
