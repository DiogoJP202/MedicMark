# Plano e resultado — Plantão, integridade e Administração

Status: **implementado em 20/08/2026** na branch
`codex/correcao-plantao-reformulacao-admin`.

| Etapa | Resultado | Commit |
|---|---|---|
| Regra central do resumo | Matriz esperada e filtros de dados inconsistentes | `2cc08ee` |
| Snapshot em todo o cliente | Sessão direta, quadro e alertas no mesmo universo | `cfea7de` |
| Tela Plantão | Estados, cards e recarga reativa segura | `f0d9104` |
| Sessão aberta única | Índice por setor, migração e conflito HTTP 409 | `4a2d299` |
| Base visual da Administração | Cabeçalho, ajuda, badges, responsividade e rodapé de modal | `a6ea842` |
| Administração principal | Setores/leitos, tipos/colunas e marcadores | `ab577a9` |
| Configurações avançadas | Assistentes de acesso e páginas avançadas responsivas | `87cec8a` |
| Documentação e qualificação | Documentação harmonizada e gates completos | commit desta etapa |

O gate automatizado medido ao final contém **587 testes, sem falhas**, e o build completo termina
com **0 erros e 0 avisos**, incluindo Android e Windows. O resultado detalhado e as validações
manuais já realizadas ficam em [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).

## Diagnóstico e estratégia

- A causa principal do falso “concluído” está no
  [`SessionSummary.cs`](../src/ChecklistPlantao.Domain/Operations/SessionSummary.cs): o resumo conta somente
  `ChecklistEntry` persistidos. Como uma célula intocada não possui registro, ela desaparece do total.
- O total correto será calculado por `leitos ativos na sessão × colunas ativas do tipo`. Registros ausentes
  representarão pendências.
- Servidor e modo offline já usam o mesmo calculador; a correção será centralizada nele.
  [`SessionService.cs`](../src/ChecklistPlantao.Application/Sessions/SessionService.cs) e
  [`LocalChecklistStore.cs`](../src/ChecklistPlantao.Client.Core/Services/LocalChecklistStore.cs) apenas fornecerão
  o snapshot correto da sessão.
- C.I., Sondas e Drenos já percorrem backend, sincronização local e DTO. O problema está ligado ao carregamento
  indireto do resumo e ao uso incorreto dos leitos atuais do setor, em vez dos leitos pertencentes à sessão.
- A implementação seguirá na branch `codex/correcao-plantao-reformulacao-admin`.

## Implementação

### 1. Correção funcional do Plantão

- Alterar o calculador para receber somente os leitos ativos da sessão e:
  - manter todo tipo ativo e aplicável que possua tarefas esperadas, mesmo sem nenhuma entrada persistida;
  - calcular cada coluna com o total de leitos da sessão;
  - contar como concluídas apenas entradas completas desses leitos;
  - ignorar entradas, marcadores, colunas ou tipos fora do snapshot ativo;
  - somar os mesmos resultados para tipo e resumo geral.
- Ajustar servidor, cálculo offline e montagem do quadro para utilizarem `SessionBed.IsActiveInSession`, mantendo
  resumo e checklist visualmente idênticos.
- Adicionar ao `IChecklistStore` uma consulta direta da sessão atual e um view model enxuto com identificador,
  setor, data e situação. A
  [`SessionPage.razor`](../src/ChecklistPlantao.UI/Pages/SessionPage.razor) deixará de abrir o primeiro tipo apenas
  para descobrir o ID da sessão.
- Centralizar em `ProgressDto` o estado calculado `NotStarted`, `Partial` ou `Completed`, sem alterar o payload
  HTTP. A interface traduzirá esses estados para “Pendente”, “Parcial” e “Concluído”.
- Exibir “Por tipo” em cards com estado, concluídos, total e pendentes. Um tipo sem marcações aparecerá como
  `0 de N — Pendente`.
- Exibir somente classificações que tenham leitos associados, agrupadas por C.I./Sonda/Dreno ou marcador
  correspondente, com os códigos dos leitos.
- Recarregar o resumo quando sessão, checklist, marcadores ou sincronização alterarem o armazenamento, cancelando
  as inscrições ao desmontar a página.
- Manter encerramento e reinício usando os modais existentes; a confirmação de encerramento passará a considerar
  o total corrigido.

### 2. Base visual da Administração

Criar componentes reutilizáveis antes de reformular as páginas:

- `AdminPageHeader`: título, descrição opcional, ação principal e botão voltar discreto.
- Navegação hierárquica fixa:
  - páginas principais → `/admin/sistema`;
  - páginas avançadas → `/admin/avancado`;
  - avançadas → `/admin/sistema`;
  - configurações do sistema → `/admin`.
- `HelpPopover`: aberto por clique/toque, acessível por teclado, com `aria-expanded`, fechamento por Escape ou
  perda de foco.
