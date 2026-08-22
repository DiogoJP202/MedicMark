# iPhone e iPad

O aplicativo compartilha a interface, o banco offline e a sincronização com Android e Windows.
No iOS, apenas ciclo de vida, permissão e agendamento de notificações são implementações nativas.

## Estado do porte

- alvo `net10.0-ios` incluído no projeto MAUI;
- iOS 15.0 como versão mínima do aparelho;
- mesmo identificador do aplicativo: `br.com.checklistplantao.app`;
- servidor móvel padrão: `https://163.176.119.139`;
- SQLite local em `FileSystem.AppDataDirectory`;
- credenciais protegidas por `SecureStorage`, que usa o Keychain no iOS;
- notificações locais por `UNUserNotificationCenter`, inclusive com o aplicativo fechado;
- toque no alerta encaminhado ao checklist e à coluna correspondentes;
- ícone e tela de abertura gerados pelos recursos MAUI compartilhados;
- build C# do simulador validado no Windows com .NET 10.0.302 e workload MAUI iOS 10.0.20.

O projeto não inclui permissão de push (`aps-environment`), porque os alertas do plantão são locais.
Também não libera HTTP no `Info.plist`: a conexão de produção continua exclusivamente em HTTPS.

## O que é obrigatório

Um build que rode num simulador Apple ou num iPhone real precisa das ferramentas do Xcode, que só
existem no macOS. Desenvolver no Windows é possível, mas o Visual Studio precisa estar pareado com
um Mac acessível pela rede.

No Mac:

1. instale e abra o Xcode 26, aceite a licença e conclua os componentes solicitados;
2. instale o .NET SDK 10 indicado por `global.json`;
3. instale o workload:

```bash
sudo dotnet workload install maui-ios
```

4. confirme:

```bash
dotnet --version
dotnet workload list
xcodebuild -version
```

Em agosto de 2026, a Apple exige iOS/iPadOS 26 SDK ou mais recente para novos envios ao App Store
Connect. Isso exige Xcode 26 ou uma versão posterior compatível. A versão mínima do aplicativo
continua iOS 15.0; SDK usado para compilar e versão mínima do aparelho são coisas diferentes.

## Conta Apple: testar ou compartilhar

| Objetivo | Conta necessária |
|---|---|
| Simulador no Mac | nenhuma assinatura paga |
| Testar diretamente no próprio iPhone | Apple Account gratuita, com assinatura de desenvolvimento pelo Xcode |
| Compartilhar pelo TestFlight ou publicar na App Store | Apple Developer Program, atualmente US$ 99/ano ou valor local |

A conta gratuita não gera um APK equivalente que possa ser enviado livremente. Para distribuir a
outras pessoas de forma normal, use TestFlight ou App Store e uma assinatura do programa pago.

## Rodar no simulador a partir do Mac

Liste os simuladores disponíveis:

```bash
xcrun simctl list devices available
```

Compile para um Mac Apple Silicon:

```bash
dotnet build src/ChecklistPlantao.Client/ChecklistPlantao.Client.csproj -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64
```

Para iniciar num simulador específico, copie o UDID exibido pelo primeiro comando:

```bash
dotnet build src/ChecklistPlantao.Client/ChecklistPlantao.Client.csproj -t:Run -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64 -p:_DeviceName=:v2:udid=COLE-O-UDID
```

Em Mac Intel, troque `iossimulator-arm64` por `iossimulator-x64`.

## Rodar num iPhone real

1. No Xcode, entre em **Settings → Accounts** com a Apple Account.
2. Conecte o iPhone ao Mac, confirme **Confiar neste computador** e habilite o Modo de
   Desenvolvedor quando o iOS pedir.
3. Selecione o time de desenvolvimento e crie/baixe o certificado e o perfil para o App ID
   `br.com.checklistplantao.app`.
4. Compile usando os nomes exibidos no Keychain e no portal Apple:

```bash
dotnet build src/ChecklistPlantao.Client/ChecklistPlantao.Client.csproj -f net10.0-ios -c Debug -p:RuntimeIdentifier=ios-arm64 -p:CodesignKey="Apple Development: SEU NOME (TEAMID)" -p:CodesignProvision="NOME DO PERFIL"
```

O caminho mais simples no Windows é **Visual Studio 2022 → Tools → iOS → Pair to Mac**. No Mac,
ative **System Settings → General → Sharing → Remote Login**, faça o primeiro pareamento pela
interface e depois escolha o iPhone como destino. O Visual Studio mantém a conexão SSH e envia o
build ao Xcode do Mac.

Não coloque senha do Mac, certificados, `.p12` ou perfis privados no repositório.

## Gerar o IPA para TestFlight/App Store

Crie no portal Apple:

1. um App ID explícito `br.com.checklistplantao.app`;
2. certificado **Apple Distribution**;
3. perfil de provisionamento App Store para esse App ID;
4. o registro do aplicativo no App Store Connect.

No Mac, publique o **projeto MAUI**, não a solução inteira:

```bash
dotnet publish src/ChecklistPlantao.Client/ChecklistPlantao.Client.csproj -f net10.0-ios -c Release -p:ArchiveOnBuild=true -p:RuntimeIdentifier=ios-arm64 -p:CodesignKey="Apple Distribution: SEU NOME (TEAMID)" -p:CodesignProvision="NOME DO PERFIL APPSTORE"
```

O arquivo fica em:

```text
src/ChecklistPlantao.Client/bin/Release/net10.0-ios/ios-arm64/publish/
```

Envie o archive/IPA com o Organizer do Xcode ou o aplicativo Transporter. Para TestFlight externo
e para a App Store, a Apple ainda faz a análise correspondente.

## Checklist obrigatório no aparelho

Antes de compartilhar:

1. abrir o app e confirmar que ele mostra a entrada sem revelar o IP padrão;
2. entrar online e sincronizar o setor;
3. fechar e reabrir offline, verificando o banco e o Keychain;
4. marcar e desmarcar checklists nos setores Oeste e Teste#1;
5. tocar em **Testar notificação** e conceder a permissão do iOS;
6. agendar uma coluna para os minutos seguintes, fechar o app e aguardar o alerta;
7. tocar no alerta e confirmar que a coluna correta abre;
8. testar tela bloqueada, modo Foco e permissão negada/corrigida em Ajustes;
9. validar tema claro/escuro, rotação, teclado e área segura em iPhone pequeno e grande;
10. instalar a compilação Release/TestFlight, não apenas Debug.

## O que ainda não foi validado

O build do código iOS passou no Windows, mas este computador não substitui um Mac. Permanecem
pendentes: assinatura Apple, geração do IPA, instalação num iPhone, comportamento em tela
bloqueada, entrega com o processo encerrado e revisão visual final em tamanhos reais.

Referências oficiais: [Pair to Mac](https://learn.microsoft.com/dotnet/maui/ios/pair-to-mac?view=net-maui-10.0),
[publicação do IPA](https://learn.microsoft.com/dotnet/maui/ios/deployment/publish-cli?view=net-maui-10.0),
[permissão de notificações](https://developer.apple.com/documentation/usernotifications/asking-permission-to-use-notifications),
[envio à App Store](https://developer.apple.com/app-store/submitting/) e
[Apple Developer Program](https://developer.apple.com/programs/).
