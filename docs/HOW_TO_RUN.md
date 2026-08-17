# Como executar

Guia de ponta a ponta: da máquina limpa até o aplicativo rodando no celular e no computador.

Tudo aqui foi exercitado de verdade em 09/08/2026, em Windows 11 com um POCO/Xiaomi (Android 13,
MIUI V140). Os erros da seção [Quando der errado](#quando-der-errado) são os que realmente
apareceram, com a causa e a saída de cada um.

---

## 1. O que precisa estar instalado

| Item | Versão | Para quê |
|---|---|---|
| .NET SDK | 10.0.2xx | Tudo. `global.json` fixa 10.0.201 com `rollForward: latestFeature` |
| Workload `maui-android` | 10.0.20 | Compilar o aplicativo Android |
| Workload `maui-windows` | 10.0.20 | Compilar o aplicativo Windows |
| Android SDK | API 36 + build-tools 36.0.0 | Empacotar e instalar no aparelho |
| Microsoft OpenJDK | 17 (faixa aceita: 17.0 → 21.0.99) | Exigido pelo SDK do Android |

Conferir o que já existe:

```bash
dotnet --version
```

```bash
dotnet workload list
```

### 1.1 Workloads

O workload do MAUI traz o *compilador* Android, **não** o SDK do Google nem o Java — esses são
downloads separados.

```bash
dotnet workload install maui-android maui-windows
```

### 1.2 Android SDK e JDK

O pack do Android já instalado traz o alvo `InstallAndroidDependencies`, que baixa **os dois de uma
vez** (SDK + JDK). Não é preciso instalar o Android Studio.

```bash
dotnet build src/ChecklistPlantao.Client -f net10.0-android -t:InstallAndroidDependencies -p:AndroidSdkDirectory="C:\Users\SEU_USUARIO\AppData\Local\Android\Sdk" -p:JavaSdkDirectory="C:\Users\SEU_USUARIO\AppData\Local\Programs\Microsoft\jdk" -p:AcceptAndroidSDKLicenses=True -p:AndroidDependencyInstallationTimeout=60
```

Sobre esse comando:

- `AcceptAndroidSDKLicenses=True` **aceita a licença do Android SDK do Google em seu nome**. Sem
  isso o download para pedindo confirmação interativa, que não existe em terminal não interativo.
- Os caminhos ficam dentro do perfil do usuário de propósito. `C:\Program Files` exigiria terminal
  como administrador.
- São cerca de **740 MB** (SDK 440 MB + JDK 303 MB) e leva alguns minutos.
- O log termina com "Build succeeded" **repetindo avisos `XA5300` de SDK ausente**. Isso é
  esperado: são os avisos da avaliação inicial do MSBuild, repetidos no resumo final. Confira no
  disco, não no log.

Componentes instalados: `platforms/android-36`, `build-tools/36.0.0`, `platform-tools` (com `adb`),
`cmdline-tools/latest` e a licença aceita.

**Emulador não é instalado** por esse alvo. Se não tiver aparelho físico, veja
[§5.3](#53-sem-aparelho-f%C3%ADsico).

### 1.3 Variáveis de ambiente

Sem elas, todo build precisaria repetir os caminhos:

```bash
setx ANDROID_HOME "C:\Users\SEU_USUARIO\AppData\Local\Android\Sdk"
```

```bash
setx JAVA_HOME "C:\Users\SEU_USUARIO\AppData\Local\Programs\Microsoft\jdk"
```

Confirme o nome real da pasta do JDK — algumas instalações criam `jdk-17.0.14+7`.

> **Terminais já abertos não enxergam variáveis novas.** `setx` grava no registro e vale para
> processos criados **depois**; a janela atual continua com o ambiente antigo, e um `dotnet build`
> disparado dela também. Depois de definir as variáveis, **feche o terminal e abra um novo** —
> inclusive o terminal integrado do editor, que herda o ambiente de quando o editor foi aberto.

### 1.4 Conferir

```bash
dotnet build ChecklistPlantao.sln -c Debug
```

Deve terminar com **0 erros e 0 avisos**, incluindo os dois heads MAUI.

---

## 2. Segredos do servidor

Não existe senha padrão no código (decisão D-012). O administrador só é criado se estes três
valores existirem:

```bash
dotnet user-secrets set "Jwt:SigningKey" "cole-aqui-uma-chave-aleatoria-de-no-minimo-32-caracteres" --project src/ChecklistPlantao.Server
```

```bash
dotnet user-secrets set "Bootstrap:AdminUserName" "admin" --project src/ChecklistPlantao.Server
```

```bash
dotnet user-secrets set "Bootstrap:AdminPassword" "SuaSenhaForte1" --project src/ChecklistPlantao.Server
```

Regra de senha: mínimo 8 caracteres com ao menos um dígito.

Para ver o que está configurado:

```bash
dotnet user-secrets list --project src/ChecklistPlantao.Server
```

> A `Jwt:SigningKey` de desenvolvimento **não deve ir para produção**. Quem tem essa chave forja
> tokens válidos para qualquer usuário.

Sem esses valores o servidor sobe normalmente e apenas registra um aviso — mas não haverá nenhum
usuário para entrar.

---

## 3. Servidor

```bash
dotnet run --project src/ChecklistPlantao.Server --urls "http://0.0.0.0:5136"
```

`0.0.0.0` e não `localhost`: assim o servidor aceita conexões de outros aparelhos da rede. Com
`localhost` só a própria máquina alcança.

Na subida ele aplica as migrations, semeia setor Oeste, 16 leitos, os três checklists com horários,
os marcadores e os grupos.

Conferir, em outro terminal:

```bash
curl http://localhost:5136/health/ready
```

```bash
curl http://localhost:5136/api/server-info
```

O segundo é o endpoint que o aplicativo usa para medir se o servidor está no ar. Se ele responder
200, o aplicativo consegue enxergar o servidor.

**Não existe painel web.** O servidor é só API, SignalR e health — sem `wwwroot`, sem Razor Pages.
Abrir `http://localhost:5136` no navegador não mostra tela nenhuma. A administração fica dentro do
aplicativo, em `/admin`.

---

## 4. Aplicativo no Windows

```bash
dotnet run --project src/ChecklistPlantao.Client -f net10.0-windows10.0.19041.0 -c Debug
```

Na primeira execução ele pede o endereço do servidor. **Aqui `localhost` está certo**, porque o
aplicativo e o servidor rodam na mesma máquina:

```
http://localhost:5136
```

O `dotnet run` pode retornar com código 0 enquanto a janela continua aberta — ele destaca o
processo. Janela fechada e comando encerrado são coisas diferentes.

Limitação real desta plataforma (D-010): sem empacotamento MSIX, o alerta só chega com o
aplicativo **aberto**. No Android não há essa restrição.

---

## 5. Aplicativo no Android

### 5.1 Preparar o aparelho

1. **Opções do desenvolvedor** — em *Sobre o telefone*, toque 7 vezes no número da versão.
2. **Depuração USB** — ligue nas opções do desenvolvedor.
3. **Instalar via USB** — ligue também. Em MIUI/HyperOS esta é obrigatória; sem ela a instalação é
   recusada com `INSTALL_FAILED_USER_RESTRICTED`.
4. Conecte o cabo e **aceite o diálogo "Permitir depuração USB"** na tela do aparelho.

Conferir:

```bash
adb devices -l
```

Precisa aparecer `device`. Se aparecer `unauthorized`, o diálogo do passo 4 não foi aceito.

### 5.2 Escolher como o aparelho alcança o servidor

**Opção A — pelo cabo (`adb reverse`).** Não mexe no firewall e é a mais simples para desenvolver:

```bash
adb reverse tcp:5136 tcp:5136
```

Com isso o endereço a informar no aplicativo é `http://localhost:5136` — o `localhost` do celular
sai pelo cabo até a sua máquina. É a única situação em que `localhost` funciona no aparelho.

O encaminhamento **cai quando o cabo é desconectado**; refaça o comando ao reconectar.

**Opção B — pela rede Wi-Fi.** Necessária para testar de verdade: perder rede, andar pelo setor,
app fechado. Descubra o IP da sua máquina:

```bash
ipconfig
```

Use o IP do adaptador Wi-Fi e confirme que o celular está na **mesma sub-rede**. O endereço a
informar no aplicativo fica `http://SEU_IP:5136`.

Exige liberar a porta no firewall, em terminal **como administrador**:

```bash
netsh advfirewall firewall add rule name="ChecklistPlantao 5136" dir=in action=allow protocol=TCP localport=5136 profile=private
```

`profile=private` de propósito: em rede pública essa porta não deve ficar aberta.

### 5.3 Instalar e executar

```bash
dotnet build src/ChecklistPlantao.Client -f net10.0-android -c Debug -t:Run -p:EmbedAssembliesIntoApk=true
```

**Use `EmbedAssembliesIntoApk=true` enquanto testar em aparelho.** Sem essa flag, o build Debug usa
*Fast Deployment*: os assemblies ficam **fora** do APK, numa pasta enviada por `adb`. Isso é rápido
(20 s contra 6 min), mas o aplicativo quebra se alguém limpar os dados, e é frágil justamente nos
cenários que faltam validar — reinício do aparelho, app fechado, `BootReceiver`.

Se não tiver aparelho físico, instale emulador e imagem de sistema (uns 5 GB a mais):

```bash
%ANDROID_HOME%\cmdline-tools\latest\bin\sdkmanager.bat "emulator" "system-images;android-36;google_apis;x86_64"
```

### 5.4 Permissões no aparelho

O Android 13 pede notificação em tempo de execução. Verificar o que foi concedido:

```bash
adb shell dumpsys package br.com.checklistplantao.app | findstr granted=
```

| Permissão | Observação |
|---|---|
| `USE_EXACT_ALARM` | Concedida na instalação — garante horário exato |
| `SCHEDULE_EXACT_ALARM` | Idem |
| `RECEIVE_BOOT_COMPLETED` | Reagenda após reiniciar |
| `POST_NOTIFICATIONS` | **Pedida em tempo de execução**, começa negada |

Em Xiaomi/Redmi/POCO, além disso: libere **Início automático** e trave o app em segundo plano, ou a
MIUI mata os alarmes. Ver limitação nº 4 em [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md).

---

## 6. Entrar

Usuário e senha são os que você definiu em [§2](#2-segredos-do-servidor). Consulte com
`dotnet user-secrets list`.

Só existe esse usuário. O seed cria os grupos **Administradores** (as 12 permissões, com acesso a
todos os setores, inclusive os criados depois) e **Plantão Oeste** (ver e atualizar, restrito ao
setor Oeste), mas nenhum outro usuário. Os demais são criados em `/admin/acessos`.

**O primeiro login exige servidor.** Ele grava a credencial local; a partir daí a entrada offline
vale por 7 dias configuráveis.

---

## 7. Testes

```bash
dotnet test ChecklistPlantao.NoMaui.slnf -c Debug
```

São **260 testes**. O filtro `.slnf` exclui o head MAUI, que não executa testes.

> **Pare o servidor e o aplicativo Windows antes de rodar os testes.** Os processos em execução
> travam os binários e o build falha com arquivo em uso.

---

## Quando der errado

### `XA5300: The Android SDK directory could not be found`

O Android SDK não está instalado ou `ANDROID_HOME` não está definida. Ver [§1.2](#12-android-sdk-e-jdk)
e [§1.3](#13-vari%C3%A1veis-de-ambiente).

### `XA5300: The Java SDK directory could not be found`

Mesmo código de erro, causa diferente — e é o caso mais confuso dos dois.

Quase sempre significa que **o terminal foi aberto antes de `JAVA_HOME` ser definida**. Confirme que
a variável existe:

```bash
dotnet build src/ChecklistPlantao.Client -f net10.0-android -c Debug -p:JavaSdkDirectory="C:\Users\SEU_USUARIO\AppData\Local\Programs\Microsoft\jdk"
```

Se com o caminho explícito o build passa, o JDK está instalado e o problema é só ambiente: **abra um
terminal novo** ([§1.3](#13-vari%C3%A1veis-de-ambiente)).

Por que o erro fala do Java e não do Android, se as duas variáveis foram definidas juntas? Porque o
resolvedor procura o Android SDK em caminhos convencionais e encontra `%LOCALAPPDATA%\Android\Sdk`
sozinho, sem precisar de `ANDROID_HOME`. Já o JDK, instalado em
`%LOCALAPPDATA%\Programs\Microsoft\jdk`, não está em nenhum caminho convencional — ele depende
inteiramente de `JAVA_HOME`. Por isso o Android "funciona" e o Java falha no mesmo terminal.

### `INSTALL_FAILED_USER_RESTRICTED: Install canceled by user`

Trava da MIUI/HyperOS. Ligue **Instalar via USB** nas opções do desenvolvedor ([§5.1](#51-preparar-o-aparelho)).

Contorno sem mexer na configuração: copie o APK e instale tocando nele.

```bash
adb push src/ChecklistPlantao.Client/bin/Debug/net10.0-android/br.com.checklistplantao.app-Signed.apk /sdcard/Download/ChecklistPlantao.apk
```

### O app abre e fecha sozinho

```
No assemblies found in '/data/.../files/.__override__/arm64-v8a'. Assuming this is part of Fast Deployment. Exiting...
```

Alguém limpou os dados do aplicativo, e isso apaga a pasta onde o Fast Deployment coloca os
assemblies. Reinstale com `-p:EmbedAssembliesIntoApk=true` ([§5.3](#53-instalar-e-executar)).

**Um redeploy comum pode não resolver**: se o build estiver incremental e em dia, o MSBuild não
reenvia nada e o erro se repete. A flag força os assemblies para dentro do APK.

Para zerar o aplicativo, prefira **desinstalar e reinstalar** em vez de "limpar dados".

### `Failed to bind to address http://0.0.0.0:5136: address already in use`

Já existe um servidor rodando nessa porta — possivelmente em outro terminal, minimizado. Encerre-o
antes de subir outro:

```bash
netstat -ano | findstr :5136
```

### "Este usuário ainda não entrou neste dispositivo"

O aplicativo não alcançou o servidor e caiu para a entrada offline, que exige um login online
anterior neste aparelho. Não é erro de senha.

Verifique, nesta ordem: o servidor está no ar (`curl .../api/server-info`)? O `adb reverse` continua
ativo (`adb reverse --list`)? O endereço configurado no aplicativo está correto?

### A tela de login mostra estado de conexão desatualizado

A medição acontece **ao carregar a tela**. Se o servidor subiu depois, reabra a tela ou o
aplicativo.

### O log do servidor é uma enxurrada

Em `Development`, o `appsettings.Development.json` liga `Default: Debug`, e o EF Core imprime a
árvore de expressão de cada consulta — mais de 1.400 linhas só na subida. Para um log legível:

```bash
dotnet run --project src/ChecklistPlantao.Server --urls "http://0.0.0.0:5136" --Logging:LogLevel:Default=Information --Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Warning
```

### `adb devices` mostra `unauthorized`

O aparelho não confiou nesta máquina. Desbloqueie a tela e aceite "Permitir depuração USB",
marcando "sempre permitir deste computador".

---

## Referências

| Documento | Quando consultar |
|---|---|
| [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md) | Antes de prometer qualquer coisa a um usuário |
| [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) | O que está validado e o que não está |
| [MANUAL_TEST_PLAN.md](MANUAL_TEST_PLAN.md) | Cenários que só um aparelho real valida |
| [DEPLOYMENT.md](DEPLOYMENT.md) | Docker, serviço no Windows, HTTPS, backup |
| [ANDROID_SETUP.md](ANDROID_SETUP.md) · [WINDOWS_SETUP.md](WINDOWS_SETUP.md) | Detalhes por plataforma |
