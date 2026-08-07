# Windows

> O projeto **compila** para `net10.0-windows10.0.19041.0` neste ambiente. A execução e as
> notificações em máquina real não foram validadas — ver
> [MANUAL_TEST_PLAN.md](MANUAL_TEST_PLAN.md), seção 6.

## Requisitos

| Item | Versão |
|---|---|
| Windows | 10 versão 1809 (17763) ou superior |
| .NET SDK | 10.0.2xx |
| Workload | `maui-windows` |
| Windows App SDK | Runtime instalado na máquina |

```bash
dotnet workload install maui-windows
```

## Compilar e executar

```bash
dotnet build src/ChecklistPlantao.Client -f net10.0-windows10.0.19041.0 -c Debug
```

```bash
dotnet run --project src/ChecklistPlantao.Client -f net10.0-windows10.0.19041.0
```

Publicar:

```bash
dotnet publish src/ChecklistPlantao.Client -f net10.0-windows10.0.19041.0 -c Release
```

## Desempacotado, e por quê

O aplicativo usa `WindowsPackageType=None`. Consequências:

- instalação por cópia de pasta, sem MSIX e sem certificado;
- **não** exige o Windows 10 SDK para compilar;
- exige o **runtime do Windows App SDK** na máquina de destino.

Para evitar essa dependência, publique autocontido:

```bash
dotnet publish src/ChecklistPlantao.Client -f net10.0-windows10.0.19041.0 -c Release -p:WindowsAppSDKSelfContained=true -p:SelfContained=true
```

O pacote fica maior, mas roda sem instalar nada antes.

## Notificações — leia antes de prometer

Decisão D-010, confirmada com o cliente.

Sem MSIX o Windows não oferece agendamento de notificação no sistema operacional. Os alertas são
disparados por um temporizador dentro do processo do aplicativo.

| Situação | Alerta chega? |
|---|---|
| Aplicativo aberto | Sim |
| Aplicativo minimizado | Sim |
| Aplicativo fechado | **Não** |
| Computador desligado ou suspenso | **Não** |

**O aplicativo declara isso.** A tela "Estado do dispositivo" mostra "Alerta exige o app aberto:
sim" e a faixa de saúde permanece visível.

**Recomendação operacional:** deixar o aplicativo aberto no posto durante o plantão. Ele foi
desenhado para isso — a matriz em tela cheia é a visão de acompanhamento do turno.

Para alertas com o aplicativo fechado seria necessário empacotar como MSIX, o que exige o
Windows 10 SDK e certificado de assinatura. A interface `ILocalNotificationScheduler` já está
preparada para receber essa implementação sem alterar o restante do sistema.

## Uso por teclado

A grade em matriz foi feita para teclado:

| Tecla | Ação |
|---|---|
| `Tab` / `Shift+Tab` | Percorre as células |
| `Espaço` ou `Enter` | Marca e desmarca |
| `Ctrl+F` | Foco na busca (padrão do navegador embutido) |

O foco é sempre visível, com contorno de 3 px. A primeira coluna e o cabeçalho ficam fixos durante
a rolagem.

## Problemas comuns

| Sintoma | Causa provável |
|---|---|
| Não abre; erro de Windows App SDK | Runtime ausente — instale ou publique autocontido |
| Nenhum toast | Notificações desativadas para o aplicativo, ou Assistente de Foco ativo |
| Alerta não chegou de madrugada | Aplicativo fechado ou computador suspenso — limitação conhecida |
| Não conecta ao servidor | Firewall bloqueando, ou endereço com `localhost` em vez do IP |
