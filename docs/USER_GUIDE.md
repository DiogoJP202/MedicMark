# Guia do plantão

## Primeira vez neste aparelho

1. **Endereço do servidor.** O aplicativo pede o endereço na primeira abertura — algo como
   `http://192.168.0.10:5000`, informado pela TI. Toque em **Testar conexão** antes de salvar.
2. **Nome do aparelho.** Algo que identifique, como "Tablet Oeste 1".
3. **Entrar.** O primeiro login precisa de rede. Depois disso, você entra mesmo sem conexão.
4. **Permitir notificações** quando o aparelho perguntar. Sem isso, você não recebe os lembretes
   dos horários.

## Marcar uma tarefa

Toque na célula. Pronto.

- O X aparece **na hora**, com ou sem internet.
- Não há confirmação — seria lento demais para o plantão.
- Errou? Toque de novo, ou use **Desfazer** na faixa que aparece por alguns segundos.

## No celular

1. Escolha o tipo de checklist (Gelo, Glicemia, SSVV).
2. Escolha o horário nas abas do topo.
3. Toque nos leitos da lista.

Cada aba mostra quantos leitos faltam naquele horário e marca em destaque os atrasados.

**Somente pendentes** esconde o que já está feito — o jeito mais rápido de terminar um horário.

**Pesquisar** filtra por número do leito.

## No computador

A tela mostra a matriz completa, como a folha de papel: leitos nas linhas, horários nas colunas.
O número do leito e o cabeçalho ficam fixos durante a rolagem.

Funciona por teclado: `Tab` percorre, `Espaço` ou `Enter` marca.

## Classificações (C.I., Sondas, Drenos)

Menu **Classificações**. Marque o que se aplica a cada leito — um leito pode ter mais de uma, ou
nenhuma.

Os botões no topo filtram: tocar em "Sondas" mostra só os leitos com sonda.

Estas marcações valem **apenas para o plantão atual**. No plantão seguinte, começam zeradas.

## Faixa do topo

| O que diz | O que significa |
|---|---|
| **Tudo sincronizado** | Tudo enviado ao servidor |
| **N alterações sendo enviadas** | Enviando; nada se perdeu |
| **Offline — N aguardando** | Sem rede. Continue trabalhando; sobe sozinho quando voltar |
| **Servidor indisponível** | Há rede, mas o servidor não responde. **Suas marcações estão salvas neste aparelho** |

Em nenhum desses casos você precisa parar. O aplicativo foi feito para funcionar assim.

## Tarefas atrasadas

Quando um horário passa com pendências, uma faixa laranja aparece no topo:

```
3 TAREFAS ATRASADAS
Gelo 22H, SSVV PM
Toque para visualizar
```

Toque para ir direto às pendências, agrupadas por horário e com os leitos que faltam.

## Faixa vermelha de notificações

Significa que este aparelho **não vai avisar** nos horários. Toque em **Corrigir agora** — o
aplicativo abre exatamente a tela onde falta permissão.

A faixa não pode ser fechada enquanto o problema existir. É proposital: perder um alerta de
madrugada é pior que um aviso insistente na tela.

Use **Testar alerta** para confirmar que voltou a funcionar.

## Encerrar o plantão

Menu **Plantão** → **Encerrar plantão**.

Antes de encerrar você vê o resumo: quantas tarefas foram feitas, quantas ficaram e quais leitos
estão com C.I., Sondas e Drenos. Havendo pendências, o sistema pede confirmação.

**Reiniciar checklist** devolve todas as marcações a pendente e mantém as classificações. Serve
para recomeçar o turno sem refazer as classificações dos leitos.

Encerrar e reiniciar **exigem conexão** com o servidor.

## Perguntas frequentes

**Perco alguma coisa se ficar sem internet?** Não. Tudo fica salvo no aparelho e sobe quando a
conexão volta — mesmo horas depois, mesmo depois de fechar o aplicativo ou reiniciar o aparelho.

**Duas pessoas marcaram o mesmo leito?** Sem problema. O sistema converge, e uma marcação de
"feito" nunca é apagada por engano.

**Alguém vê que fui eu que marquei?** Não. O sistema não guarda essa informação.

**Meu acesso offline expirou.** Conecte-se ao servidor e entre uma vez. Depois volta a funcionar
offline pelo período configurado.

**Não recebi o alerta.** Abra **Estado do dispositivo** — a tela lista exatamente o que está
faltando neste aparelho.
