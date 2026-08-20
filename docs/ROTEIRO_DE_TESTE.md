# Roteiro de teste guiado

Sequência prática para ver o sistema funcionando, na ordem em que faz sentido exercitar.
Cada etapa diz **o que fazer**, **o que deve acontecer** e **por que importa**.

O plano exaustivo, com todos os cenários, está em [MANUAL_TEST_PLAN.md](MANUAL_TEST_PLAN.md).
Este aqui é o caminho curto para ver o essencial.

**Ambiente pressuposto:** servidor rodando no PC, celular ligado por USB com o túnel ativo
(`adb reverse tcp:5000 tcp:5000`), aplicativo instalado e login feito.

---

## 1. Marcação — a operação mais usada do sistema

1. Painel → toque em **Gelo**.
2. Escolha o horário **20H** nas abas.
3. Toque em alguns leitos.

**Esperado:** o `X` aparece **instantaneamente**, sem espera e sem confirmação. Aparece uma faixa
escura embaixo com **Desfazer**, que some sozinha em poucos segundos.

**Por que importa:** é o requisito central — marcar precisa ser um toque, não um formulário.
Repare que nada de "salvo com sucesso" aparece: mensagem repetitiva a cada marcação seria ruído.

4. Toque em **Desfazer** logo após marcar um leito.

**Esperado:** volta ao estado anterior.

5. Toque **várias vezes seguidas, rápido**, na mesma caixa.

**Esperado:** a primeira marcação vale; a caixa fica travada por 1 segundo e os toques extras são
ignorados. **Só aquela caixa** trava — tocar no leito seguinte funciona imediatamente, porque
percorrer os leitos em sequência é o uso normal desta tela.

---

## 2. Filtros e progresso

1. Ative **Somente pendentes**.

**Esperado:** os leitos já marcados desaparecem da lista. Sobra só o que falta fazer.

2. Digite `1152` na busca.

**Esperado:** só o leito 1152.

3. Olhe as abas de horário.

**Esperado:** cada aba mostra quantos faltam ("8 pendentes"), e as concluídas mudam de aparência.
A barra de progresso mostra número explícito — "10 de 16 · 6 pendentes" —, não só a barra.

4. Na linha de filtros por classificação, toque em **Sondas** (o botão só aparece se houver leitos
   com aquela classificação no plantão — faça a etapa 3 antes, se necessário).

**Esperado:** o checklist mostra só os leitos com Sondas, e a contagem ao lado do nome confere.
Tocar de novo no mesmo botão desliga o filtro. O filtro combina com **Somente pendentes** e com a
busca.

**Por que importa:** era preciso sair do checklist para saber quem tem sonda ou dreno. Agora as
classificações filtram a própria tela de marcação.

---

## 3. Classificações (C.I., Sondas, Drenos)

1. Menu → **Classificações**.
2. Marque **Sondas** e **Drenos** no mesmo leito.
3. Toque no botão de filtro **Sondas**.

**Esperado:** um leito aceita mais de uma classificação ao mesmo tempo; o filtro mostra só os
leitos com aquela marcação; a contagem ao lado do nome confere.

4. Role a lista até o fim e marque um leito de baixo.

**Esperado:** a lista **não volta ao topo**. A rolagem fica onde estava, e só a linha tocada muda.

5. Volte ao checklist.

**Esperado:** o leito classificado mostra as etiquetas ("Sondas", "Drenos") ao lado do número.

**Por que importa:** substitui as três listas soltas do rodapé da folha de papel, e são
classificações **do plantão atual**, não atributos permanentes do leito.

---

## 4. O modo offline — o teste mais importante

Este é o coração do sistema. Vale fazer com atenção.

1. Com o app aberto, **desconecte o cabo USB** (isso derruba o túnel e o servidor fica inalcançável).
2. Volte ao checklist e **marque 5 ou 6 leitos**.

**Esperado:** marcar continua funcionando exatamente igual, sem travar nem reclamar. No topo
aparece **"Servidor indisponível"** e, ao abrir a faixa, *"As alterações estão salvas neste
dispositivo"* com a contagem de pendentes.

3. **Feche o aplicativo completamente** (multitarefa → deslizar para fora).
4. Abra de novo.

