# Limitações conhecidas

Este documento existe para evitar promessas que o sistema não cumpre. Cada item é uma limitação
real, com o motivo e o que fazer a respeito.

---

## 1. Dois dispositivos isolados não sincronizam entre si

**Nenhum sistema consegue** sincronizar dois aparelhos que não têm caminho de comunicação. Se dois
celulares estão sem rede e sem servidor, cada um enxerga apenas as próprias marcações até que ao
menos um alcance o servidor.

**Consequência prática.** Dois profissionais marcando o mesmo leito em aparelhos isolados só verão
o resultado combinado quando ambos sincronizarem. A regra de "conclusão vence" garante que nenhuma
conclusão será perdida nesse reencontro.

**Não há solução dentro do escopo.** Sincronização direta entre aparelhos (Bluetooth, Wi-Fi Direct)
não foi implementada e traria complexidade desproporcional.

---

## 1-A. Um aparelho não é avisado na hora do que o outro fez

O servidor tem o hub de avisos pronto, mas **o aplicativo ainda não se conecta a ele**. Marcar um
leito em um aparelho não acende nada no outro.

Os dados não divergem: a sincronização acontece na abertura do aplicativo, no login, na volta ao
primeiro plano, no retorno da rede e no toque em "Sincronizar agora". O que falta é a atualização
**imediata**.

**Consequência prática.** Dois profissionais no mesmo setor podem levar alguns minutos para ver o
trabalho um do outro, dependendo de quando o aplicativo sincronizar. A regra de "conclusão vence"
continua garantindo que nada se perca no reencontro.

**Como reduzir.** Tocar em "Sincronizar agora" força a atualização na hora.

---

## 2. Desativar um usuário não alcança um aparelho offline

Quando o administrador desativa um usuário ou muda seu grupo, o servidor revoga os tokens
imediatamente. Mas um aparelho **totalmente offline** continua aceitando a entrada local até:

- o dispositivo voltar a sincronizar; **ou**
- a validade do acesso offline expirar (padrão: 7 dias, configurável).

**Como reduzir a janela.** Diminuir `acessoOffline.validadeDias` nas configurações gerais. O
custo é que o pessoal do plantão precisará reconectar com mais frequência.

**Como reagir a um aparelho perdido.** Desative o usuário no servidor e, se possível, apague os
dados do aparelho remotamente pelo gerenciamento de dispositivos da instituição.

---

## 3. O servidor não conhece o estado atual de um aparelho offline

O painel administrativo mostra o **último estado sincronizado** de cada dispositivo — permissões de
notificação, versão, última sincronização. Se o aparelho está offline há dois dias, esses dados têm
dois dias.

Isso é inerente: um aparelho sem comunicação não tem como relatar nada. A tela deixa isso explícito
em vez de exibir dados velhos como se fossem atuais.

---

## 4. As notificações dependem da configuração do aparelho

Notificação é **mecanismo adicional**, não garantia. O sistema operacional pode atrasá-la ou
suprimi-la por:

- permissão de notificações negada;
- ausência de permissão de alarme exato (Android 12+);
- economia de bateria / modo Soneca;
- restrições agressivas do fabricante (Xiaomi, Huawei, Samsung e outros mantêm listas próprias);

> **Camadas separadas.** A isenção de otimização de bateria do **Android** (`deviceidle whitelist`)
> e a configuração de bateria do **fabricante** são independentes. No Xiaomi/MIUI, marcar
> "Sem restrições" na tela do aplicativo **não** o coloca na lista do Android — e o diagnóstico do
> aplicativo continuará, corretamente, apontando a pendência. Use "Corrigir agora", que abre o
> diálogo de isenção do próprio Android. Para cobertura completa em aparelhos Xiaomi, faça **as
> duas** coisas, e ative também o "Início automático".

- Assistente de Foco / Não Perturbe.

