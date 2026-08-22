# Android

> O projeto compila para `net10.0-android` e foi executado num Xiaomi com Android 13 em
> 18/08/2026. Login, checklist, botão de teste e alerta no horário da coluna foram validados.
> Reinício do aparelho, uso prolongado com o app fechado e restrições agressivas de bateria
> continuam pendentes; ver [MANUAL_TEST_PLAN.md](MANUAL_TEST_PLAN.md), seção 5.

## Requisitos

| Item | Versão |
|---|---|
| .NET SDK | 10.0.2xx |
| Workload | `android` (e `maui-android`, se o build reclamar) |
| Android SDK | Platform 35 ou 36, build-tools 36.x |
| JDK | 17 ou superior |
| Android mínimo | 7.0 (API 24) |

```bash
dotnet workload install maui-android
```

Se o SDK não estiver no caminho padrão:

```bash
export ANDROID_HOME=/caminho/para/android-sdk
```

## Compilar e executar

```bash
dotnet build src/ChecklistPlantao.Client -f net10.0-android -c Debug
```

Com o aparelho conectado por USB e depuração USB ativa:

```bash
dotnet build src/ChecklistPlantao.Client -f net10.0-android -t:Run -c Debug
```

APK e AAB oficiais para distribuição:

```powershell
.\deploy\mobile\publish-android.ps1
```

O script exige a chave privada criada uma única vez por
`.\deploy\mobile\setup-android-signing.ps1`, assina com a chave oficial, verifica que não é uma
assinatura de debug e grava APK, AAB, certificado e hashes em `artifacts/android/`. A chave e a
senha ficam fora do repositório em `%USERPROFILE%\.medicmark\android-signing`.

O download compartilhável é publicado em <https://diogojp202.github.io/MedicMark/>. Como a
instalação ocorre fora da Play Store, o Android pode pedir autorização para esta fonte; isso não
é motivo para desativar o Play Protect.

Use o APK para compartilhamento direto e o AAB para a Play Console. O procedimento completo,
backup obrigatório da chave e formulários da loja estão em
[PLAY_STORE_RELEASE.md](PLAY_STORE_RELEASE.md).

## Alcançar o servidor a partir do aparelho (desenvolvimento)

Quando o celular não está na mesma rede do PC — ou o firewall do Windows bloqueia a entrada — o
túnel USB resolve sem mexer em firewall nem em endereço:

```bash
adb reverse tcp:5000 tcp:5000
```

Com isso, `localhost:5000` **no aparelho** vira a porta 5000 **do PC**, e o app pode ficar
configurado com `http://localhost:5000`.

> **A porta dos dois lados precisa bater.** `dotnet run` sem `--urls` usa o que está em
> `Properties/launchSettings.json` — hoje **5136**, não 5000. O sintoma é o app não sincronizar,
> sem erro visível: o túnel existe e o servidor existe, só que em portas diferentes.

Duas saídas, qualquer uma serve:

```bash
dotnet run --project src/ChecklistPlantao.Server --urls http://0.0.0.0:5000
```

```bash
adb reverse tcp:5000 tcp:5136
```

Como conferir, em ordem — o primeiro que falhar aponta a causa:

```bash
adb reverse --list
```

```bash
curl http://localhost:5136/health
```

```bash
adb shell curl -s http://localhost:5000/health
```

O último é o que importa: `Healthy` vindo dele significa que o aparelho alcança o servidor. O túnel
**cai quando o cabo é desconectado** e precisa ser refeito na reconexão.

## Permissões declaradas

| Permissão | Para quê | Quando é pedida |
|---|---|---|
| `INTERNET`, `ACCESS_NETWORK_STATE` | Falar com o servidor | Instalação |
| `POST_NOTIFICATIONS` | Exibir alertas (Android 13+) | Em execução, no primeiro uso |
| `RECEIVE_BOOT_COMPLETED` | Reagendar após reinício | Instalação |
| `SCHEDULE_EXACT_ALARM` | Solicitar alarme exato ao usuário, com fallback inexato | Em execução, opcional |
| `VIBRATE` | Vibração no alerta | Instalação |

`USE_EXACT_ALARM` e `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` não são declaradas: as duas têm uso
restrito pela política da Play. O diagnóstico abre as telas de configuração do sistema sem pedir
uma isenção direta.

## Alarme exato

A partir do Android 12, alarme exato exige permissão. Sem ela o aplicativo usa alarme inexato: o
alerta chega, mas pode atrasar alguns minutos.

O aplicativo detecta e **informa** — a tela "Estado do dispositivo" mostra "Permissão de alarmes
exatos: não" e o botão "Corrigir agora" abre exatamente a tela do sistema onde ela é concedida.
Nunca dizemos "no horário" quando pode não estar.

## Economia de bateria

O modo Soneca e as restrições do fabricante podem adiar alarmes. Xiaomi (MIUI), Huawei (EMUI),
Samsung, Oppo e outros mantêm listas próprias de aplicativos restritos, **independentes** das
configurações padrão do Android.

Recomendação para a instituição: em "Bateria" → "Uso de bateria do aplicativo", marcar o Checklist
de Plantão como **irrestrito**. O diagnóstico do aplicativo mostra se a isenção está ativa.

## Como validar as notificações

1. Abrir o aplicativo e **conceder** a permissão de notificações.
2. Ir em "Estado do dispositivo" e conferir que não há faixa de alerta.
3. Tocar em "Testar notificação" — deve aparecer em segundos.
4. Configurar uma coluna para 2 minutos à frente, deixar pendências e **fechar o aplicativo**.
5. Aguardar: o alerta deve chegar com o aplicativo fechado.
6. Tocar na notificação: deve abrir o checklist **na coluna certa**.
7. Marcar tudo daquela coluna: as repetições devem parar.
8. Reiniciar o aparelho e repetir o passo 4 — o `BootReceiver` deve ter reagendado.

## Problemas comuns

| Sintoma | Causa provável |
|---|---|
| Nenhum alerta | Permissão de notificações negada — a faixa vermelha aponta |
| Alerta atrasado | Sem alarme exato, ou economia de bateria ativa |
| Alertas somem após reiniciar | `RECEIVE_BOOT_COMPLETED` bloqueada pelo fabricante |
| Não conecta ao servidor | A distribuição oficial exige HTTPS. Use o servidor padrão ou outro endereço HTTPS com certificado público válido — ver [DEPLOYMENT.md](DEPLOYMENT.md) |
| "Primeiro acesso exige conexão" | Correto: o primeiro login de cada aparelho precisa do servidor |
