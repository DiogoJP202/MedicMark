# Guia do administrador

Tudo aqui está em **Administração**, no menu do aplicativo. Cada tela exige a permissão
correspondente — não existe um "modo admin" que libera tudo.

Alterações administrativas **exigem conexão** com o servidor.

## Primeiro acesso

O administrador inicial é criado na primeira execução do servidor, a partir de
`Bootstrap__AdminUserName` e `Bootstrap__AdminPassword` (ver [DEPLOYMENT.md](DEPLOYMENT.md)).

Faça isto logo:

1. Entre com o administrador inicial.
2. Crie os usuários reais do plantão.
3. **Troque a senha** do administrador.
4. Peça à TI para remover `Bootstrap__AdminPassword` do ambiente.

## Setores e leitos

O seed traz o setor **Oeste** com os 16 leitos da folha. Tudo é editável.

- **Ordem** controla a posição na tela. Use múltiplos de 10 para poder inserir depois.
- **Desativar** tira o item dos próximos plantões e não apaga o plantão atual.
- Um leito pode ser **movido** para outro setor.
- Dois leitos **ativos** não podem ter o mesmo código no mesmo setor. Desativados podem.
- **Turno do setor**: deixe em branco para usar a janela geral; preencha os dois campos para dar
  um horário próprio a este setor.

## Tipos de checklist e horários

Esta é a tela mais importante do sistema.

O seed traz Gelo (20H, 22H, 00H, 02H, 04H, 06H), Glicemia (Jantar 19:30, Café 07:00) e SSVV
(PM 20:00, AM 06:00). **Confirme esses horários com a equipe** — eles foram acordados no início do
projeto e definem quando cada aparelho alerta.

Por coluna:

| Campo | O que faz |
|---|---|
| **Nome exibido** | O que aparece na tela — "Jantar", "20H" |
| **Horário real** | Quando alertar. **Sem horário, a coluna nunca notifica** |
| **Ordem** | Posição na grade |
| **Notificar** | Silencia a coluna sem apagá-la |
| **Antecedência** | Aviso antes da hora |
| **Tolerância** | Espera antes de começar a insistir |
| **Intervalo de repetição** | Espaçamento entre lembretes |
| **Máximo de repetições** | Quantos lembretes no máximo |
| **Permitir adiar** | Botão "Lembrar em N minutos" |

Alterar um horário **reagenda todos os aparelhos** na próxima sincronização.

## Marcadores

C.I., Sondas e Drenos são apenas os iniciais — crie outros à vontade.

Renomear é seguro: o sistema identifica marcadores por Id, não por nome. Nenhuma regra do código
compara textos como "Sondas".

## Usuários e grupos

O acesso é sempre pela **união dos grupos ativos** do usuário. Não existe permissão avulsa.

Comece pelos grupos:

1. Nomeie ("Plantão Noturno Oeste").
2. Escolha os setores, ou marque **acesso a todos os setores** — que inclui os criados depois.
3. Marque as permissões.

Depois crie os usuários e associe. Um usuário em dois grupos recebe a soma.

Alterar um grupo **desconecta todos os membros**: a mudança precisa valer na hora.

Redefinir a senha desconecta o usuário de todos os aparelhos. A senha exige no mínimo 8 caracteres,
com pelo menos uma letra e um número.

**"Por que fulano não vê o setor tal?"** — use `GET /api/admin/users/{id}/effective-access`, que
mostra o acesso efetivo calculado.

## Notificações

Configurações gerais: som, vibração, prioridade, habilitação por plataforma e os textos.

Os modelos aceitam `{checklist}`, `{coluna}`, `{setor}` e `{pendentes}`:

```
ATENÇÃO — Gelo 22H
8 leito(s) pendente(s) no setor Oeste.
```

**Alerta em tela cheia no Android** vem desligado. Exige permissão especial, é intrusivo e as lojas
restringem o uso. Ligue apenas se a instituição realmente precisar.

## Configurações gerais

| Configuração | Padrão | Efeito |
|---|---|---|
| **Fuso horário** | `America/Sao_Paulo` | Base de toda exibição e agendamento |
| **Início / fim do plantão** | 19:00 / 07:00 | Define a que dia pertence cada coluna |
| **Recuperação após encerrar** | 24 h | Depois disso, as marcações são apagadas. Zero apaga na hora |
| **Validade do acesso offline** | 7 dias | Quanto tempo se entra sem servidor |
| **Tentativas offline** | 5 | Antes do bloqueio temporário |
| **Abrir plantão automaticamente** | ligado | Evita um botão a mais no início do turno |

Reduzir a validade do acesso offline diminui a janela em que um usuário desativado ainda consegue
entrar em um aparelho sem rede — ao custo de reconexões mais frequentes.

## Dispositivos

Mostra os aparelhos conhecidos com o **último estado sincronizado**.

Um aparelho offline há dois dias mostra dados de dois dias atrás. Isso é inerente: o servidor não
tem como saber o estado atual de um aparelho sem comunicação.

Use para responder "por que o tablet do posto não está alertando?" — a coluna de notificações
mostra o que ele relatou por último.

## Conflito de edição

Se aparecer *"Outra pessoa alterou este item"*, alguém salvou enquanto você editava. Atualize a
tela e refaça. O sistema **nunca** sobrescreve silenciosamente uma alteração de configuração.

## O que o sistema não faz

Antes de prometer à equipe, leia [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md). Em resumo: não há
histórico permanente do checklist, não há registro de quem marcou, o alerta no Windows exige o
aplicativo aberto, e um usuário desativado continua entrando em aparelho offline até ele
sincronizar ou a validade expirar.
