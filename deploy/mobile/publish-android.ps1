[CmdletBinding()]
param(
    [string]$SigningDirectory = (Join-Path $env:USERPROFILE '.medicmark\android-signing'),
    [string]$OutputDirectory,
    [string]$KeyAlias = 'medicmark-upload',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$projectFile = Join-Path $repositoryRoot 'src\ChecklistPlantao.Client\ChecklistPlantao.Client.csproj'
$solutionFilter = Join-Path $repositoryRoot 'ChecklistPlantao.NoMaui.slnf'
$signingRoot = [System.IO.Path]::GetFullPath($SigningDirectory)
$keyStoreFile = Join-Path $signingRoot 'medicmark-upload.keystore'
$passwordFile = Join-Path $signingRoot 'password.txt'
$certificateFile = Join-Path $signingRoot 'upload-certificate.pem'

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\android'
}

$artifactRoot = [System.IO.Path]::GetFullPath($OutputDirectory)

foreach ($requiredFile in @($keyStoreFile, $passwordFile, $certificateFile))
{
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf))
    {
        throw "Arquivo de assinatura ausente: $requiredFile. Execute setup-android-signing.ps1 uma única vez."
    }
}

if (-not $SkipTests)
{
    & dotnet test $solutionFilter -c Release --nologo

    if ($LASTEXITCODE -ne 0)
    {
        throw "Os testes falharam (código $LASTEXITCODE); a publicação foi interrompida."
    }
}

$publishArguments = @(
    'publish',
    $projectFile,
    '-f', 'net10.0-android',
    '-c', 'Release',
    '--nologo',
    '-p:AndroidKeyStore=true',
    "-p:AndroidSigningKeyStore=$keyStoreFile",
    "-p:AndroidSigningKeyAlias=$KeyAlias",
    "-p:AndroidSigningKeyPass=file:$passwordFile",
    "-p:AndroidSigningStorePass=file:$passwordFile"
)

& dotnet @publishArguments

if ($LASTEXITCODE -ne 0)
{
    throw "A publicação Android falhou (código $LASTEXITCODE)."
}

[xml]$project = Get-Content -Raw -LiteralPath $projectFile
$displayVersion = $project.SelectSingleNode('/Project/PropertyGroup/ApplicationDisplayVersion').InnerText.Trim()
$versionCode = $project.SelectSingleNode('/Project/PropertyGroup/ApplicationVersion').InnerText.Trim()
$releaseName = "$displayVersion-code$versionCode"
$releaseDirectory = Join-Path $artifactRoot $releaseName
$publishDirectory = Join-Path (Split-Path -Parent $projectFile) 'bin\Release\net10.0-android\publish'

$signedApk = Get-ChildItem -LiteralPath $publishDirectory -Filter '*-Signed.apk' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
$signedBundle = Get-ChildItem -LiteralPath $publishDirectory -Filter '*-Signed.aab' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $signedApk -or $null -eq $signedBundle)
{
    throw "APK/AAB assinados não foram encontrados em $publishDirectory"
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

$releaseApk = Join-Path $releaseDirectory "MedicMark-$releaseName.apk"
$releaseBundle = Join-Path $releaseDirectory "MedicMark-$releaseName.aab"
$releaseCertificate = Join-Path $releaseDirectory 'upload-certificate.pem'

Copy-Item -LiteralPath $signedApk.FullName -Destination $releaseApk -Force
Copy-Item -LiteralPath $signedBundle.FullName -Destination $releaseBundle -Force
Copy-Item -LiteralPath $certificateFile -Destination $releaseCertificate -Force

if ([string]::IsNullOrWhiteSpace($env:ANDROID_HOME))
{
    throw 'ANDROID_HOME não está definido; não é possível validar o APK.'
}

if ([string]::IsNullOrWhiteSpace($env:JAVA_HOME))
{
    throw 'JAVA_HOME não está definido; não é possível validar o AAB.'
}

$buildTools = Get-ChildItem -LiteralPath (Join-Path $env:ANDROID_HOME 'build-tools') -Directory |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1

if ($null -eq $buildTools)
{
    throw 'Android build-tools não encontrado.'
}

$apkSigner = Join-Path $buildTools.FullName 'apksigner.bat'
$aapt = Join-Path $buildTools.FullName 'aapt.exe'
$jarSigner = Join-Path $env:JAVA_HOME 'bin\jarsigner.exe'

$apkVerification = & $apkSigner verify --verbose --print-certs $releaseApk 2>&1

if ($LASTEXITCODE -ne 0 -or ($apkVerification -join "`n") -match 'CN=Android Debug')
{
    throw 'O APK não passou na validação da assinatura de produção.'
}

$bundleVerification = & $jarSigner -verify $releaseBundle 2>&1

if ($LASTEXITCODE -ne 0 -or ($bundleVerification -join "`n") -notmatch 'jar verified')
{
    throw 'O AAB não passou na validação de assinatura.'
}

$badging = & $aapt dump badging $releaseApk 2>&1
$badgingText = $badging -join "`n"

if (($LASTEXITCODE -ne 0) -or
    ($badgingText -notmatch "package: name='br\.com\.checklistplantao\.app'") -or
    ($badgingText -notmatch "versionCode='$([regex]::Escape($versionCode))'") -or
    ($badgingText -notmatch "versionName='$([regex]::Escape($displayVersion))'"))
{
    throw 'O APK gerado não contém o pacote e a versão esperados.'
}

$targetMatch = [regex]::Match($badgingText, "targetSdkVersion:'(?<api>\d+)'")

if (-not $targetMatch.Success -or [int]$targetMatch.Groups['api'].Value -lt 36)
{
    throw 'O APK não atende ao targetSdkVersion 36 exigido para novas publicações a partir de 31/08/2026.'
}

$hashLines = @(
    ("{0}  {1}" -f (Get-FileHash -Algorithm SHA256 -LiteralPath $releaseApk).Hash.ToLowerInvariant(), (Split-Path -Leaf $releaseApk))
    ("{0}  {1}" -f (Get-FileHash -Algorithm SHA256 -LiteralPath $releaseBundle).Hash.ToLowerInvariant(), (Split-Path -Leaf $releaseBundle))
)
$hashFile = Join-Path $releaseDirectory 'SHA256SUMS.txt'
Set-Content -LiteralPath $hashFile -Value $hashLines -Encoding ascii

Write-Host 'Release Android assinada e validada.' -ForegroundColor Green
Write-Host "APK para compartilhamento: $releaseApk"
Write-Host "AAB para Play Console:      $releaseBundle"
Write-Host "Hashes SHA-256:              $hashFile"
