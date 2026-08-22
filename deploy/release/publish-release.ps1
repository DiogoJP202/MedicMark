[CmdletBinding()]
param(
    [string]$Tag,
    [string]$OutputDirectory,
    [switch]$CreateDraftRelease,
    [switch]$SkipTests,
    [switch]$SkipDefenderScan
)

$ErrorActionPreference = 'Stop'

if (-not $IsWindows)
{
    throw 'A release oficial exige Windows para compilar o head MAUI e executar o Microsoft Defender.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$projectFile = Join-Path $repositoryRoot 'src\ChecklistPlantao.Client\ChecklistPlantao.Client.csproj'
$solutionFilter = Join-Path $repositoryRoot 'ChecklistPlantao.NoMaui.slnf'
$androidScript = Join-Path $repositoryRoot 'deploy\mobile\publish-android.ps1'
$windowsReadme = Join-Path $PSScriptRoot 'WINDOWS-README.txt'

[xml]$project = Get-Content -Raw -LiteralPath $projectFile
$displayVersion = [string]$project.Project.PropertyGroup.ApplicationDisplayVersion
$versionCode = [string]$project.Project.PropertyGroup.ApplicationVersion
$assemblyVersion = [string]$project.Project.PropertyGroup.Version

if ([string]::IsNullOrWhiteSpace($Tag))
{
    $Tag = "v$displayVersion"
}

if ($Tag -ne "v$displayVersion" -or $assemblyVersion -ne $displayVersion)
{
    throw "Versões incoerentes: tag=$Tag, app=$displayVersion, assembly=$assemblyVersion."
}

$gitStatus = & git -C $repositoryRoot status --porcelain=v1 --untracked-files=all

if ($LASTEXITCODE -ne 0)
{
    throw 'Não foi possível verificar o estado do Git.'
}

if ($gitStatus)
{
    throw 'A árvore Git precisa estar limpa antes de gerar uma release oficial.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\release\$Tag"
}

$releaseDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path -LiteralPath $releaseDirectory)
{
    throw "O diretório de release já existe e não será sobrescrito: $releaseDirectory"
}

New-Item -ItemType Directory -Path $releaseDirectory | Out-Null

if (-not $SkipTests)
{
    & dotnet test $solutionFilter -c Release --nologo

    if ($LASTEXITCODE -ne 0)
    {
        throw "Os testes falharam (código $LASTEXITCODE); a release foi interrompida."
    }
}

$androidRoot = Join-Path $releaseDirectory '.android'
& $androidScript -OutputDirectory $androidRoot -SkipTests

if ($LASTEXITCODE -ne 0)
{
    throw "A publicação Android falhou (código $LASTEXITCODE)."
}

$androidVersionDirectory = Join-Path $androidRoot "$displayVersion-code$versionCode"
$sourceApk = Get-ChildItem -LiteralPath $androidVersionDirectory -Filter '*.apk' -File | Select-Object -First 1
$sourceCertificate = Join-Path $androidVersionDirectory 'upload-certificate.pem'

if ($null -eq $sourceApk -or -not (Test-Path -LiteralPath $sourceCertificate -PathType Leaf))
{
    throw 'Os artefatos Android validados não foram encontrados.'
}

$releaseApk = Join-Path $releaseDirectory 'MedicMark-Android.apk'
$releaseCertificate = Join-Path $releaseDirectory 'MedicMark-Android-certificate.pem'
Copy-Item -LiteralPath $sourceApk.FullName -Destination $releaseApk
Copy-Item -LiteralPath $sourceCertificate -Destination $releaseCertificate

$windowsPublish = Join-Path $releaseDirectory '.windows'
$windowsArguments = @(
    'publish',
    $projectFile,
    '-f', 'net10.0-windows10.0.19041.0',
    '-c', 'Release',
    '-r', 'win-x64',
    '--nologo',
    '-p:WindowsPackageType=None',
    '-p:WindowsAppSDKSelfContained=true',
    '-p:SelfContained=true',
    '-o', $windowsPublish
)

& dotnet @windowsArguments

if ($LASTEXITCODE -ne 0)
{
    throw "A publicação Windows falhou (código $LASTEXITCODE)."
}