**Esperado:** todas as marcações continuam lá, e a contagem de alterações aguardando também.

5. **Reconecte o cabo USB.** Se necessário, rode no PC: `adb reverse tcp:5000 tcp:5000`.
6. No app, toque no indicador de sincronização no topo.

**Esperado:** a faixa passa a **"Tudo sincronizado"** e a contagem zera.

**Por que importa:** é a promessa central. Nada se perde sem rede, nem ao fechar o app, nem ao
reiniciar o aparelho.

### Conferir do lado do servidor

No PC, para ver que as marcações realmente chegaram:

```bash
Select-String -Path .run\servidor.log -Pattern "sync/push" | Select-Object -Last 5
```

---

## 5. Pendências e atraso

1. Menu → **Pendências**.

**Esperado:** as pendências agrupadas por tipo de checklist e horário, com os leitos que faltam em
cada grupo. Horários já vencidos aparecem primeiro, com a etiqueta **"Atrasado"**.

2. Toque em **Abrir checklist** em um dos grupos.

**Esperado:** vai direto para aquele tipo **e naquele horário** — não para a tela inicial.

3. Se houver algo atrasado, volte ao Painel.

**Esperado:** faixa laranja no topo: *"N TAREFAS ATRASADAS"*, citando os horários.

**Por que importa:** é o alerta que funciona **mesmo quando a notificação do sistema falha** — e
notificação é um mecanismo que o sistema operacional pode suprimir.

---

## 6. Notificações

1. Menu → **Dispositivo**.
2. Confira a lista: permissão de notificações, alarme exato, som, vibração, fuso e hora.
3. Toque em **Testar notificação**.

**Esperado:** o alerta aparece em segundos.

4. Se houver faixa de aviso, toque em **Corrigir agora**.

**Esperado:** abre a tela do sistema **da pendência mais grave** — não uma tela genérica. Resolva,
volte e toque em **Verificar novamente**: o item some da lista.

### Alerta agendado com o app fechado

Este é o cenário crítico do plantão noturno.

O caminho, tela por tela — o item "Administração" no menu **só aparece para quem tem a permissão
`admin.settings`**; se você não o vê, entrou com um usuário de plantão, não com o administrador:

1. Menu → **Administração** → cartão **Tipos de checklist** (endereço `/admin/templates`).
2. Na tabela do **Gelo**, escolha uma linha de coluna (20H, 22H, …) e toque em **Editar** — o botão
   fica na última célula da linha, não no cabeçalho.
3. No campo **Horário real**, ponha **3 minutos à frente** do relógio do celular. Toque em
   **Salvar coluna**.

> A coluna **Notifica** da tabela precisa estar ligada, e a ajuda do campo explica o resto: *"Sem
> horário, a coluna não gera notificação."*

4. Volte ao checklist, abra aquela coluna e confirme que **há leitos pendentes** nela — coluna sem
   pendência não notifica, de propósito.
5. **Feche o aplicativo por completo** (não só minimize: deslize para fora da lista de recentes).
6. Espere os 3 minutos.

**Esperado:** a notificação chega com o app fechado, dizendo o tipo, o horário e quantos leitos
faltam. Tocar nela abre o checklist **naquela coluna**.

**Se não chegar**, o culpado provável é a MIUI, não o código. Ative:
Ajustes → Apps → Checklist de Plantão → **Início automático**, e bateria em **Sem restrições**.
Ver [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md), item 4.

7. Marque **todos** os leitos daquela coluna e espere o horário da repetição.

**Esperado:** **nenhuma** repetição chega. Coluna concluída não incomoda mais.

> Ao terminar, **devolva o horário original** da coluna (Gelo: 20H, 22H, 00H, 02H, 04H, 06H).

---

## 7. Administração e sincronização de configuração

1. **Administração → Setores e leitos** → crie um leito novo (ex.: `1170`).
2. Volte ao checklist e sincronize.

**Esperado:** o leito novo aparece na grade.

3. **Administração → Marcadores** → crie um marcador (ex.: `Isolamento`).
4. Vá em Classificações.

**Esperado:** o marcador novo está lá, junto com C.I., Sondas e Drenos.

