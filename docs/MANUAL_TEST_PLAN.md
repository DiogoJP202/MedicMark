# Plano de testes manuais

Cenários que **só um dispositivo real valida**.

Parte já foi executada num Xiaomi com Android 13 e no cliente Windows — instalação, entrada,
checklist em uso, cadastro pela administração, botão de teste de alerta e **alerta agendado
disparando no horário**. O que foi validado até agora está registrado em
[IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md); esta lista segue como o roteiro completo,
incluindo os cenários de longa duração que ainda não foram cobertos.

Registre o resultado na coluna correspondente ao rodar.

Legenda: ✅ passou · ❌ falhou · ⏳ não executado

---

## 1. Marcação e persistência local

| # | Cenário | Resultado esperado | Android | Windows |
|---|---|---|---|---|
| 1.1 | Tocar em uma célula | Muda para X **na hora**, sem espera de rede | ⏳ | ⏳ |
| 1.2 | Tocar de novo | Volta a vazio | ⏳ | ⏳ |
| 1.3 | Marcar e tocar em "Desfazer" | Volta ao estado anterior | ⏳ | ⏳ |
| 1.4 | Aguardar o Desfazer sumir | Some sozinho após ~6 s, sem modal | ⏳ | ⏳ |
| 1.5 | Marcar 10 células seguidas | Nenhuma confirmação, nenhuma mensagem repetitiva | ⏳ | ⏳ |
| 1.6 | Fechar e reabrir o app | As marcações continuam lá | ⏳ | ⏳ |
| 1.7 | Reiniciar o aparelho e abrir | As marcações continuam lá | ⏳ | ⏳ |

## 2. Modo offline

| # | Cenário | Resultado esperado | Android | Windows |
|---|---|---|---|---|
| 2.1 | Modo avião, marcar 5 células | Funciona; faixa mostra "Offline — 5 aguardando" | ⏳ | ⏳ |
| 2.2 | Desligar só o servidor, com Wi-Fi | Faixa diz "Servidor indisponível — as alterações estão salvas neste dispositivo" | ⏳ | ⏳ |
| 2.3 | Servidor local sem internet externa | Sincroniza normalmente; faixa cita a rede local | ⏳ | ⏳ |
| 2.4 | Marcar offline, fechar o app, esperar 8 h, reabrir | A fila continua íntegra | ⏳ | ⏳ |
| 2.5 | Religar a rede | Envia sozinho; faixa passa a "Tudo sincronizado" | ⏳ | ⏳ |
| 2.6 | Marcar 50 células offline e reconectar | Todas sobem; nenhuma se perde | ⏳ | ⏳ |
| 2.7 | Desligar a rede no meio de um envio | Nova tentativa com espera crescente; nada se perde | ⏳ | ⏳ |

## 3. Dois dispositivos

| # | Cenário | Resultado esperado | Resultado |
|---|---|---|---|
| 3.1 | A e B online; A marca o leito 1148 | B mostra a marcação em segundos | ⏳ |
| 3.2 | A offline marca 1150; B online marca 1152 | Ao reconectar, ambas aparecem nos dois | ⏳ |
| 3.3 | **Conflito:** B conclui 1148; A, offline desde antes, desmarca; A reconecta | A conclusão **permanece**; a tela de A se atualiza sem mensagem técnica | ⏳ |
| 3.4 | A desmarca 1148 já sincronizado | Desmarca normalmente nos dois | ⏳ |
| 3.5 | A e B marcam a mesma célula simultaneamente | Sem duplicidade; contagem correta | ⏳ |

## 4. Sessão e retenção

| # | Cenário | Resultado esperado | Resultado |
|---|---|---|---|
| 4.1 | Entrar no setor sem plantão aberto | Abre automaticamente com os 16 leitos | ⏳ |
| 4.2 | Encerrar com pendências | Exige confirmação e mostra o resumo com C.I., Sondas e Drenos | ⏳ |
| 4.3 | Encerrar sem pendências | Não exige confirmação extra | ⏳ |
| 4.4 | Reiniciar o checklist | Marcações voltam a pendente; classificações permanecem | ⏳ |
| 4.5 | Retenção em 0 h, encerrar e aguardar a manutenção | Marcações apagadas; cadastros intactos | ⏳ |
| 4.6 | Retenção em 24 h, encerrar, esperar 25 h | Apagado depois do prazo, não antes | ⏳ |

