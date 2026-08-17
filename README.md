# ChecklistPlantão

Substitui a folha de papel "CHECKLIST DE PLANTÃO" por um aplicativo instalado, rápido, que
**funciona sem internet** e **notifica nos horários** das colunas.

A folha original está em [docs/reference/CHECKLIST-OESTE-PM.pdf](docs/reference/CHECKLIST-OESTE-PM.pdf):
três grades (Gelo, Glicemia, SSVV) sobre os mesmos 16 leitos, mais as listas de C.I., Sondas e Drenos.

| | |
|---|---|
| **Servidor** | ASP.NET Core 10 + SQLite + SignalR |
| **Aplicativo** | .NET MAUI Blazor Hybrid — Android e Windows, mesma interface |
| **Offline** | Banco SQLite local + fila de envio (Outbox) com operações idempotentes |
| **Notificações** | Locais e agendadas no aparelho; não dependem de internet nem de push |

Sem cadastro de paciente. Sem nome, CPF, prontuário, diagnóstico ou qualquer dado clínico.
Sem histórico de quem marcou cada tarefa — foi decisão explícita do cliente.

---

## Como executar o servidor

Pré-requisito: .NET SDK 10.0.2xx.

```bash
dotnet user-secrets set "Jwt:SigningKey" "cole-aqui-uma-chave-aleatoria-de-no-minimo-32-caracteres" --project src/ChecklistPlantao.Server
```

```bash
dotnet user-secrets set "Bootstrap:AdminUserName" "admin" --project src/ChecklistPlantao.Server
```

```bash
dotnet user-secrets set "Bootstrap:AdminPassword" "SuaSenhaForte1" --project src/ChecklistPlantao.Server
```

```bash
dotnet run --project src/ChecklistPlantao.Server
```

O servidor cria o banco, aplica as migrations e semeia os dados iniciais (setor Oeste, 16 leitos,
Gelo/Glicemia/SSVV com horários, marcadores C.I./Sondas/Drenos, grupos Administradores e Plantão
Oeste). O administrador só é criado se `Bootstrap:AdminUserName` e `Bootstrap:AdminPassword`
existirem — **nunca há senha padrão no código**.

Conferir:

```bash
curl http://localhost:5136/health/ready
```

Em Docker, ver [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

## Como executar o aplicativo

Windows (desempacotado):

```bash
dotnet build src/ChecklistPlantao.Client -f net10.0-windows10.0.19041.0 -c Debug
```

Android (aparelho conectado por USB com depuração ativa):

```bash
dotnet build src/ChecklistPlantao.Client -f net10.0-android -t:Run
```

Detalhes e requisitos por plataforma: [docs/ANDROID_SETUP.md](docs/ANDROID_SETUP.md) e
[docs/WINDOWS_SETUP.md](docs/WINDOWS_SETUP.md).

Na primeira execução o aplicativo pede o **endereço do servidor** (ex.: `http://192.168.0.10:5000`) —
nunca `localhost`, porque o aparelho não é a máquina do servidor. O primeiro login precisa de rede;
a partir daí a entrada offline vale por 7 dias configuráveis.

## Como rodar testes

```bash
dotnet test ChecklistPlantao.NoMaui.slnf -c Debug
```

O filtro `ChecklistPlantao.NoMaui.slnf` exclui o head MAUI, que não executa testes. Para compilar
tudo, inclusive os heads:

```bash
dotnet build ChecklistPlantao.sln -c Debug
```

## Migrations

```bash
dotnet dotnet-ef migrations add NomeDaMigration --project src/ChecklistPlantao.Infrastructure --startup-project src/ChecklistPlantao.Infrastructure --output-dir Persistence/Migrations
```

São aplicadas automaticamente na subida (`Database:MigrateOnStartup`). O banco local do aparelho
não usa migrations — ver D-017 em [docs/DECISIONS.md](docs/DECISIONS.md).

## Estrutura

```
src/
  ChecklistPlantao.Domain          entidades, regras de turno, permissões, retenção, conflito
  ChecklistPlantao.Contracts       DTOs e contratos de sincronização
  ChecklistPlantao.Application     casos de uso do servidor
  ChecklistPlantao.Infrastructure  EF Core, Identity, migrations, log de alterações
  ChecklistPlantao.Server          API, JWT, SignalR, health, manutenção
  ChecklistPlantao.UI              RCL: design system, componentes e páginas
  ChecklistPlantao.Client.Core     SQLite local, Outbox, sincronização, auth offline
  ChecklistPlantao.Client          MAUI Blazor Hybrid (Android + Windows)
tests/                             5 projetos, 287 testes
deploy/                            Dockerfile, compose, backup e restore
```

## Documentação

| Documento | Conteúdo |
|---|---|
| [HOW_TO_RUN.md](docs/HOW_TO_RUN.md) | **Da máquina limpa ao app rodando**, com os erros comuns e a saída de cada um |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | Camadas, dependências e por quê |
| [DECISIONS.md](docs/DECISIONS.md) | Decisões arquiteturais com contexto e consequências |
| [DOMAIN.md](docs/DOMAIN.md) | Regras de negócio, turno, permissões, retenção |
| [DATA_MODEL.md](docs/DATA_MODEL.md) | Tabelas, índices e restrições |
| [OFFLINE_SYNC.md](docs/OFFLINE_SYNC.md) | Outbox, cursor, idempotência e conflitos |
| [NOTIFICATIONS.md](docs/NOTIFICATIONS.md) | Agendamento, repetição e o que cada plataforma entrega |
| [SECURITY.md](docs/SECURITY.md) | Autenticação, acesso offline, o que nunca é registrado |
| [DEPLOYMENT.md](docs/DEPLOYMENT.md) | Docker, serviço no Windows, rede local, HTTPS, backup |
| [ANDROID_SETUP.md](docs/ANDROID_SETUP.md) | Requisitos, permissões e como validar no aparelho |
| [WINDOWS_SETUP.md](docs/WINDOWS_SETUP.md) | Requisitos e a limitação de notificação |
| [ROTEIRO_DE_TESTE.md](docs/ROTEIRO_DE_TESTE.md) | **Caminho curto para ver o sistema funcionando**, passo a passo |
| [MANUAL_TEST_PLAN.md](docs/MANUAL_TEST_PLAN.md) | Cenários que só um dispositivo real valida |
| [USER_GUIDE.md](docs/USER_GUIDE.md) | Guia do plantão |
| [ADMIN_GUIDE.md](docs/ADMIN_GUIDE.md) | Guia do administrador |
| [KNOWN_LIMITATIONS.md](docs/KNOWN_LIMITATIONS.md) | O que o sistema **não** faz |
| [IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md) | O que está pronto e o que foi validado como |

> **Antes de usar em produção**, leia [KNOWN_LIMITATIONS.md](docs/KNOWN_LIMITATIONS.md) e a seção
> de validação pendente em [IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md). As
> notificações em aparelhos reais e a implantação ainda não foram validadas.