5. **Administração → Tipos de checklist** → renomeie a coluna "Jantar" para "Jantar (19:30)".

**Esperado:** muda no aplicativo; nada quebra. As regras usam identificador, nunca o nome.

6. **Cadastro em modal.** Em qualquer tela de administração, toque em **Novo** (setor, leito,
   marcador ou coluna).

**Esperado:** abre um diálogo sobre a tela, com o foco já no primeiro campo. **Esc** e **Cancelar**
fecham sem salvar. A tela de trás não perde a rolagem nem o filtro.

7. **Nome duplicado — a regra que virava erro 500.** Em **Tipos de checklist → Gelo**, crie uma
   coluna nova chamada exatamente **20H** (que já existe e está ativa).

**Esperado:** mensagem legível dizendo que já existe uma coluna ativa com esse nome. **Não** pode
aparecer "erro inesperado", nem a tela quebrar.

8. Repita renomeando uma coluna existente para o nome de outra ativa — por exemplo, renomeie
   **22H** para **20H**.

**Esperado:** a mesma recusa. A regra vale na edição e na criação, não só na criação.

**Por que importa:** antes, a edição passava pelo domínio e só era barrada pelo índice único do
banco, que virava HTTP 500 sem informação nenhuma para quem estava preenchendo o formulário.

9. **Coluna sem horário.** Crie uma coluna deixando o campo **Horário real** vazio.

**Esperado:** ela é aceita e aparece no checklist, mas a ajuda do campo avisa que sem horário a
coluna **não gera notificação** — e ela não aparece entre os horários agendados na tela Dispositivo.

---

## 8. Encerramento do plantão

1. Menu → **Plantão**.

**Esperado:** o resumo com total, concluídas, pendentes e — item por item — os leitos com C.I.,
Sondas e Drenos.

2. Toque em **Encerrar plantão** com pendências.

**Esperado:** pede **confirmação explícita**, dizendo quantas tarefas ficaram por fazer.

3. Confirme.

**Esperado:** o checklist fica **somente leitura**, com faixa avisando que o plantão foi encerrado.

4. Teste **Reiniciar checklist** (antes de encerrar, em outro plantão).

**Esperado:** as marcações voltam a pendente e **as classificações permanecem**.

---

## 9. Permissões

1. **Administração → Usuários e grupos** → crie um grupo "Plantão simples" só com
   `checklist.view` e `checklist.update`, com acesso ao setor Oeste.
2. Crie um usuário nesse grupo.
3. Saia e entre com ele.

**Esperado:** consegue ver e marcar; **não** vê o menu Administração; não consegue encerrar o
plantão.

4. No PC, tente chamar um endpoint administrativo com o token dele.

**Esperado:** **403**. A interface esconde por conveniência; o servidor recusa por segurança.

---

## 10. Dois dispositivos

Deixou de ser opcional: é o critério 16, e o cliente do hub que faz o aviso chegar é código novo,
nunca visto funcionando fora dos testes.

Precisa do cliente Windows rodando junto com o celular:

```bash
dotnet build src/ChecklistPlantao.Client -f net10.0-windows10.0.19041.0 -c Debug -t:Run
```

Os dois precisam estar **no mesmo setor**. Usar a mesma conta `admin` nos dois é o teste mais
severo, não o mais frouxo: quem tem acesso a todos os setores não entra em nenhum grupo do
servidor automaticamente, e só recebe aviso porque o aplicativo pede a inscrição explicitamente.

1. Marque um leito no celular.

**Esperado:** aparece no Windows em segundos, sem ninguém tocar em "Sincronizar agora".

2. Marque outro leito no Windows.

**Esperado:** aparece no celular, também sozinho.

3. **Reconexão.** Com os dois abertos, encerre o servidor e suba de novo. Espere alguns segundos e
   marque um leito no Windows.

**Esperado:** o celular volta a receber sozinho. É o que prova que a reconexão refaz a inscrição
no setor — sem isso o aparelho reconecta mudo, que é a falha mais traiçoeira desta parte.

4. **Troca de setor.** Se houver mais de um setor, mude o setor no Windows e marque um leito no
   celular, no setor antigo.