## 5. Notificações — Android

| # | Cenário | Resultado esperado | Resultado |
|---|---|---|---|
| 5.1 | Primeira execução | Pede permissão de notificações | ⏳ |
| 5.2 | Negar a permissão | Faixa vermelha fixa; **não** diz que as notificações estão ativas | ⏳ |
| 5.3 | "Testar notificação" | Alerta aparece em segundos | ⏳ |
| 5.4 | Coluna com pendências no horário, **app fechado** | Alerta chega | ⏳ |
| 5.5 | Coluna concluída antes do horário | **Nenhum** alerta | ⏳ |
| 5.6 | Concluir a coluna após o primeiro alerta | As repetições param | ⏳ |
| 5.7 | Tocar na notificação | Abre o checklist **na coluna certa** | ⏳ |
| 5.8 | Tela bloqueada no horário | Alerta aparece na tela de bloqueio | ⏳ |
| 5.9 | Reiniciar o aparelho e aguardar o próximo horário | Alerta chega (BootReceiver) | ⏳ |
| 5.10 | Sem permissão de alarme exato | Alerta chega, possivelmente atrasado; a tela **avisa** | ⏳ |
| 5.11 | Economia de bateria ativa | Diagnóstico aponta; alerta pode atrasar | ⏳ |
| 5.12 | Encerrar o plantão | Alertas pendentes são cancelados | ⏳ |
| 5.13 | Admin muda 22H para 21H e o aparelho sincroniza | Reagenda para 21H; o antigo não dispara | ⏳ |
| 5.14 | Mudar o fuso do aparelho | Reagenda; horários continuam corretos | ⏳ |
| 5.15 | Aparelho Xiaomi/Huawei/Samsung com restrição do fabricante | Documentar o comportamento observado | ⏳ |

## 6. Notificações — Windows

| # | Cenário | Resultado esperado | Resultado |
|---|---|---|---|
| 6.1 | "Testar notificação" | Toast nativo aparece | ⏳ |
| 6.2 | App aberto no horário | Alerta chega | ⏳ |
| 6.3 | App minimizado no horário | Alerta chega | ⏳ |
| 6.4 | **App fechado** no horário | **Não chega** — limitação conhecida, deve estar declarada na tela | ⏳ |
| 6.5 | Assistente de Foco ativo | Comportamento do sistema; diagnóstico coerente | ⏳ |
| 6.6 | Clicar no toast | Abre o checklist correto | ⏳ |

## 7. Permissões e acesso

| # | Cenário | Resultado esperado | Resultado |
|---|---|---|---|
| 7.1 | Usuário só com `checklist.view` | Vê a grade; não consegue marcar | ⏳ |
| 7.2 | Usuário sem `admin.*` | Não vê o menu Administração | ⏳ |
| 7.3 | O mesmo usuário chama `/api/admin/users` direto | **403** | ⏳ |
| 7.4 | Usuário em dois grupos | Enxerga a **soma** dos setores e permissões | ⏳ |
| 7.5 | Remover um grupo do usuário | Ele é desconectado; o acesso muda no próximo login | ⏳ |
| 7.6 | Desativar o usuário com o aparelho online | Perde o acesso na hora | ⏳ |
| 7.7 | Desativar com o aparelho **offline** | Continua até sincronizar ou a validade expirar (limitação documentada) | ⏳ |

## 8. Acesso offline

| # | Cenário | Resultado esperado | Resultado |
|---|---|---|---|
| 8.1 | Primeiro login em aparelho novo **sem servidor** | Recusa e explica que o primeiro acesso exige conexão | ⏳ |
| 8.2 | Login online e depois offline | Entra normalmente | ⏳ |
| 8.3 | Senha errada offline, 5 vezes | Bloqueia temporariamente | ⏳ |
| 8.4 | Validade em 1 dia; esperar 2 dias offline | Recusa e pede conexão | ⏳ |
| 8.5 | Trocar a senha no servidor e entrar offline com a antiga | Recusa após a próxima sincronização online | ⏳ |

## 9. Interface

