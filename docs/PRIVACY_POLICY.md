# Política de Privacidade — Checklist de Plantão

**Vigente a partir de 22 de agosto de 2026.**

O Checklist de Plantão é uma ferramenta operacional destinada às equipes das instituições que
adotam o sistema. A instituição responsável pela instalação administra os usuários, os setores, a
retenção e o acesso aos dados.

## Dados tratados

- **Conta e acesso:** nome de usuário, nome de exibição, situação, grupos e setores permitidos. A
  senha é transmitida por conexão criptografada e guardada somente na forma de hash; nunca em
  texto legível.
- **Operação do plantão:** setor, data da sessão, códigos de leitos, tarefas concluídas,
  classificações e horários de sincronização.
- **Dispositivo e diagnóstico:** identificador gerado pelo app, nome do aparelho, plataforma,
  versão, último contato e estado de sincronização e notificações.
- **Segurança:** o endereço IP e dados técnicos da requisição podem ser processados nos registros
  do servidor e na limitação de tentativas de acesso.

**O sistema não cadastra dados de pacientes.** Não há nome, CPF, prontuário, diagnóstico ou
informação clínica de paciente. Também não registra qual usuário marcou cada tarefa.

## Finalidades

Os dados são usados somente para autenticar o acesso, aplicar permissões, exibir e sincronizar o
checklist, permitir o uso offline, enviar notificações locais e manter a segurança e a
disponibilidade do serviço.

## Armazenamento local e notificações

O aplicativo mantém no aparelho uma cópia operacional para funcionar sem internet. Tokens ficam
no armazenamento seguro do sistema e a senha não é armazenada. As notificações são agendadas
localmente no aparelho e não usam publicidade nem rastreamento.

## Compartilhamento

Não vendemos dados e não usamos redes de anúncios, analytics de terceiros ou rastreamento entre
aplicativos. O provedor de infraestrutura pode processar os dados apenas para hospedar e proteger
o serviço, sob as instruções do responsável pela instalação.

## Retenção

Os prazos são administrados pela instituição. Na configuração padrão, tokens de renovação expiram
em 30 dias, registros de sincronização são mantidos por 30 dias, operações idempotentes por 7 dias
e dispositivos sem contato são marcados inativos após 30 dias. Sessões encerradas têm retenção
operacional padrão de 24 horas. Cópias de segurança seguem a política da instituição.

## Segurança

O serviço usa HTTPS, controle de acesso por setor e permissão, senhas com hash, tokens rotativos,
bloqueio de tentativas e armazenamento seguro no aparelho. Nenhum sistema é infalível, mas são
adotadas medidas compatíveis com a natureza dos dados tratados.

## Direitos e exclusão

Para consultar, corrigir ou solicitar a exclusão de dados de conta ou dispositivo, procure o
administrador da instituição que forneceu seu acesso. Também é possível usar o e-mail público
exibido na seção de suporte da ficha do aplicativo na loja. Desinstalar o aplicativo ou apagar
seus dados remove a cópia local; dados do servidor devem ser tratados pelo administrador.

## Público

O aplicativo não é direcionado a crianças. Ele é uma ferramenta de trabalho para profissionais
autorizados pela instituição.

## Alterações e contato

Esta política pode ser atualizada quando o funcionamento do serviço ou requisitos legais mudarem.
A data de vigência acima identifica a versão atual. O contato público de privacidade e suporte é o
informado na ficha do aplicativo na loja e, quando configurado no servidor, aparece na versão web
desta política.

A versão pública é servida sem autenticação em `/privacidade` e `/privacy`. Configure o contato no
servidor com `Privacy__ContactEmail`.
