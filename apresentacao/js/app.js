/*
    Apresentação do ChecklistPlantão — front-end puro, sem servidor.

    Reproduz as telas do aplicativo real usando o mesmo design system e a mesma estrutura de
    marcação. O login foi deliberadamente pulado: a demonstração já começa autenticada no setor
    Oeste. Toda alteração vive em memória e some ao recarregar a página.
*/

const TELAS = [
    { rota: "painel", titulo: "Painel", render: renderPainel },
    { rota: "checklist", titulo: "Checklist", render: renderChecklist },
    { rota: "classificacoes", titulo: "Classificações", render: renderClassificacoes },
    { rota: "pendencias", titulo: "Pendências", render: renderPendencias },
    { rota: "sessao", titulo: "Plantão", render: renderSessao },
    { rota: "dispositivo", titulo: "Dispositivo", render: renderDispositivo },
    { rota: "admin", titulo: "Administração", render: renderAdmin },
];

/* Estado só da navegação e dos filtros — o estado do domínio mora em dados.js. */
const ui = {
    rota: "painel",
    templateAtual: "gelo",
    colunaAtual: "gelo-20",
    busca: "",
    somentePendentes: false,
    marcadorFiltrado: null,
    filtroClassificacao: null,
    celulasBloqueadas: new Set(),
    desfazer: null,
};

/* ------------------------------------------------------------------ utilidades */

function el(html) {
    const molde = document.createElement("template");
    molde.innerHTML = html.trim();
    return molde.content.firstElementChild;
}

