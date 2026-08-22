# Publicação e compartilhamento do Android

Este documento separa os dois artefatos oficiais:

- **APK:** arquivo que pode ser enviado diretamente para aparelhos Android;
- **AAB:** arquivo aceito pela Play Console. Ele não é instalado diretamente no celular.

O identificador definitivo do aplicativo é `br.com.checklistplantao.app`. Depois de criar a ficha
na Play Console, esse identificador não pode ser trocado naquela ficha.

## 1. Chave oficial de upload

A chave identifica todas as versões oficiais. Ela fica fora do Git, por padrão em
`%USERPROFILE%\.medicmark\android-signing`.

Na primeira publicação de uma instalação de desenvolvimento:

```powershell
.\deploy\mobile\setup-android-signing.ps1
```

O comando cria:

- `medicmark-upload.keystore` — chave privada;
- `password.txt` — senha aleatória da chave;
- `upload-certificate.pem` — certificado público que pode ser enviado à Play;
- `BACKUP-OBRIGATORIO.txt` — instruções de recuperação.

Copie **o keystore e a senha juntos** para um cofre de senhas e para um segundo backup seguro.
Não envie esses dois arquivos por mensagem e nunca os adicione ao repositório. O certificado PEM
é público.

Ao ativar o [Play App Signing](https://developer.android.com/studio/publish/app-signing), o Google
guarda a chave que assina a distribuição e essa chave local passa a ser somente a chave de upload.

## 2. Gerar APK e AAB

Antes de publicar, aumente no projeto MAUI:

- `ApplicationDisplayVersion` para a versão visível, por exemplo `0.1.3`;
- `ApplicationVersion` para um inteiro **maior que qualquer code já enviado**, por exemplo `4`.

Depois execute:

```powershell
.\deploy\mobile\publish-android.ps1
```

O script roda a suíte, publica somente o projeto Android, usa a senha por arquivo para não expô-la
no log, recusa chave de debug, verifica assinatura/pacote/versão/API e grava tudo em
`artifacts\android\<versao>-code<codigo>`.

Para repetir apenas o empacotamento depois de a suíte já ter passado:

```powershell
.\deploy\mobile\publish-android.ps1 -SkipTests
```

O arquivo `SHA256SUMS.txt` permite confirmar que uma cópia recebida não foi alterada:

```powershell
Get-FileHash .\MedicMark-0.1.2-code3.apk -Algorithm SHA256
```

## 3. Compartilhar diretamente

Envie somente o `.apk` e, se desejar, o hash correspondente. No aparelho:

1. baixe o APK da origem conhecida;
2. autorize temporariamente **Instalar apps desconhecidos** para o aplicativo que abriu o arquivo;
3. instale e depois remova essa autorização;
4. confira que o nome exibido é **Checklist de Plantão**.

Uma instalação antiga assinada pela chave de desenvolvimento não pode ser atualizada pela chave
oficial. Nesse único caso, desinstale a antiga primeiro; isso apaga os dados locais. A partir do
primeiro APK oficial, as próximas versões atualizam normalmente porque usam a mesma chave.

O APK não deve ser publicado como anexo aberto em local desconhecido: ele contém o endereço
padrão do servidor, embora a tela não o revele no uso normal.

## 4. Criar a ficha na Play Console

1. Crie/valide a conta no [Google Play Console](https://play.google.com/console/). A conta de
   distribuição completa tem taxa única de US$ 25 e exige verificação de identidade.
2. Crie o app **Checklist de Plantão**, idioma `Português (Brasil)`, tipo **App**, gratuito.
3. Use o pacote `br.com.checklistplantao.app` e ative o **Play App Signing**.
4. Comece em **Teste interno** e envie o `.aab` gerado pelo script.
5. Adicione testadores e instale pelo link da própria Play para validar atualização, login,
   sincronização e notificação.

Contas pessoais criadas depois de 13/11/2023 normalmente precisam manter pelo menos 12 testadores
no teste fechado por 14 dias contínuos antes de solicitar acesso à produção. O teste interno pode
ser usado antes disso. Consulte o
[requisito atual de teste](https://support.google.com/googleplay/android-developer/answer/14151465).

## 5. Ficha sugerida

**Nome:** Checklist de Plantão

**Descrição curta:** Checklist por leito, sincronização offline e alertas para o plantão.

**Descrição completa:**

> Organize as rotinas do plantão por setor, leito, tipo e horário. O Checklist de Plantão mantém
> os dados disponíveis mesmo sem internet, sincroniza as alterações quando a conexão volta e
> agenda alertas locais para tarefas pendentes. O acesso é fornecido pela instituição e respeita
> grupos, permissões e setores. O sistema não cadastra dados de pacientes.

Materiais necessários:

- ícone da loja em PNG;
- imagem de destaque;
- ao menos duas capturas de tela reais sem credenciais, IP ou notificações pessoais;
- categoria, e-mail de suporte e política de privacidade pública.

Não use capturas com a senha, com o usuário administrador ou com dados da barra de notificações que
identifiquem o dono do aparelho.

## 6. Conteúdo do app e revisão

Preencha na Play Console:

- **Política de privacidade:** `https://163.176.119.139/privacidade`, depois que a nova versão do
  servidor estiver publicada;
- **Anúncios:** não contém anúncios;
- **Acesso ao app:** acesso restrito. Crie um usuário de revisão com dados fictícios, permissão
  apenas nos setores de demonstração e informe usuário/senha nas instruções privadas da revisão;
- **Público-alvo:** ferramenta profissional, não direcionada a crianças;
- **Classificação de conteúdo:** responda conforme ferramenta de produtividade/saúde operacional,
  sem conteúdo clínico ou gerado por usuários;
- **Segurança de dados:** declarar os dados realmente tratados descritos em
  [PRIVACY_POLICY.md](PRIVACY_POLICY.md), conexão criptografada, ausência de venda/ads/analytics e o
  processo de exclusão feito pelo administrador da instituição.

Todas as fichas publicadas, exceto as que permanecem exclusivamente no teste interno, precisam
preencher o formulário de segurança de dados. A orientação oficial está em
[Data safety](https://support.google.com/googleplay/android-developer/answer/10787469).

Nunca coloque senha administrativa na descrição pública, nas notas da versão ou em capturas. As
credenciais de revisão são informadas apenas no campo privado **App access** e devem ser revogadas
depois da aprovação.

## 7. Permissões e conformidade

A versão oficial declara somente as permissões necessárias:

- internet e estado da rede;
- notificações e vibração;
- reagendamento após reinício;
- `SCHEDULE_EXACT_ALARM`, concedida pelo usuário, com fallback inexato.

Ela não declara `USE_EXACT_ALARM` nem solicita diretamente isenção de bateria. A primeira é
restrita pela Play a apps cuja função principal é despertador, temporizador ou calendário; a
segunda também sofre restrições de política. O usuário pode abrir as configurações pelo próprio
diagnóstico do app.

O build atual mira Android API 36. A partir de 31/08/2026, a política publicada exige API 36 para
novos apps e atualizações; confirme sempre a
[exigência vigente de target API](https://support.google.com/googleplay/android-developer/answer/11926878)
antes de enviar uma nova versão.

## 8. Portão antes de produção

- suíte completa sem falhas;
- APK e AAB validados pelo script e hashes arquivados;
- APK oficial testado em aparelho real;
- atualização instalada pela trilha interna da Play;
- política pública abrindo sem login;
- e-mail de suporte e instruções privadas de revisão preenchidos;
- backup da chave confirmado;
- login, offline, sincronização, marcação e notificação exercitados;
- versão/código e notas da versão conferidos.