$windowsExecutable = Join-Path $windowsPublish 'ChecklistPlantao.Client.exe'

if (-not (Test-Path -LiteralPath $windowsExecutable -PathType Leaf))
{
    throw 'O executável Windows não foi encontrado no diretório publicado.'
}

$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($windowsExecutable).ProductVersion

if ($fileVersion -notlike "$displayVersion*")
{
    throw "O executável Windows contém versão inesperada: $fileVersion."
}

Get-ChildItem -LiteralPath $windowsPublish -Filter '*.pdb' -File -Recurse |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName }
Copy-Item -LiteralPath $windowsReadme -Destination (Join-Path $windowsPublish 'LEIA-ME.txt')

if (-not $SkipDefenderScan)
{
    if ($null -eq (Get-Command Start-MpScan -ErrorAction SilentlyContinue))
    {
        throw 'Microsoft Defender não está disponível. Use -SkipDefenderScan somente em validação não oficial.'
    }

    $scanStart = (Get-Date).AddSeconds(-5)
    Start-MpScan -ScanType CustomScan -ScanPath $windowsPublish
    $detections = Get-MpThreatDetection -ErrorAction SilentlyContinue |
        Where-Object {
            $_.InitialDetectionTime -ge $scanStart -and
            ($_.Resources -join ' ') -like "*$windowsPublish*"
        }

    if ($detections)
    {
        throw 'O Microsoft Defender detectou uma ameaça no pacote Windows.'
    }
}

$releaseWindows = Join-Path $releaseDirectory 'MedicMark-Windows-x64.zip'
Compress-Archive -Path (Join-Path $windowsPublish '*') -DestinationPath $releaseWindows -CompressionLevel Optimal

if (-not $SkipDefenderScan)
{
    Start-MpScan -ScanType CustomScan -ScanPath $releaseWindows
}

$hashTargets = @($releaseApk, $releaseWindows, $releaseCertificate)
$hashLines = foreach ($artifact in $hashTargets)
{
    "{0}  {1}" -f
        (Get-FileHash -Algorithm SHA256 -LiteralPath $artifact).Hash.ToLowerInvariant(),
        (Split-Path -Leaf $artifact)
}

$hashFile = Join-Path $releaseDirectory 'SHA256SUMS.txt'
Set-Content -LiteralPath $hashFile -Value $hashLines -Encoding ascii

Remove-Item -LiteralPath $androidRoot -Recurse
Remove-Item -LiteralPath $windowsPublish -Recurse

if ($CreateDraftRelease)
{
    if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue))
    {
        throw 'GitHub CLI não está disponível.'
    }

    $branch = (& git -C $repositoryRoot branch --show-current).Trim()

    if ($branch -ne 'main')
    {
        throw 'A criação do rascunho público só pode partir da branch main.'
    }

    & git -C $repositoryRoot fetch origin main --quiet
    $head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    $originMain = (& git -C $repositoryRoot rev-parse origin/main).Trim()

    if ($head -ne $originMain)
    {
        throw 'A main local precisa corresponder exatamente a origin/main.'
    }

    & gh release view $Tag --repo DiogoJP202/MedicMark *> $null

    if ($LASTEXITCODE -eq 0)
    {
        throw "Já existe uma release ou rascunho para $Tag."
    }

    $notesFile = Join-Path $PSScriptRoot "RELEASE_NOTES_$Tag.md"
    $assets = @($releaseApk, $releaseWindows, $releaseCertificate, $hashFile)

    & gh release create $Tag @assets --repo DiogoJP202/MedicMark --target $head --draft --title "MedicMark $displayVersion" --notes-file $notesFile

    if ($LASTEXITCODE -ne 0)
    {
        throw "Não foi possível criar o rascunho da release $Tag."
    }
}

Write-Host 'Release oficial gerada e validada.' -ForegroundColor Green
Write-Host "Diretório: $releaseDirectory"
Write-Host "Android:   $releaseApk"
Write-Host "Windows:   $releaseWindows"
Write-Host "Hashes:    $hashFile"

if ($CreateDraftRelease)
{
    Write-Host "Rascunho:  https://github.com/DiogoJP202/MedicMark/releases"
}