**Por isso existe o alerta dentro do aplicativo.** A faixa de tarefas atrasadas e a tela de
pendências funcionam mesmo quando o alerta do sistema falha. E a tela "Estado do dispositivo"
mostra exatamente o que está faltando — o aplicativo **nunca** afirma que as notificações estão
ativas quando uma permissão essencial está ausente.

---

## 5. No Windows, o alerta exige o aplicativo em execução

Decisão D-010, confirmada com o cliente.

O aplicativo Windows é **desempacotado** (sem MSIX), e nessa modalidade o Windows não oferece
agendamento de notificação no sistema operacional. Os alertas são disparados por um temporizador
dentro do processo.

| Situação | Alerta chega? |
|---|---|
| Aplicativo aberto | Sim |
| Aplicativo minimizado | Sim |
| Aplicativo fechado | **Não** |
| Computador desligado ou suspenso | **Não** |

**Onde isso aparece.** `ILocalNotificationScheduler.RequiresAppRunning` é verdadeiro, a tela
"Estado do dispositivo" mostra a ressalva e a faixa de saúde permanece visível.

**Como resolver, se necessário.** Empacotar como MSIX e usar o agendamento nativo. Exige o
Windows 10 SDK, certificado de assinatura e instalação por MSIX. A interface
`ILocalNotificationScheduler` já está preparada para receber essa implementação sem alterar o resto.

No **Android não há essa limitação**: o `AlarmManager` dispara com o aplicativo fechado e o
`BootReceiver` reagenda depois do reinício.

---

## 6. Exatidão do horário no Android depende de permissão

A partir do Android 12, alarme no horário exato exige `SCHEDULE_EXACT_ALARM` ou `USE_EXACT_ALARM`.
Sem uma delas, o aplicativo usa alarme inexato: o alerta chega, mas pode **atrasar alguns minutos**,
e mais ainda com o aparelho em economia de bateria.

O aplicativo detecta isso e informa. Nunca promete exatidão que não pode entregar.

---

## 7. Não existe histórico permanente do checklist

Por decisão do cliente. Depois de encerrada, a sessão fica disponível por uma janela de recuperação
(padrão 24 h, configurável, podendo ser zero) e então **suas marcações e classificações são
apagadas**.

Cadastros — usuários, grupos, setores, leitos, tipos de checklist, colunas, marcadores e
configurações — nunca são apagados.

**Se a instituição precisar de histórico**, isso é mudança de requisito, não ajuste de configuração:
exigiria uma tabela de histórico e uma política de retenção própria.

---

## 8. Não há auditoria de quem marcou

Também por decisão do cliente. `ChecklistEntry` e `SessionBedMarker` **não têm** campo de usuário, e
um teste automatizado garante que ninguém acrescente um por engano.

Os logs técnicos registram sincronização e erros, com rotação, e **não** são um histórico de ações
dos funcionários.

---

## 9. Encerrar e reiniciar o plantão exigem servidor

São ações com efeito para todos os aparelhos. Fazê-las apenas localmente criaria divergência entre
dispositivos — um mostrando o plantão aberto e outro encerrado. Sem servidor, o botão informa que a
ação exige conexão.

**Marcar e desmarcar continuam funcionando offline**, que é o essencial do plantão.

---

## 10. Administração exige servidor

Cadastrar setor, leito, coluna ou usuário são operações do servidor. Não há edição offline de
configuração — alterar um horário sem o servidor faria aparelhos diferentes agendarem coisas
diferentes.

---

## 11. Limitações do ambiente de desenvolvimento atual

Registradas em [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md):

- o **agendamento** de notificações em aparelho real ainda não foi validado. O aparelho existe e o
  aplicativo roda nele — o que falta é observar um alerta chegando no horário, com o app fechado;
- o Windows 10 SDK não está instalado, então o empacotamento MSIX não foi exercitado;
- Docker não foi executado neste ambiente: o `Dockerfile` e o `docker-compose.yml` **não foram
  testados**;
- HTTPS com certificado confiável nos aparelhos não foi validado.

Nada disso é afirmado como testado em nenhum ponto da documentação.