| # | Cenário | Resultado esperado | Resultado |
|---|---|---|---|
| 9.1 | Celular em pé | Lista vertical; **sem rolagem horizontal** | ⏳ |
| 9.2 | Windows maximizado | Matriz com leito fixo à esquerda e cabeçalho fixo | ⏳ |
| 9.3 | Navegação só por teclado no Windows | Tab percorre; Espaço/Enter marca; foco visível | ⏳ |
| 9.4 | Filtro "Somente pendentes" | Some o que está feito | ⏳ |
| 9.5 | Pesquisar "1152" | Só o leito 1152 | ⏳ |
| 9.6 | Uso com uma mão no celular | Alvos confortáveis, texto legível | ⏳ |
| 9.7 | Em escala de cinza | Estados continuam distinguíveis (X, borda, texto) | ⏳ |
| 9.8 | Marcar C.I., Sondas e Drenos no mesmo leito | Os três coexistem | ⏳ |
| 9.9 | Filtrar por "Sondas" | Só os leitos com Sondas | ⏳ |
| 9.10 | Menu do rodapé, aberto e fechado | Cresce para cima; o gatilho não sai do lugar; toque fora fecha | ⏳ |
| 9.11 | Rolar qualquer tela até o fim | O último cartão não fica atrás do menu; tela curta não rola à toa | ⏳ |
| 9.12 | Desfazer com o menu visível | A faixa fica acima do menu, sem sobrepor | ⏳ |
| 9.13 | Interruptor "Tema escuro" no menu | Troca na hora e sobrevive a fechar e abrir o aplicativo | ⏳ |
| 9.14 | "Seguir o aparelho" + modo noturno do sistema | O aplicativo acompanha; o interruptor mostra o tema em vigor | ⏳ |
| 9.15 | Abrir no escuro | A tela não pisca branca antes de escurecer | ⏳ |
| 9.16 | Tocar no nome do setor | Abre a lista com as pendências de cada setor | ⏳ |
| 9.17 | Trocar de setor a partir de um checklist aberto | Mostra "Trocando de setor…" e volta ao Painel | ⏳ |
| 9.18 | Tocar no setor em que já se está | Nada acontece; o menu fecha | ⏳ |
| 9.19 | Nome de setor longo no topo | Corta com reticências, sem empurrar o botão de sincronizar | ⏳ |
| 9.20 | Tab a partir do topo, no Windows | Anel de foco branco visível no setor e no botão de sincronizar | ⏳ |
| 9.21 | Leito atrasado, em escala de cinza | O triângulo distingue de pendente sem depender da cor | ⏳ |

## 10. Administração

| # | Cenário | Resultado esperado | Resultado |
|---|---|---|---|
| 10.1 | Criar setor e leitos | Aparecem no aplicativo após sincronizar | ⏳ |
| 10.2 | Alterar o horário de "Jantar" | Aparelhos reagendam | ⏳ |
| 10.3 | Criar um marcador novo | Aparece na tela de classificações | ⏳ |
| 10.4 | Renomear "Sondas" | Nada quebra — as regras usam Id, não nome | ⏳ |
| 10.5 | Desativar leito com plantão aberto | Sai dos próximos plantões; o atual segue íntegro | ⏳ |
| 10.6 | Dois administradores editam o mesmo setor | O segundo recebe aviso de conflito e precisa atualizar | ⏳ |
| 10.7 | Mudar o fuso horário | Horários exibidos e agendados acompanham | ⏳ |

## 11. Implantação

| # | Cenário | Resultado esperado | Resultado |
|---|---|---|---|
| 11.1 | `docker compose up -d --build` | Sobe e `/health/ready` responde Healthy | ⏳ |
| 11.2 | Recriar o contêiner | O banco sobrevive (volume) | ⏳ |
| 11.3 | Backup com o servidor rodando | Arquivo consistente | ⏳ |
| 11.4 | Restaurar em servidor limpo | Dados voltam | ⏳ |
| 11.5 | Acessar de um celular pelo IP da rede | Conecta e sincroniza | ⏳ |
| 11.6 | Subir sem `Jwt__SigningKey` | Falha na subida com mensagem clara | ⏳ |
| 11.7 | Subir sem `Bootstrap__Admin*` e sem usuários | Sobe e registra aviso explicando o que fazer | ⏳ |
