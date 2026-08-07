<#
.SYNOPSIS
    Faz backup do banco SQLite do ChecklistPlantão.

.DESCRIPTION
    Usa o comando .backup do sqlite3, que é seguro com o servidor EM EXECUÇÃO — copiar o arquivo
    com o servidor rodando pode capturar um estado inconsistente por causa do WAL.

    Sem o sqlite3 disponível, cai para a cópia de arquivo e AVISA que o servidor deveria estar
    parado. O aviso é proposital: um backup silenciosamente inconsistente é pior que nenhum.

.EXAMPLE
    ./backup.ps1 -DatabasePath C:\dados\checklistplantao.db -DestinationDirectory C:\backups
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DatabasePath,

    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory,

    [int]$RetentionDays = 30
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $DatabasePath)) {
    throw "Banco não encontrado: $DatabasePath"
}

New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null

$carimbo = Get-Date -Format 'yyyyMMdd-HHmmss'
$destino = Join-Path $DestinationDirectory "checklistplantao-$carimbo.db"

$sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue

if ($sqlite) {
    & $sqlite.Source $DatabasePath ".backup '$destino'"
    Write-Host "Backup consistente gerado em $destino"
}
else {
    Write-Warning 'sqlite3 não encontrado. Usando cópia simples de arquivo.'
    Write-Warning 'PARE o servidor antes de usar este backup, ou ele pode ficar inconsistente.'
    Copy-Item $DatabasePath $destino
    Write-Host "Cópia gerada em $destino"
}

# Retenção: remove backups mais antigos que o prazo.
$limite = (Get-Date).AddDays(-$RetentionDays)
Get-ChildItem $DestinationDirectory -Filter 'checklistplantao-*.db' |
    Where-Object { $_.LastWriteTime -lt $limite } |
    ForEach-Object {
        Write-Host "Removendo backup antigo: $($_.Name)"
        Remove-Item $_.FullName -Force
    }
