/*
    Dados FALSOS para a apresentação.

    Os valores vêm do SeedCatalog real (setor Oeste, 16 leitos, 3 tipos de checklist com os
    horários acordados, marcadores C.I./Sondas/Drenos) para que a tela mostre exatamente o que
    o sistema mostra em uso. Nada aqui toca servidor, banco ou rede.

    Nenhum dado de paciente — nome, prontuário ou diagnóstico não existem no sistema real e
    também não existem aqui.
*/

const LEITOS = [
    "1148", "1150", "1152", "1153", "1154", "1156", "1158", "1160",
    "1161", "1162", "1163", "1164", "1165", "1166", "1167", "1169",
];

const TEMPLATES = [
    {
        id: "gelo",
        nome: "Gelo",
        colunas: [
            { id: "gelo-20", nome: "20H", hora: "20:00" },
            { id: "gelo-22", nome: "22H", hora: "22:00" },
            { id: "gelo-00", nome: "00H", hora: "00:00" },
            { id: "gelo-02", nome: "02H", hora: "02:00" },
            { id: "gelo-04", nome: "04H", hora: "04:00" },
            { id: "gelo-06", nome: "06H", hora: "06:00" },
        ],
    },
    {
        id: "glicemia",
        nome: "Glicemia",
        colunas: [
            { id: "gli-jantar", nome: "Jantar", hora: "19:30" },
            { id: "gli-cafe", nome: "Café", hora: "07:00" },
        ],
    },
    {
        id: "ssvv",
        nome: "SSVV",
        colunas: [
            { id: "ssvv-pm", nome: "PM", hora: "20:00" },
            { id: "ssvv-am", nome: "AM", hora: "06:00" },
        ],
    },
];

const MARCADORES = [
    { id: "ci", nome: "C.I." },
    { id: "sondas", nome: "Sondas" },
    { id: "drenos", nome: "Drenos" },
];

/* Quais leitos já estão marcados em cada coluna — escolhido à mão para a tela contar uma
   história: as primeiras colunas do plantão quase completas, as da madrugada ainda abertas. */
const MARCACOES_INICIAIS = {
    "gelo-20": ["1148", "1150", "1152", "1153", "1154", "1156", "1158", "1160", "1161", "1162", "1163", "1164", "1165", "1166"],
    "gelo-22": ["1148", "1150", "1152", "1153", "1154", "1156", "1158", "1160", "1161"],
    "gelo-00": [],
    "gelo-02": [],
    "gelo-04": [],
    "gelo-06": [],
    "gli-jantar": LEITOS.slice(0, 15),
    "gli-cafe": [],
    "ssvv-pm": ["1148", "1150", "1152", "1153", "1154", "1156", "1158", "1160", "1161", "1162", "1163"],
    "ssvv-am": [],
};

/* Colunas já vencidas no horário simulado (plantão em andamento, por volta das 23h). */
const COLUNAS_ATRASADAS = new Set(["gelo-20", "gelo-22", "gli-jantar", "ssvv-pm"]);

const CLASSIFICACOES_INICIAIS = {
    "1150": ["sondas"],
    "1153": ["ci"],
    "1156": ["sondas", "drenos"],
    "1160": ["drenos"],
    "1163": ["ci", "sondas"],
    "1167": ["sondas"],
};

/* Estado vivo da apresentação: começa dos valores acima e muda conforme o usuário clica. */
const estado = {
    setor: "Oeste",
    usuario: "Administrador",
    dataDoPlantao: "2026-08-17",
    janelaDoTurno: "19:00 → 07:00",
    plantaoAberto: true,
    sincronizacao: {
        estado: "online",
        pendentes: 0,
        ultimaSincronizacao: "há 2 minutos",
        sincronizando: false,
    },
    notificacoes: {
        verificado: true,
        permissaoConcedida: true,
        alarmeExato: true,
        som: true,
        vibracao: true,
        problemas: [],
    },
    marcacoes: JSON.parse(JSON.stringify(MARCACOES_INICIAIS)),
    classificacoes: JSON.parse(JSON.stringify(CLASSIFICACOES_INICIAIS)),
};

function colunasDe(templateId) {
    return TEMPLATES.find((t) => t.id === templateId).colunas;
}

function estaMarcado(colunaId, leito) {
    return estado.marcacoes[colunaId].includes(leito);
}

function alternarMarcacao(colunaId, leito) {
    const lista = estado.marcacoes[colunaId];
    const posicao = lista.indexOf(leito);

    if (posicao >= 0) {
        lista.splice(posicao, 1);
        return false;
    }

    lista.push(leito);
    return true;
}

function marcadoresDoLeito(leito) {
    return (estado.classificacoes[leito] || []).map((id) => MARCADORES.find((m) => m.id === id));
}

function progressoDaColuna(colunaId) {
    const feitos = estado.marcacoes[colunaId].length;
    return { total: LEITOS.length, feitos, pendentes: LEITOS.length - feitos };
}

function progressoDoTemplate(templateId) {
    let total = 0;
    let feitos = 0;

    for (const coluna of colunasDe(templateId)) {
        const p = progressoDaColuna(coluna.id);
        total += p.total;
        feitos += p.feitos;
    }

    return { total, feitos, pendentes: total - feitos };
}

function progressoGeral() {
    let total = 0;
    let feitos = 0;

    for (const template of TEMPLATES) {
        const p = progressoDoTemplate(template.id);
        total += p.total;
        feitos += p.feitos;
    }

    return { total, feitos, pendentes: total - feitos };
}

/** Grupos de pendência, no mesmo formato que a tela de Pendências usa no sistema real. */
function gruposDePendencia() {
    const grupos = [];

    for (const template of TEMPLATES) {
        for (const coluna of template.colunas) {
            const faltando = LEITOS.filter((l) => !estaMarcado(coluna.id, l));

            if (faltando.length === 0) {
                continue;
            }

            grupos.push({
                templateId: template.id,
                templateNome: template.nome,
                colunaId: coluna.id,
                colunaNome: coluna.nome,
                hora: coluna.hora,
                atrasado: COLUNAS_ATRASADAS.has(coluna.id),
                leitos: faltando,
            });
        }
    }

    return grupos;
}
