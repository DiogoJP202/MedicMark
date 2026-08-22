[CmdletBinding()]
param(
    [string]$SigningDirectory = (Join-Path $env:USERPROFILE '.medicmark\android-signing'),
    [string]$KeyAlias = 'medicmark-upload'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:JAVA_HOME))
{
    throw 'JAVA_HOME não está definido. Instale/configure o JDK antes de gerar a chave.'
}

$keytool = Join-Path $env:JAVA_HOME 'bin\keytool.exe'

if (-not (Test-Path -LiteralPath $keytool))
{
    throw "keytool não encontrado em $keytool"
}

$signingRoot = [System.IO.Path]::GetFullPath($SigningDirectory)
$keyStoreFile = Join-Path $signingRoot 'medicmark-upload.keystore'
$passwordFile = Join-Path $signingRoot 'password.txt'
$certificateFile = Join-Path $signingRoot 'upload-certificate.pem'
$instructionsFile = Join-Path $signingRoot 'BACKUP-OBRIGATORIO.txt'

foreach ($protectedFile in @($keyStoreFile, $passwordFile))
{
    if (Test-Path -LiteralPath $protectedFile)
    {
        throw "A assinatura já existe em $signingRoot. Nada foi sobrescrito."
    }
}

New-Item -ItemType Directory -Path $signingRoot -Force | Out-Null

$passwordBytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
$signingPassword = [Convert]::ToBase64String($passwordBytes)

Set-Content -LiteralPath $passwordFile -Value $signingPassword -NoNewline -Encoding utf8

& $keytool `
    -genkeypair `
    -v `
    -keystore $keyStoreFile `
    -storetype JKS `
    -storepass $signingPassword `
    -keypass $signingPassword `
    -alias $KeyAlias `
    -keyalg RSA `
    -keysize 4096 `
    -validity 10000 `
    -dname 'CN=MedicMark, O=MedicMark, C=BR'

if ($LASTEXITCODE -ne 0)
{
    throw "keytool falhou com código $LASTEXITCODE."
}

& $keytool `
    -exportcert `
    -rfc `
    -keystore $keyStoreFile `
    -storepass $signingPassword `
    -alias $KeyAlias `
    -file $certificateFile

if ($LASTEXITCODE -ne 0)
{
    throw "Não foi possível exportar o certificado público (código $LASTEXITCODE)."
}

Set-Content -LiteralPath $instructionsFile -Encoding utf8 -Value @"
BACKUP OBRIGATÓRIO DA ASSINATURA ANDROID

Guarde juntos, em um cofre de senhas/backup criptografado:
- medicmark-upload.keystore
- password.txt

Sem a chave usada numa distribuição direta por APK, futuras atualizações não instalarão por cima.
Na Play Store, esta é a upload key e pode ser redefinida pelo Play App Signing, mas o backup ainda
é obrigatório. O arquivo upload-certificate.pem é público e pode ser enviado ao Play Console.

Alias: $KeyAlias
Pacote: br.com.checklistplantao.app
"@

if ($IsWindows)
{
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $acl = Get-Acl -LiteralPath $signingRoot
    $acl.SetAccessRuleProtection($true, $false)
    $accessRule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $identity,
        [System.Security.AccessControl.FileSystemRights]::FullControl,
        [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AccessControlType]::Allow)
    $acl.SetAccessRule($accessRule)
    Set-Acl -LiteralPath $signingRoot -AclObject $acl
}

$signingPassword = $null
[Array]::Clear($passwordBytes, 0, $passwordBytes.Length)

Write-Host 'Assinatura Android criada com sucesso.' -ForegroundColor Green
Write-Host "Diretório privado: $signingRoot"
Write-Host 'A senha não foi exibida. Faça agora o backup indicado em BACKUP-OBRIGATORIO.txt.'
