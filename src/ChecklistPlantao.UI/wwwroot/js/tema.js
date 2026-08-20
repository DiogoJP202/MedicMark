/*
    Escolha de tema — claro, escuro ou "seguir o aparelho".

    O CSS resolve sozinho quem não escolheu nada: basta a consulta prefers-color-scheme. Este
    arquivo existe só para a escolha EXPLÍCITA, que precisa marcar o <html> — fora do alcance do
    Blazor, que só governa o que está dentro da raiz do aplicativo.

    Roda enquanto o <head> é lido, antes do primeiro pixel. Esperar o aplicativo iniciar para
    aplicar o tema faria a tela piscar branca no meio da madrugada, que é exatamente o que este
    tema existe para evitar.
*/
(function () {
    'use strict';

    var CHAVE = 'checklistplantao.tema';

    function aplicar(escolha) {
        var raiz = document.documentElement;
        if (escolha === 'claro' || escolha === 'escuro') {
            raiz.setAttribute('data-tema', escolha);
        } else {
            /* Sem atributo, o @media volta a mandar — é assim que "automático" funciona. */
            raiz.removeAttribute('data-tema');
        }
    }

    function guardado() {
        /*
            localStorage pode lançar (modo privado, armazenamento desabilitado). Uma preferência
            de aparência nunca pode ser motivo de a tela não abrir.
        */
        try {
            return window.localStorage.getItem(CHAVE);
        } catch (erro) {
            return null;
        }
    }

    window.temaDoAplicativo = {
        /* Chamado pelo Blazor quando a pessoa escolhe em "Estado do dispositivo". */
        definir: function (escolha) {
            aplicar(escolha);
            try {
                window.localStorage.setItem(CHAVE, escolha);
            } catch (erro) {
                /* Aplicou nesta sessão; só não sobreviverá ao fechamento. Melhor que falhar. */
            }
        },

        /* Chamado ao abrir a tela, para marcar a opção que está valendo. */
        lido: function () {
            return guardado() || 'automatico';
        },

        /*
            O tema que está VALENDO agora — nunca "automatico".

            O interruptor do menu precisa disto, e não da preferência: com "seguir o aparelho"
            escolhido, quem sabe se está claro ou escuro é o aparelho, e um interruptor mostrando
            a posição errada é pior que não ter interruptor.
        */
        efetivo: function () {
            var atributo = document.documentElement.getAttribute('data-tema');

            if (atributo === 'claro' || atributo === 'escuro') {
                return atributo;
            }

            try {
                return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'escuro' : 'claro';
            } catch (erro) {
                return 'claro';
            }
        }
    };

    aplicar(guardado());
})();
