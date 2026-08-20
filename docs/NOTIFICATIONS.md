# Notificações

## Princípio

O alerta principal é **local e agendado no aparelho**. Não depende de internet, de push remoto nem
de o servidor estar acessível — se dependesse, seria inútil justamente nas noites em que a rede cai.

## Abstrações

Todas em `ChecklistPlantao.Client.Core.Notifications`:

| Interface | Responsabilidade |
|---|---|
| `ILocalNotificationScheduler` | Agendar, cancelar, exibir agora, listar agendados |
| `INotificationPermissionService` | Consultar, solicitar e abrir a configuração do sistema |
| `INotificationHealthService` | Listar, em português, tudo que impede o alerta de funcionar |
| `IDeviceStartupRescheduler` | Recalcular tudo após reinício, sincronização ou mudança de fuso |
| `INotificationSoundService` | Som próprio, quando a plataforma permitir |

## Como um alerta é calculado

`LocalNotificationPlanService` lê o snapshot local da sessão e usa o mesmo
`SessionSummaryCalculator` do resumo online e offline. Depois, `NotificationScheduleBuilder`
transforma cada coluna pendente na lista concreta:

1. a janela do plantão (padrão 19:00 → 07:00) define a que dia pertence cada coluna;
2. `NotificationPlanner` produz as ocorrências: antecedência opcional, alerta na hora e as
   repetições após a tolerância;
3. colunas **sem pendência são puladas** — é assim que as repetições são canceladas quando o
   horário é concluído;
4. conclusões de leitos fora de `SessionBed.IsActiveInSession` são ignoradas;
5. sessão encerrada gera **lista vazia**;
6. ocorrências já passadas são descartadas.

Todo o cálculo é puro: recebe o instante e o fuso, não os consulta. É por isso que a travessia da
meia-noite tem teste automatizado sem depender do relógio da máquina.

### Identificador estável

`{colunaId}:{aaaaMMdd}:{tipo}:{repeticao}`

Reagendar recalcula o mesmo identificador, então o sistema **substitui** em vez de acumular alertas
duplicados. É o que permite reagendar tudo do zero a cada sincronização sem efeito colateral.

## Parâmetros por coluna

Configuráveis no painel administrativo, por coluna:

| Parâmetro | Padrão | Efeito |
|---|---|---|
| Horário | — | Hora real; sem ele a coluna nunca alerta |
| Notificar | ligado | Desligar silencia a coluna sem apagá-la |
| Antecedência | 0 min | Aviso antes da hora |
| Tolerância | 15 min | Espera antes de começar a insistir |
| Intervalo de repetição | 10 min | Espaçamento entre lembretes |
| Máximo de repetições | 3 | Quantos lembretes no máximo |
| Adiar | ligado / 5 min | Botão "Lembrar em N minutos" |

Configurações gerais: som, vibração, prioridade, habilitação por plataforma, modelos de texto e
alerta em tela cheia.

Os modelos aceitam `{checklist}`, `{coluna}`, `{setor}` e `{pendentes}`:

```
ATENÇÃO — Gelo 22H
8 leito(s) pendente(s) no setor Oeste.
```

## Android

- Canal de alta importância com som e vibração, criado na primeira execução.
- `AlarmManager` com `SetExactAndAllowWhileIdle` quando há permissão de alarme exato; caso
  contrário `SetAndAllowWhileIdle`, e **a interface avisa que o horário pode não ser exato**.
- `POST_NOTIFICATIONS` solicitada em tempo de execução (Android 13+).
- `BootReceiver` reage a `BOOT_COMPLETED`, `MY_PACKAGE_REPLACED`, `TIME_CHANGED` e
  `TIMEZONE_CHANGED`, marcando o reagendamento; ele acontece na próxima abertura.
- Deep link: tocar na notificação abre `/checklist/{template}/{coluna}` — a coluna atrasada, não a
  tela inicial.
- Alerta em tela cheia **desligado por padrão**: exige permissão especial, é intrusivo e as lojas
  restringem o uso. Existe a opção no painel, com o aviso.

**Alertas funcionam com o aplicativo fechado e sobrevivem ao reinício do aparelho.**

## Windows

Decisão D-010, confirmada com o cliente. Sem MSIX não há agendamento no sistema operacional, então
os alertas vêm de um temporizador dentro do processo e o toast é o nativo (`AppNotificationManager`).

**Consequência: o alerta exige o aplicativo aberto ou minimizado.** Fechado, não há alerta.

`RequiresAppRunning` é verdadeiro, a tela "Estado do dispositivo" mostra a ressalva e a faixa de
saúde permanece visível. Para alertas com o app fechado seria preciso MSIX + Windows 10 SDK +
certificado; a interface já comporta essa implementação sem alterar o restante.

## Saúde das notificações

A regra é única: **se há qualquer problema na lista, o aplicativo não diz que está tudo em ordem.**

`NotificationHealthService` verifica permissão de notificações, permissão de alarme exato, economia
de bateria, exigência de app em execução, habilitação pela plataforma no painel e existência de
setor selecionado.

Havendo problema, a faixa fica fixa no topo, **não pode ser dispensada** enquanto durar, e oferece
"Corrigir agora" (abre exatamente a tela do sistema onde falta permissão) e "Testar alerta".

## Validação realizada e pendente

Em 18/08/2026, num Xiaomi com Android 13, o botão de teste e um alerta agendado no horário da
coluna foram validados com o aplicativo em execução real. Permanecem pendentes o alerta depois de
reiniciar o aparelho (`BootReceiver`), um período prolongado com o aplicativo fechado e a validação
com isenção/restrição agressiva de bateria. No Windows, continua valendo a limitação deliberada:
sem MSIX o alerta exige o processo aberto ou minimizado. Os cenários restantes estão em
[MANUAL_TEST_PLAN.md](MANUAL_TEST_PLAN.md), seções 5 a 9.