**Esperado:** o Windows **não** se mexe. Ele só deve receber avisos do setor em que está.

5. **Conflito:** deixe o celular offline, marque o leito 1148 no Windows, e **desmarque** o mesmo
   leito no celular. Reconecte o celular.

**Esperado:** a **conclusão prevalece** — o leito continua marcado. O celular adota o estado do
servidor sem exibir mensagem técnica.

**Por que importa:** é a regra que garante que uma tarefa realmente feita nunca seja apagada por
uma operação antiga vinda de um aparelho que estava sem rede.

> Se o aviso não chegar mas a marcação aparecer ao tocar em "Sincronizar agora", o problema é o
> hub, não a sincronização. Vale anotar em qual dos cinco passos parou.

---

## 11. Navegação e recuperação de falha

Esta seção existe por causa dos defeitos da segunda rodada em campo. Vale percorrer inteira.

1. Circule pelas telas: Painel → Gelo → Classificações → Estado do dispositivo → Painel. Repita
   algumas vezes, incluindo o **botão voltar do Android**.

**Esperado:** o menu de navegação **nunca some**. "Painel" nunca leva de volta à tela de entrada
com você logado. Nenhuma tela de "O aplicativo precisa ser reiniciado".

O menu fica no **rodapé**. Ao abrir, a lista cresce **para cima** e o botão **não sai do lugar** —
o dedo continua onde estava. Toque fora para fechar sem escolher.

2. Role qualquer tela até o fim.

**Esperado:** o último cartão **não fica escondido atrás do menu**. E numa tela curta — Pendências
sem pendências, por exemplo — a página **não rola** sem ter o que rolar.

3. Desfaça uma marcação e observe a faixa **Desfazer**, que também aparece no rodapé.

**Esperado:** ela fica **acima** do menu, sem sobrepor.

4. Abra o menu e use o interruptor **Tema escuro**. Feche o aplicativo por completo e abra de novo.

**Esperado:** a troca é imediata, a tela **não pisca branca** ao reabrir, e o tema escolhido
sobreviveu. Em **Estado do dispositivo → Aparência**, a opção marcada acompanha o que você
escolheu no menu.

5. Com **Seguir o aparelho** marcado em Aparência, mude o modo escuro do próprio celular.

**Esperado:** o aplicativo acompanha, e o interruptor do menu mostra a posição **do tema em vigor**
— não a de "automático".

6. Toque no **nome do setor**, no canto superior esquerdo, e escolha outro.

**Esperado:** a lista mostra as pendências de cada setor; o setor atual traz a marca de conferido;
ao trocar aparece "Trocando de setor…" e o aplicativo volta ao Painel. Tocar no setor em que você
já está **não faz nada**. Com um setor só, o painel explica em vez de listar.

7. Navegue por teclado no Windows: `Tab` a partir do topo da tela.

**Esperado:** o **anel de foco branco** aparece no nome do setor e no botão de sincronizar — os
dois ficam sobre a barra escura. Nenhum controle recebe foco invisível.

8. Abra e feche telas por alguns minutos, observando o topo.

**Esperado:** o aviso *"O estado das notificações ainda não foi verificado"* **não aparece**. A
faixa de notificações só surge quando existe um problema real e verificado — permissão negada,
alarme exato indisponível. Quando aparecer, ela traz "Corrigir agora" e pode ser recolhida.

9. Se alguma tela falhar mesmo assim:

**Esperado:** aparece "Esta tela não pôde ser aberta", com **"Tentar de novo"**, **"Voltar ao
início"** e um detalhe técnico recolhido. O **menu continua ali** — dá para ir para outra tela sem
reiniciar o aplicativo. As marcações continuam salvas.

> Se isso acontecer, abra o detalhe técnico e me passe o texto: ele indica exatamente a tela e a
> causa.

---

## O que observar em tudo

- Nenhuma tela deve **afirmar** algo que não verificou ("Offline", "notificações ativas",
  "estado não verificado" como se fosse defeito).
- Nenhuma confirmação para marcar; confirmação **apenas** para ações destrutivas.
- Nenhum estado transmitido só por cor — sempre há `X`, texto ou ícone junto.
- Nada deve travar esperando a rede.