- Badges padronizados para ativo/inativo e estados do checklist.
- Padrão responsivo de listagem: tabela/lista compacta em desktop e cards empilhados abaixo de 900 px.
- Estender `FormDialog` com rodapé de ações customizável, preservando o comportamento atual para os modais
  existentes e permitindo Voltar/Continuar/Salvar nos fluxos em etapas.

### 3. Administração principal

- **Setores e Leitos**
  - Apresentar um card por setor, com nome, situação, ordem e turno.
  - Listar os leitos dentro do setor correspondente.
  - Manter “Novo setor” no cabeçalho e “Adicionar leito” no card do setor.
  - Reaproveitar os modais e confirmações atuais para criar, editar, desativar e reativar.

- **Tipos de Checklist**
  - Mostrar cards-resumo com nome, situação, setores atendidos e quantidade de colunas.
  - Usar uma seção expansível “Gerenciar colunas”, evitando exibir toda a configuração simultaneamente.
  - Manter modais separados para tipo e coluna.
  - Agrupar o modal de coluna em identificação/horário, notificação e repetição/adiamento.
  - Ocultar parâmetros de notificação quando “Notificar” estiver desligado e minutos de adiamento quando essa
    opção estiver desativada.
  - Usar ajuda contextual para antecedência, tolerância, repetições e adiamento.

- **Marcadores**
  - Trocar a tabela larga por uma listagem responsiva com nome, código estável, ordem e situação.
  - Manter criação e edição nos modais existentes.
  - Adicionar estado vazio, cabeçalho e ações consistentes.

### 4. Configurações avançadas

- **Grupos e usuários**
  - Remover os dois formulários permanentes.
  - Manter duas listagens de gerenciamento: grupos e usuários.
  - A ação principal “Adicionar” abrirá a escolha “Novo usuário” ou “Novo grupo”.
  - Usuário novo: identificação e senha → grupos e revisão.
  - Grupo novo ou editado: identificação → setores → permissões.
  - Edição de usuário: modal único com nome, situação e grupos; login somente leitura.
  - Redefinição de senha permanece separada e confirmada.
  - Cada etapa mostrará posição, Voltar, Continuar, Cancelar e Salvar; erros manterão dados e etapa atuais.

- **Notificações**
  - Separar em canais, conteúdo do alerta e opções avançadas do Android.
  - Substituir textos longos por popovers nos modelos de texto e alerta em tela cheia.
  - Manter uma única ação primária de salvar.

- **Plantão e acesso**
  - Substituir “Configurações gerais” no título e menu, preservando a rota `/admin/configuracoes`.
  - Separar horários/fuso, retenção, acesso offline e abertura automática.
  - Usar ajuda contextual para fuso IANA, virada do turno, retenção e limites offline.

- **Dispositivos**
  - Manter somente leitura e dentro das configurações avançadas.
  - Converter a observação sobre “último estado sincronizado” em ajuda contextual.
  - Usar tabela no desktop e cards no celular, preservando todos os campos atuais.

## Interfaces e testes

- Alterações públicas controladas:
  - nova consulta de sessão atual em `IChecklistStore`;
  - novo view model de sessão na camada cliente;
  - estado calculado no `ProgressDto`, ignorado na serialização;
  - rodapé customizável opcional no `FormDialog`.
- Nenhum endpoint HTTP ou DTO serializado foi alterado. Foi criada somente a migração do banco
  central `SessaoAbertaUnicaPorSetor`; o banco local permanece inalterado.
- Substituir o teste que hoje exige a omissão de tipos sem entradas.
- Cobrir:
  - tipo nunca iniciado, parcial e totalmente concluído;
  - múltiplos tipos e colunas;
  - leitos removidos da sessão;
  - paridade entre resumo online e offline;
  - C.I., Sonda, Dreno, múltiplos marcadores e marcadores desmarcados/inativos;
  - atualização da página após mudança ou sincronização;
  - destinos do botão voltar e permissões dos menus;
  - teclado e ARIA do popover;
  - todos os passos, retornos, cancelamentos e erros do fluxo de usuários/grupos;
  - preservação dos testes atuais de modais e administração.
  - disputa concorrente de abertura, ciclo `Up`/`Down` da migração e detecção operacional prévia
    de sessões abertas duplicadas.
- Validação final:
  - `dotnet test ChecklistPlantao.NoMaui.slnf -c Release --no-restore --nologo`;
  - `dotnet build ChecklistPlantao.sln -c Release --no-restore --nologo`;
  - inspeção visual em largura móvel e desktop, incluindo modais, listas extensas, tema claro/escuro e navegação
    por teclado.

## Premissas

- “Pendente” representa um checklist com tarefas esperadas e nenhuma concluída.
- Tipos sem leitos ou sem colunas ativas não serão apresentados como trabalho pendente.
- Apenas marcadores ativos e selecionados em leitos ativos da sessão aparecerão no Plantão.
- Permissões, contratos de salvamento, regras de sincronização e rotas existentes serão preservados.