function escapar(texto) {
    return String(texto).replace(/[&<>"']/g, (c) =>
        ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

function plural(n, singular, pluralForma) {
    return n === 1 ? singular : pluralForma;
}

/** Barra de progresso com número explícito — a barra sozinha não diz quantos leitos faltam. */
function progressoHtml(rotulo, total, feitos) {
    const pendentes = Math.max(0, total - feitos);
    const percentual = total === 0 ? 0 : Math.round((feitos / total) * 100);

    return `
        <div class="progresso">
            <div class="progresso__rotulo">
                <span>${escapar(rotulo)}</span>
                <span>${feitos} de ${total}${pendentes > 0
                    ? ` · ${pendentes} pendente${plural(pendentes, "", "s")}`
                    : " · tudo feito"}</span>
            </div>
            <div class="progresso__trilha" role="progressbar" aria-valuemin="0" aria-valuemax="${total}" aria-valuenow="${feitos}" aria-label="${escapar(rotulo)}">
                <div class="progresso__barra ${pendentes === 0 ? "progresso__barra--completo" : ""}" style="width:${percentual}%"></div>
            </div>
        </div>`;
}

/* ------------------------------------------------------------------ casca */

function renderNav() {
    const nav = document.getElementById("nav");
    nav.innerHTML = "";

    for (const tela of TELAS) {
        const item = el(`<button type="button" class="nav-principal__item ${tela.rota === ui.rota ? "active" : ""}">${escapar(tela.titulo)}</button>`);
        item.addEventListener("click", () => irPara(tela.rota));
        nav.appendChild(item);
    }
}

function renderTopo() {
    document.getElementById("topo-titulo").textContent = estado.setor;

    const sync = estado.sincronizacao;
    const indicador = document.getElementById("indicador-sync");
    const texto = document.getElementById("indicador-sync-texto");

    if (sync.sincronizando) {
        indicador.className = "distintivo";
        indicador.querySelector("span").textContent = "⟳";
        texto.textContent = "Sincronizando…";
        return;
    }

    if (sync.pendentes > 0) {
        indicador.className = "distintivo distintivo--alerta";
        indicador.querySelector("span").textContent = "▲";
        texto.textContent = `${sync.pendentes} aguardando`;
        return;
    }

    indicador.className = "distintivo distintivo--sucesso";
    indicador.querySelector("span").textContent = "●";
    texto.textContent = "Tudo sincronizado";
}

/**
 * A faixa de notificações só aparece quando há problema REAL e já verificado. "Não medido" não
 * é defeito — é a regra que o sistema segue para nunca afirmar o que não checou.
 */
function renderFaixaNotificacoes() {
    const area = document.getElementById("faixa-notificacoes");
    const n = estado.notificacoes;

    if (!n.verificado || n.problemas.length === 0) {
        area.innerHTML = "";
        return;
    }

    area.innerHTML = `
        <div class="faixa faixa--erro faixa--fixa" role="alert">
            <span class="faixa__icone" aria-hidden="true">▲</span>
            <div class="faixa__conteudo">
                <p class="faixa__titulo">ATENÇÃO: os alertas podem não chegar</p>
                ${n.problemas.map((p) => `<p class="faixa__texto">${escapar(p)}</p>`).join("")}
            </div>
        </div>`;
}

function irPara(rota) {
    ui.rota = rota;
    render();
    window.scrollTo({ top: 0 });
}

function render() {
    renderTopo();
    renderNav();
    renderFaixaNotificacoes();

    const conteudo = document.getElementById("conteudo");
    conteudo.innerHTML = "";

    TELAS.find((t) => t.rota === ui.rota).render(conteudo);
}

/* ------------------------------------------------------------------ painel */

function renderPainel(raiz) {
    const geral = progressoGeral();
    const grupos = gruposDePendencia();
    const atrasadas = grupos.filter((g) => g.atrasado).reduce((soma, g) => soma + g.leitos.length, 0);

    const pilha = el('<div class="pilha"></div>');

    if (atrasadas > 0) {
        const descricao = grupos
            .filter((g) => g.atrasado)
            .map((g) => `${g.templateNome} ${g.colunaNome}`)
            .join(", ");

        const faixa = el(`
            <button type="button" class="faixa faixa--alerta" style="width:100%;text-align:left;cursor:pointer">
                <span class="faixa__icone" aria-hidden="true">!</span>
                <span class="faixa__conteudo">
                    <span class="faixa__titulo">${atrasadas} TAREFA${plural(atrasadas, "", "S")} ATRASADA${plural(atrasadas, "", "S")}</span>
                    <span class="faixa__texto">${escapar(descricao)}</span>
                    <span class="faixa__texto">Toque para visualizar</span>
                </span>
            </button>`);

        faixa.addEventListener("click", () => irPara("pendencias"));
        pilha.appendChild(faixa);
    }

    pilha.appendChild(el(`
        <div class="cartao">
            <h2 class="cartao__titulo">${escapar(estado.setor)}</h2>
            <p class="faixa__texto">Olá, ${escapar(estado.usuario)}.</p>
            ${progressoHtml("Progresso do plantão", geral.total, geral.feitos)}
        </div>`));

    pilha.appendChild(el("<h3>Tipos de checklist</h3>"));

    const grade = el('<div class="grade-cartoes"></div>');

    for (const template of TEMPLATES) {
        const p = progressoDoTemplate(template.id);

        const cartao = el(`
            <button type="button" class="cartao cartao--acionavel">
                <span class="cartao__titulo">${escapar(template.nome)}</span>
                <span class="linha">
                    ${p.pendentes === 0
                        ? '<span class="distintivo distintivo--sucesso">✓ Tudo feito</span>'
                        : `<span class="distintivo distintivo--alerta">${p.pendentes} pendente${plural(p.pendentes, "", "s")}</span>`}
                    <span class="distintivo">${template.colunas.length} horário(s)</span>
                </span>
            </button>`);

        cartao.addEventListener("click", () => {
            ui.templateAtual = template.id;
            ui.colunaAtual = template.colunas[0].id;
            irPara("checklist");
        });

        grade.appendChild(cartao);
    }

    pilha.appendChild(grade);

    const atalhos = el('<div class="grade-cartoes"></div>');

    const cartoes = [
        { titulo: "Classificações", texto: "C.I., Sondas, Drenos e outros marcadores do plantão.", rota: "classificacoes" },
        { titulo: "Plantão", texto: "Abrir, encerrar ou reiniciar o checklist.", rota: "sessao" },
        {
            titulo: "Estado do dispositivo",
            texto: estado.notificacoes.problemas.length === 0
                ? 'Notificações: <span class="distintivo distintivo--sucesso">✓ em ordem</span>'
                : 'Notificações: <span class="distintivo distintivo--erro">▲ atenção</span>',
            rota: "dispositivo",
        },
    ];

    for (const c of cartoes) {
        const cartao = el(`
            <button type="button" class="cartao cartao--acionavel">
                <span class="cartao__titulo">${escapar(c.titulo)}</span>
                <span class="faixa__texto">${c.texto}</span>
            </button>`);

        cartao.addEventListener("click", () => irPara(c.rota));
        atalhos.appendChild(cartao);
    }

    pilha.appendChild(atalhos);
    raiz.appendChild(pilha);
}

/* ------------------------------------------------------------------ checklist */

function leitosVisiveis(colunaId) {
    return LEITOS.filter((leito) => {
        if (ui.busca && !leito.includes(ui.busca)) {
            return false;
        }

        if (ui.somentePendentes && estaMarcado(colunaId, leito)) {
            return false;
        }

        if (ui.marcadorFiltrado && !(estado.classificacoes[leito] || []).includes(ui.marcadorFiltrado)) {
            return false;
        }

        return true;
    });
}

/** Igual ao aplicativo: a linha some da grade se nenhuma das colunas passar no filtro. */
function linhasVisiveisDaGrade(template) {
    return LEITOS.filter((leito) => {
        if (ui.busca && !leito.includes(ui.busca)) {
            return false;
        }

        if (ui.marcadorFiltrado && !(estado.classificacoes[leito] || []).includes(ui.marcadorFiltrado)) {
            return false;
        }

        if (ui.somentePendentes) {
            return template.colunas.some((c) => !estaMarcado(c.id, leito));
        }

        return true;
    });
}

function renderChecklist(raiz) {
    const template = TEMPLATES.find((t) => t.id === ui.templateAtual);

    if (!template.colunas.some((c) => c.id === ui.colunaAtual)) {
        ui.colunaAtual = template.colunas[0].id;
    }

    const pilha = el('<div class="pilha"></div>');

    /* --- troca de tipo + progresso --- */
    const cabecalho = el('<div class="linha linha--entre"></div>');
    const tipos = el('<div class="linha"></div>');

    for (const t of TEMPLATES) {
        const botao = el(`<button type="button" class="botao ${t.id === template.id ? "botao--primario" : "botao--contorno"}">${escapar(t.nome)}</button>`);

        botao.addEventListener("click", () => {
            ui.templateAtual = t.id;
            ui.colunaAtual = t.colunas[0].id;
            render();
        });

        tipos.appendChild(botao);
    }

    const p = progressoDoTemplate(template.id);
    cabecalho.appendChild(tipos);
    cabecalho.appendChild(el(progressoHtml(template.nome, p.total, p.feitos)));
    pilha.appendChild(cabecalho);

    /* --- busca e somente pendentes --- */
    const filtros = el(`
        <div class="linha">
            <div class="campo" style="flex:1 1 200px;margin:0">
                <label class="campo__rotulo" for="busca-leito">Pesquisar leito</label>
                <input id="busca-leito" class="campo__controle" type="search" inputmode="numeric"
                       placeholder="Número do leito" value="${escapar(ui.busca)}" />
            </div>
            <label class="campo campo--horizontal" style="margin:0">
                <input type="checkbox" style="width:24px;height:24px" ${ui.somentePendentes ? "checked" : ""} />
                <span class="campo__rotulo">Somente pendentes</span>
            </label>
        </div>`);

    const campoBusca = filtros.querySelector("input[type=search]");
    campoBusca.addEventListener("input", (e) => {
        ui.busca = e.target.value.trim();
        render();
        const novo = document.getElementById("busca-leito");
        novo.focus();
        novo.setSelectionRange(novo.value.length, novo.value.length);
    });

    filtros.querySelector("input[type=checkbox]").addEventListener("change", (e) => {
        ui.somentePendentes = e.target.checked;
        render();
    });

    pilha.appendChild(filtros);

    /* --- filtro por classificação, com a contagem de cada marcador --- */
    const comMarcadores = MARCADORES
        .map((m) => ({ ...m, quantidade: LEITOS.filter((l) => (estado.classificacoes[l] || []).includes(m.id)).length }))
        .filter((m) => m.quantidade > 0);

    if (comMarcadores.length > 0) {
        const linha = el('<div class="linha" role="group" aria-label="Filtrar por classificação"></div>');

        const todos = el(`<button type="button" class="botao ${ui.marcadorFiltrado === null ? "botao--primario" : "botao--contorno"}">Todos</button>`);
        todos.addEventListener("click", () => { ui.marcadorFiltrado = null; render(); });
        linha.appendChild(todos);

        for (const m of comMarcadores) {
            const botao = el(`<button type="button" class="botao ${ui.marcadorFiltrado === m.id ? "botao--primario" : "botao--contorno"}">${escapar(m.nome)} (${m.quantidade})</button>`);
            botao.addEventListener("click", () => { ui.marcadorFiltrado = m.id; render(); });
            linha.appendChild(botao);
        }

        pilha.appendChild(linha);
    }

    pilha.appendChild(renderListaCelular(template));
    pilha.appendChild(renderGradeDesktop(template));

    raiz.appendChild(pilha);
}

/** Celular: uma coluna por vez, alvo de toque grande, sem rolagem horizontal. */
function renderListaCelular(template) {
    const area = el('<div class="somente-mobile"></div>');

    const abas = el('<div class="seletor-colunas" role="tablist" aria-label="Horários do checklist"></div>');

    for (const coluna of template.colunas) {
        const p = progressoDaColuna(coluna.id);
        const completo = p.pendentes === 0;
        const atrasado = COLUNAS_ATRASADAS.has(coluna.id) && !completo;

        const aba = el(`
            <button type="button" role="tab"
                    class="seletor-colunas__item ${coluna.id === ui.colunaAtual ? "seletor-colunas__item--ativo" : ""} ${completo ? "seletor-colunas__item--completo" : ""}"
                    aria-selected="${coluna.id === ui.colunaAtual}">
                <span>${escapar(coluna.nome)} · ${coluna.hora}</span>
                <span class="seletor-colunas__pendentes">
                    ${completo ? "completo" : `${p.pendentes} pendente${plural(p.pendentes, "", "s")}`}${atrasado ? " · atrasado" : ""}
                </span>
            </button>`);

        aba.addEventListener("click", () => { ui.colunaAtual = coluna.id; render(); });
        abas.appendChild(aba);
    }

    area.appendChild(abas);

    const coluna = template.colunas.find((c) => c.id === ui.colunaAtual);
    const pc = progressoDaColuna(coluna.id);
    area.appendChild(el(progressoHtml(`${template.nome} · ${coluna.nome}`, pc.total, pc.feitos)));

    const visiveis = leitosVisiveis(coluna.id);

    if (visiveis.length === 0) {
        area.appendChild(el(`
            <div class="estado-vazio">
                <span class="estado-vazio__icone" aria-hidden="true">✓</span>
                <p class="estado-vazio__titulo">${ui.somentePendentes ? "Nada pendente aqui" : "Nenhum leito encontrado"}</p>
                <p>${ui.somentePendentes ? "Todos os leitos deste horário já foram marcados." : "Nenhum leito corresponde à busca."}</p>
            </div>`));

        return area;
    }

    const lista = el('<ul class="lista-leitos"></ul>');

    for (const leito of visiveis) {
        const marcado = estaMarcado(coluna.id, leito);
        const atrasado = COLUNAS_ATRASADAS.has(coluna.id) && !marcado;
        const chave = `${coluna.id}:${leito}`;
        const bloqueada = ui.celulasBloqueadas.has(chave);

        const marcadores = marcadoresDoLeito(leito)
            .map((m) => `<span class="distintivo">${escapar(m.nome)}</span>`)
            .join("");

        const item = el(`
            <li class="lista-leitos__item">
                <button type="button"
                        class="leito-botao ${marcado ? "leito-botao--marcado" : ""} ${atrasado ? "leito-botao--atrasado" : ""}"
                        aria-pressed="${marcado}"
                        aria-label="Leito ${leito}: ${marcado ? "realizado" : "não realizado"}"
                        ${bloqueada ? "disabled" : ""}>
                    <span class="leito-botao__caixa" aria-hidden="true">${marcado ? "✕" : ""}</span>
                    <span class="leito-botao__codigo">${leito}</span>
                    ${marcadores ? `<span class="leito-botao__marcadores">${marcadores}</span>` : ""}
                </button>
            </li>`);

        item.querySelector("button").addEventListener("click", () => marcar(coluna.id, leito, chave));
        lista.appendChild(item);
    }

    area.appendChild(lista);
    return area;
}

/** Desktop: a matriz da folha de papel, com coluna de leito e cabeçalho fixos. */
function renderGradeDesktop(template) {
    const area = el('<div class="somente-desktop"></div>');
    const linhas = linhasVisiveisDaGrade(template);

    if (linhas.length === 0) {
        area.appendChild(el(`
            <div class="estado-vazio">
                <span class="estado-vazio__icone" aria-hidden="true">✓</span>
                <p class="estado-vazio__titulo">Nenhum leito para mostrar</p>
                <p>${ui.somentePendentes ? "Não há pendências com os filtros atuais." : "Nenhum leito corresponde à busca."}</p>
            </div>`));

        return area;
    }

    const cabecalhos = template.colunas.map((c) => {
        const p = progressoDaColuna(c.id);
        return `
            <th scope="col">
                ${escapar(c.nome)}
                <span class="grade-checklist__hora">${c.hora}</span>
                <span class="grade-checklist__hora">${p.pendentes === 0 ? "completo" : `${p.pendentes} pend.`}</span>
            </th>`;
    }).join("");

    const corpo = linhas.map((leito) => {
        const completa = template.colunas.every((c) => estaMarcado(c.id, leito));
        const nomes = marcadoresDoLeito(leito).map((m) => m.nome);

        const celulas = template.colunas.map((c) => {
            const marcado = estaMarcado(c.id, leito);
            const atrasado = COLUNAS_ATRASADAS.has(c.id) && !marcado;
            const chave = `${c.id}:${leito}`;

            return `
                <td>
                    <button type="button"
                            class="celula ${marcado ? "celula--marcada" : ""} ${atrasado ? "celula--atrasada" : ""}"
                            aria-pressed="${marcado}"
                            aria-label="Leito ${leito}, ${escapar(c.nome)}: ${marcado ? "realizado" : "não realizado"}"
                            data-chave="${chave}" data-coluna="${c.id}" data-leito="${leito}"
                            ${ui.celulasBloqueadas.has(chave) ? "disabled" : ""}>${marcado ? "✕" : ""}</button>
                </td>`;
        }).join("");

        return `
            <tr class="${completa ? "grade-checklist__linha--completa" : "grade-checklist__linha--pendente"}">
                <th scope="row" class="grade-checklist__leito">
                    ${leito}
                    ${nomes.length > 0 ? `<span class="grade-checklist__hora">${escapar(nomes.join(" · "))}</span>` : ""}
                </th>
                ${celulas}
            </tr>`;
    }).join("");

    const tabela = el(`
        <div class="grade-checklist__area">
            <table class="grade-checklist">
                <caption class="visualmente-oculto">
                    Checklist ${escapar(template.nome)} do setor ${escapar(estado.setor)}.
                    Use Tab para navegar e Espaço ou Enter para marcar.
                </caption>
                <thead>
                    <tr><th scope="col" class="grade-checklist__leito">Leito</th>${cabecalhos}</tr>
                </thead>
                <tbody>${corpo}</tbody>
            </table>
        </div>`);

    for (const botao of tabela.querySelectorAll(".celula")) {
        botao.addEventListener("click", () =>
            marcar(botao.dataset.coluna, botao.dataset.leito, botao.dataset.chave));
    }

    area.appendChild(tabela);
    return area;
}

/**
 * Marca ou desmarca. A célula muda ANTES de qualquer simulação de rede — é o requisito central
 * da tela — e fica travada por 1 segundo, só ela, para o toque repetido não alternar sem querer.
 */
function marcar(colunaId, leito, chave) {
    if (ui.celulasBloqueadas.has(chave)) {
        return;
    }

    const agoraMarcado = alternarMarcacao(colunaId, leito);

    ui.celulasBloqueadas.add(chave);
    setTimeout(() => { ui.celulasBloqueadas.delete(chave); render(); }, 1000);

    simularEnvio();
    mostrarDesfazer(colunaId, leito, agoraMarcado);
    render();
}

/** A fila sobe e desce como no aplicativo: a marcação é local, o envio vem depois. */
function simularEnvio() {
    estado.sincronizacao.pendentes += 1;
    renderTopo();

    setTimeout(() => {
        estado.sincronizacao.pendentes = Math.max(0, estado.sincronizacao.pendentes - 1);
        renderTopo();
    }, 1400);
}

function mostrarDesfazer(colunaId, leito, marcado) {
    const area = document.getElementById("toast");
    clearTimeout(ui.desfazer);

    area.innerHTML = `
        <div class="toast-desfazer" role="status">
            <span>Leito ${leito} ${marcado ? "marcado" : "desmarcado"}.</span>
            <button type="button" class="toast-desfazer__acao">Desfazer</button>
        </div>`;

    area.querySelector("button").addEventListener("click", () => {
        alternarMarcacao(colunaId, leito);
        area.innerHTML = "";
        render();
    });

    ui.desfazer = setTimeout(() => { area.innerHTML = ""; }, 5000);
}

/* ------------------------------------------------------------------ classificações */

function renderClassificacoes(raiz) {
    raiz.appendChild(el("<h2>Classificações dos leitos</h2>"));
    raiz.appendChild(el("<p>Estas marcações valem apenas para o plantão atual. Um leito pode ter mais de uma.</p>"));

    const filtros = el('<div class="linha" role="group" aria-label="Filtrar por classificação"></div>');

    const todos = el(`<button type="button" class="botao ${ui.filtroClassificacao === null ? "botao--primario" : "botao--contorno"}">Todos os leitos</button>`);
    todos.addEventListener("click", () => { ui.filtroClassificacao = null; render(); });
    filtros.appendChild(todos);

    for (const m of MARCADORES) {
        const quantidade = LEITOS.filter((l) => (estado.classificacoes[l] || []).includes(m.id)).length;
        const botao = el(`<button type="button" class="botao ${ui.filtroClassificacao === m.id ? "botao--primario" : "botao--contorno"}">${escapar(m.nome)} (${quantidade})</button>`);
        botao.addEventListener("click", () => { ui.filtroClassificacao = m.id; render(); });
        filtros.appendChild(botao);
    }

    raiz.appendChild(filtros);

    const visiveis = ui.filtroClassificacao === null
        ? LEITOS
        : LEITOS.filter((l) => (estado.classificacoes[l] || []).includes(ui.filtroClassificacao));

    const pilha = el('<div class="pilha" style="margin-top:var(--esp-4)"></div>');

    for (const leito of visiveis) {
        const selecionados = estado.classificacoes[leito] || [];

        const caixas = MARCADORES.map((m) => `
            <label class="campo campo--horizontal" style="margin:0;min-height:var(--toque-min)">
                <input type="checkbox" style="width:26px;height:26px" data-leito="${leito}" data-marcador="${m.id}"
                       ${selecionados.includes(m.id) ? "checked" : ""} />
                <span>${escapar(m.nome)}</span>
            </label>`).join("");

        const cartao = el(`
            <div class="cartao">
                <h3 class="cartao__titulo">Leito ${leito}</h3>
                <div class="linha">${caixas}</div>
            </div>`);

        /*
            Atualiza só esta linha, sem recarregar a tela: recarregar devolvia a rolagem ao topo
            e atrapalhava quem estava percorrendo os leitos — foi um defeito corrigido em campo.
        */
        for (const caixa of cartao.querySelectorAll("input")) {
            caixa.addEventListener("change", (e) => {
                const lista = estado.classificacoes[leito] || (estado.classificacoes[leito] = []);
                const marcador = e.target.dataset.marcador;
                const posicao = lista.indexOf(marcador);

                if (e.target.checked && posicao < 0) {
                    lista.push(marcador);
                } else if (!e.target.checked && posicao >= 0) {
                    lista.splice(posicao, 1);
                }

                simularEnvio();
            });
        }

        pilha.appendChild(cartao);
    }

    raiz.appendChild(pilha);
    raiz.appendChild(el('<p class="demo-nota">Ao marcar, a rolagem permanece onde está — só a linha tocada muda.</p>'));
}

/* ------------------------------------------------------------------ pendências */

function renderPendencias(raiz) {
    raiz.appendChild(el("<h2>Pendências do plantão</h2>"));

    const grupos = gruposDePendencia();

    if (grupos.length === 0) {
        raiz.appendChild(el(`
            <div class="estado-vazio">
                <span class="estado-vazio__icone" aria-hidden="true">✓</span>
                <p class="estado-vazio__titulo">Nada pendente</p>
                <p>Todas as tarefas deste plantão foram marcadas.</p>
            </div>`));

        return;
    }

    const pilha = el('<div class="pilha"></div>');

    /* Atrasadas primeiro: é o que precisa de ação agora. */
    for (const grupo of [...grupos].sort((a, b) => Number(b.atrasado) - Number(a.atrasado))) {
        const cartao = el(`
            <div class="cartao">
                <div class="linha linha--entre">
                    <h3 class="cartao__titulo">${escapar(grupo.templateNome)} · ${escapar(grupo.colunaNome)} (${grupo.hora})</h3>
                    ${grupo.atrasado
                        ? '<span class="distintivo distintivo--erro">▲ atrasado</span>'
                        : '<span class="distintivo">no prazo</span>'}
                </div>
                <p class="faixa__texto">${grupo.leitos.length} leito${plural(grupo.leitos.length, "", "s")}: ${escapar(grupo.leitos.join(", "))}</p>
                <div class="linha" style="margin-top:var(--esp-3)">
                    <button type="button" class="botao botao--primario">Abrir este horário</button>
                </div>
            </div>`);

        cartao.querySelector("button").addEventListener("click", () => {
            ui.templateAtual = grupo.templateId;
            ui.colunaAtual = grupo.colunaId;
            ui.somentePendentes = true;
            irPara("checklist");
        });

        pilha.appendChild(cartao);
    }

    raiz.appendChild(pilha);
}

/* ------------------------------------------------------------------ plantão */

function renderSessao(raiz) {
    const geral = progressoGeral();

    raiz.appendChild(el("<h2>Plantão</h2>"));

    const pilha = el('<div class="pilha"></div>');

    pilha.appendChild(el(`
        <div class="cartao">
            <h3 class="cartao__titulo">Plantão aberto</h3>
            <table class="tabela">
                <tbody>
                    <tr><th>Setor</th><td>${escapar(estado.setor)}</td></tr>
                    <tr><th>Data de serviço</th><td>${estado.dataDoPlantao}</td></tr>
                    <tr><th>Janela do turno</th><td>${estado.janelaDoTurno}</td></tr>
                    <tr><th>Leitos ativos</th><td>${LEITOS.length}</td></tr>
                </tbody>
            </table>
            <div style="margin-top:var(--esp-4)">
                ${progressoHtml("Progresso do plantão", geral.total, geral.feitos)}
            </div>
        </div>`));

    pilha.appendChild(el(`
        <div class="cartao">
            <h3 class="cartao__titulo">Resumo por tipo</h3>
            <div class="tabela__area">
                <table class="tabela">
                    <thead><tr><th>Tipo</th><th>Horários</th><th>Feitos</th><th>Pendentes</th></tr></thead>
                    <tbody>
                        ${TEMPLATES.map((t) => {
                            const p = progressoDoTemplate(t.id);
                            return `<tr><td>${escapar(t.nome)}</td><td>${t.colunas.length}</td><td>${p.feitos}</td><td>${p.pendentes}</td></tr>`;
                        }).join("")}
                    </tbody>
                </table>
            </div>
        </div>`));

    pilha.appendChild(el(`
        <div class="cartao">
            <h3 class="cartao__titulo">Encerrar o plantão</h3>
            <p class="faixa__texto">
                Encerrar exige servidor: é decisão do plantão, com efeito em todos os aparelhos.
                Com pendências, o sistema pede confirmação explícita.
            </p>
            <div class="linha" style="margin-top:var(--esp-3)">
                <button type="button" class="botao botao--perigo" disabled>Encerrar plantão</button>
                <button type="button" class="botao botao--contorno" disabled>Reiniciar marcações</button>
            </div>
            <p class="demo-nota">Desativado nesta demonstração — são ações destrutivas.</p>
        </div>`));

    raiz.appendChild(pilha);
}

/* ------------------------------------------------------------------ dispositivo */

function renderDispositivo(raiz) {
    const n = estado.notificacoes;

    raiz.appendChild(el("<h2>Estado do dispositivo</h2>"));

    const item = (rotulo, ok, detalhe) => `
        <tr>
            <th>${escapar(rotulo)}</th>
            <td>${ok
                ? '<span class="distintivo distintivo--sucesso">✓ em ordem</span>'
                : '<span class="distintivo distintivo--erro">▲ atenção</span>'}</td>
            <td>${escapar(detalhe)}</td>
        </tr>`;

    const pilha = el('<div class="pilha"></div>');

    pilha.appendChild(el(`
        <div class="cartao">
            <h3 class="cartao__titulo">Notificações</h3>
            <div class="tabela__area">
                <table class="tabela">
                    <tbody>
                        ${item("Permissão de notificações", n.permissaoConcedida, "Concedida pelo usuário")}
                        ${item("Alarme exato", n.alarmeExato, "O horário será respeitado")}
                        ${item("Som", n.som, "Canal de alta importância")}
                        ${item("Vibração", n.vibracao, "Ativa")}
                    </tbody>
                </table>
            </div>
            <div class="linha" style="margin-top:var(--esp-4)">
                <button type="button" class="botao botao--contorno" id="btn-testar">Testar notificação</button>
                <button type="button" class="botao botao--texto" id="btn-simular">Simular problema</button>
            </div>
        </div>`));

    pilha.appendChild(el(`
        <div class="cartao">
            <h3 class="cartao__titulo">Conexão e sincronização</h3>
            <div class="tabela__area">
                <table class="tabela">
                    <tbody>
                        <tr><th>Servidor</th><td>http://localhost:5000</td></tr>
                        <tr><th>Estado</th><td><span class="distintivo distintivo--sucesso">● Online</span></td></tr>
                        <tr><th>Última sincronização</th><td>${escapar(estado.sincronizacao.ultimaSincronizacao)}</td></tr>
                        <tr><th>Aguardando envio</th><td id="celula-pendentes">${estado.sincronizacao.pendentes}</td></tr>
                        <tr><th>Fuso horário</th><td>America/Sao_Paulo</td></tr>
                    </tbody>
                </table>
            </div>
        </div>`));

    pilha.appendChild(el(`
        <div class="cartao">
            <h3 class="cartao__titulo">Horários agendados</h3>
            <p class="faixa__texto">Próximos alertas deste plantão, calculados a partir dos horários das colunas.</p>
            <div class="tabela__area">
                <table class="tabela">
                    <thead><tr><th>Tipo</th><th>Horário</th><th>Quando</th></tr></thead>
                    <tbody>
                        ${TEMPLATES.flatMap((t) => t.colunas.map((c) => `
                            <tr>
                                <td>${escapar(t.nome)}</td>
                                <td>${escapar(c.nome)}</td>
                                <td>${c.hora}${["00:00", "02:00", "04:00", "06:00", "07:00"].includes(c.hora) ? " (dia seguinte)" : ""}</td>
                            </tr>`)).join("")}
                    </tbody>
                </table>
            </div>
            <p class="demo-nota">Horários entre 00:00 e 07:00 caem no dia seguinte — é a regra do turno 19:00 → 07:00.</p>
        </div>`));

    raiz.appendChild(pilha);

    document.getElementById("btn-testar").addEventListener("click", () => {
        alert("Teste do Checklist de Plantão\n\nSe você está vendo isto, as notificações funcionam neste aparelho.");
    });

    /* Mostra a faixa de alerta ao vivo — é o comportamento que mais aparece na apresentação. */
    document.getElementById("btn-simular").addEventListener("click", () => {
        const comProblema = n.problemas.length === 0;

        n.permissaoConcedida = !comProblema;
        n.alarmeExato = !comProblema;
        n.problemas = comProblema
            ? ["Permissão de notificações negada.", "Alarmes exatos não permitidos: o horário pode não ser exato."]
            : [];

        render();
    });
}

/* ------------------------------------------------------------------ administração */

function renderAdmin(raiz) {
    raiz.appendChild(el("<h2>Administração</h2>"));
    raiz.appendChild(el("<p>Cadastro de setores, leitos, tipos de checklist, marcadores e acessos.</p>"));

    const pilha = el('<div class="pilha"></div>');

    pilha.appendChild(el(`
        <div class="cartao">
            <h3 class="cartao__titulo">Tipos de checklist</h3>
            <div class="tabela__area">
                <table class="tabela">
                    <thead><tr><th>Coluna</th><th>Horário</th><th>Ordem</th><th>Notifica</th><th></th></tr></thead>
                    <tbody>
                        ${TEMPLATES.flatMap((t) => t.colunas.map((c, i) => `
                            <tr>
                                <td><strong>${escapar(t.nome)}</strong> · ${escapar(c.nome)}</td>
                                <td>${c.hora}</td>
                                <td>${(i + 1) * 10}</td>
                                <td><span class="distintivo distintivo--sucesso">✓ sim</span></td>
                                <td><button type="button" class="botao botao--texto" disabled>Editar</button></td>
                            </tr>`)).join("")}
                    </tbody>
                </table>
            </div>
            <p class="demo-nota">É aqui que "Jantar" deixa de ser um rótulo: o horário real é o que gera a notificação.</p>
        </div>`));

    pilha.appendChild(el(`
        <div class="cartao">
            <h3 class="cartao__titulo">Setores e leitos</h3>
            <div class="tabela__area">
                <table class="tabela">
                    <thead><tr><th>Setor</th><th>Leitos ativos</th><th>Turno</th></tr></thead>
                    <tbody>
                        <tr><td>${escapar(estado.setor)}</td><td>${LEITOS.length}</td><td>${escapar(estado.janelaDoTurno)}</td></tr>
                    </tbody>
                </table>
            </div>
            <p class="faixa__texto" style="margin-top:var(--esp-3)">
                ${LEITOS.join(" · ")}
            </p>
        </div>`));

    pilha.appendChild(el(`
        <div class="cartao">
            <h3 class="cartao__titulo">Marcadores</h3>
            <div class="tabela__area">
                <table class="tabela">
                    <thead><tr><th>Nome</th><th>Código</th><th>Leitos no plantão</th></tr></thead>
                    <tbody>
                        ${MARCADORES.map((m) => `
                            <tr>
                                <td>${escapar(m.nome)}</td>
                                <td>${escapar(m.id.toUpperCase())}</td>
                                <td>${LEITOS.filter((l) => (estado.classificacoes[l] || []).includes(m.id)).length}</td>
                            </tr>`).join("")}
                    </tbody>
                </table>
            </div>
        </div>`));

    pilha.appendChild(el(`
        <div class="cartao">
            <h3 class="cartao__titulo">Acessos</h3>
            <div class="tabela__area">
                <table class="tabela">
                    <thead><tr><th>Grupo</th><th>Setores</th><th>Permissões</th></tr></thead>
                    <tbody>
                        <tr><td>Administradores</td><td>Todos</td><td>12</td></tr>
                        <tr><td>Plantão Oeste</td><td>Oeste</td><td>5</td></tr>
                    </tbody>
                </table>
            </div>
            <p class="demo-nota">O sistema não guarda quem marcou cada tarefa — não há auditoria por marcação.</p>
        </div>`));

    raiz.appendChild(pilha);
}

/* ------------------------------------------------------------------ início */

document.getElementById("indicador-sync").addEventListener("click", () => {
    estado.sincronizacao.sincronizando = true;
    renderTopo();

    setTimeout(() => {
        estado.sincronizacao.sincronizando = false;
        estado.sincronizacao.pendentes = 0;
        estado.sincronizacao.ultimaSincronizacao = "agora mesmo";
        render();
    }, 900);
});

render();
