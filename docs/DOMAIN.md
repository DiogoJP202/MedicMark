# Domínio

## O que o sistema modela

A folha de papel (ver [reference/CHECKLIST-OESTE-PM.pdf](reference/CHECKLIST-OESTE-PM.pdf)) tem
três grades independentes sobre os mesmos 16 leitos e três listas livres no rodapé.

| Papel | Sistema |
|---|---|
| Título "CHECKLIST DE PLANTÃO" | `OperationalSession` — um plantão de um setor |
| Coluna de números (1148…1169) | `Bed` — leito, só código |
| Grade "Gelo", "Glicemia", "SSVV" | `ChecklistTemplate` |
| Colunas 20H, Jantar, PM… | `ChecklistColumn`, cada uma com hora real |
| Um X numa célula | `ChecklistEntry.IsCompleted` |
| Listas C.I. / Sondas / Drenos | `SessionBedMarker` — classificação **da sessão**, não do leito |

O que **não** existe: paciente, nome, prontuário, diagnóstico, autoria de marcação.

## A janela do plantão

O checklist Gelo vai de 20H a 06H — atravessa a meia-noite. Sem uma janela definida não há como
dizer a que dia pertence a coluna "02H".

`ShiftWindow` (padrão 19:00 → 07:00, configurável, com sobreposição opcional por setor):

- **`ServiceDateFor(instante)`** — a que plantão um momento pertence. Em janela que atravessa a
  meia-noite, tudo antes do horário de início pertence ao plantão iniciado no dia anterior:
  02:00 do dia 7 é o plantão do dia 6.
- **`OccurrenceOf(dataDoPlantão, hora)`** — quando uma coluna acontece de fato. Hora ≥ início fica
  no próprio dia; hora menor cai no dia seguinte.

Com a janela 19:00 → 07:00 e o plantão de 06/08:

| Coluna | Hora | Acontece em |
|---|---|---|
| Gelo 20H | 20:00 | 06/08 20:00 |
| Gelo 22H | 22:00 | 06/08 22:00 |
| Gelo 00H | 00:00 | **07/08** 00:00 |
| Gelo 02H | 02:00 | **07/08** 02:00 |
| Gelo 06H | 06:00 | **07/08** 06:00 |
| Glicemia Jantar | 19:30 | 06/08 19:30 |
| Glicemia Café | 07:00 | **07/08** 07:00 |
| SSVV PM | 20:00 | 06/08 20:00 |
| SSVV AM | 06:00 | **07/08** 06:00 |

O mesmo plantão, na linha do tempo — a data de serviço é **06/08 o tempo todo**, mesmo depois da
meia-noite:

```mermaid
timeline
    title Plantão de 06/08 — data de serviço 06/08 do início ao fim
    section 06/08 (dia da sessão)
        19:00 : abre a janela do turno
        19:30 : Glicemia Jantar
        20:00 : Gelo 20H · SSVV PM
        22:00 : Gelo 22H
    section 07/08 (mesma sessão, dia seguinte)
        00:00 : Gelo 00H
        02:00 : Gelo 02H
        04:00 : Gelo 04H
        06:00 : Gelo 06H · SSVV AM
        07:00 : Glicemia Café : fecha a janela
```

A regra é genérica: vale para qualquer coluna criada depois, sem código específico por nome. O que
decide não é o nome da coluna, e sim se o horário dela é **anterior ao início do turno** — nesse
caso ela cai no dia seguinte.

## Permissões

Não existe papel "admin". Existem chaves:

`checklist.view` · `checklist.update` · `checklist.close` · `sector.select` · `admin.users` ·
`admin.groups` · `admin.sectors` · `admin.beds` · `admin.templates` · `admin.notifications` ·
`admin.devices` · `admin.settings`

Um usuário pertence a vários grupos e recebe a **união** — permissões e setores somam. Grupo
inativo não contribui com nada.

> Maria está em "Plantão Oeste" (ver e atualizar; setor Oeste) e em "Responsáveis por Sondas" (ver
> e encerrar; setor Leste). Maria pode ver, atualizar e encerrar, nos setores Oeste **e** Leste.

`AccessGroup.GrantsAllSectors` concede acesso a todos os setores, **inclusive os criados depois** —
sem isso, um setor novo ficaria invisível para os administradores até alguém reeditar o grupo.

## Sessão operacional

Um plantão de um setor. Pode ser aberta automaticamente (padrão) ou manualmente por quem tem
`checklist.close`.

**No máximo uma sessão aberta por setor** — garantido por índice único filtrado no banco, não só
por verificação em código: duas requisições simultâneas não conseguem burlar.

Ao encerrar, o resumo mostra concluídas, pendentes e os leitos de cada marcador (C.I., Sondas,
Drenos). Com pendências, exige confirmação explícita.

Reiniciar devolve todas as marcações a pendente e **mantém** as classificações — reiniciar o
checklist não é recomeçar o plantão.

## Retenção

O cliente não quer histórico permanente.

| Dado | Destino |
|---|---|
| Marcações e classificações | Apagadas após a janela de recuperação (padrão 24 h, podendo ser 0) |
| Sessão | Apagada junto |
| Usuários, grupos, setores, leitos, templates, colunas, marcadores, configurações | **Nunca** apagados |

Enquanto a janela dura, a sessão pode ser reaberta — é a rede de segurança para quem encerrou por
engano.

## Convergência

`MergePolicies` concentra as regras, e ficam no **domínio** porque o cliente precisa prever o que o
servidor fará.

**Marcação — conclusão vence.** Marcar é sempre aceito. Desmarcar só é aceito com a versão atual;
uma operação antiga, feita offline, nunca apaga silenciosamente uma conclusão mais nova.

**Configuração e classificações — versão manda.** Sem vencedor preferencial: versão defasada é
recusada e o estado atual volta.

## Versionamento

Toda entidade sincronizável tem `Version`, incrementada a cada alteração aceita. É a base do
controle de concorrência e do que o cliente envia como `BaseVersion`.

## Restrições no banco

Impostas por índice, não apenas por código:

- dois leitos **ativos** com o mesmo código no mesmo setor;
- duas colunas **ativas** com o mesmo nome no mesmo tipo de checklist;
- duas entradas para a mesma sessão + leito + tipo + coluna;
- dois marcadores iguais para o mesmo leito na mesma sessão;
- nome de usuário duplicado;
- associação duplicada entre usuário e grupo;
- duas sessões abertas no mesmo setor e data.

Índices de leito e coluna são **filtrados por ativo**: um item desativado não deve travar o
cadastro de um novo com o mesmo código.
