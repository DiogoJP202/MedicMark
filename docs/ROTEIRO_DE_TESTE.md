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

---

## 2. Filtros e progresso

1. Ative **Somente pendentes**.

**Esperado:** os leitos já marcados desaparecem da lista. Sobra só o que falta fazer.

2. Digite `1152` na busca.

**Esperado:** só o leito 1152.

3. Olhe as abas de horário.

**Esperado:** cada aba mostra quantos faltam ("8 pendentes"), e as concluídas mudam de aparência.
A barra de progresso mostra número explícito — "10 de 16 · 6 pendentes" —, não só a barra.

---

## 3. Classificações (C.I., Sondas, Drenos)

1. Menu → **Classificações**.
2. Marque **Sondas** e **Drenos** no mesmo leito.
3. Toque no botão de filtro **Sondas**.

**Esperado:** um leito aceita mais de uma classificação ao mesmo tempo; o filtro mostra só os
leitos com aquela marcação; a contagem ao lado do nome confere.

4. Volte ao checklist.

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

1. No app, entre em **Administração → Tipos de checklist**.
2. Edite uma coluna do Gelo e ponha o horário **3 minutos à frente** do relógio atual.
3. Salve, volte ao checklist e confirme que **há leitos pendentes** naquela coluna.
4. **Feche o aplicativo por completo.**
5. Espere.

**Esperado:** a notificação chega com o app fechado, dizendo o tipo, o horário e quantos leitos
faltam. Tocar nela abre o checklist **naquela coluna**.

**Se não chegar**, o culpado provável é a MIUI, não o código. Ative:
Ajustes → Apps → Checklist de Plantão → **Início automático**, e bateria em **Sem restrições**.
Ver [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md), item 4.

6. Marque **todos** os leitos daquela coluna e espere o horário da repetição.

**Esperado:** **nenhuma** repetição chega. Coluna concluída não incomoda mais.

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

## 10. Dois dispositivos (opcional)

Precisa do cliente Windows rodando junto com o celular:

```bash
dotnet build src/ChecklistPlantao.Client -f net10.0-windows10.0.19041.0 -c Debug -t:Run
```

1. Marque um leito no celular.

**Esperado:** aparece no Windows em segundos.

2. **Conflito:** deixe o celular offline, marque o leito 1148 no Windows, e **desmarque** o mesmo
   leito no celular. Reconecte o celular.

**Esperado:** a **conclusão prevalece** — o leito continua marcado. O celular adota o estado do
servidor sem exibir mensagem técnica.

**Por que importa:** é a regra que garante que uma tarefa realmente feita nunca seja apagada por
uma operação antiga vinda de um aparelho que estava sem rede.

---

## O que observar em tudo

- Nenhuma tela deve **afirmar** algo que não verificou ("Offline", "notificações ativas").
- Nenhuma confirmação para marcar; confirmação **apenas** para ações destrutivas.
- Nenhum estado transmitido só por cor — sempre há `X`, texto ou ícone junto.
- Nada deve travar esperando a rede.
