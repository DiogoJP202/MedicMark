<#
.SYNOPSIS
    Restaura um backup do banco do ChecklistPlantão.

.DESCRIPTION
    O servidor PRECISA estar parado. Antes de sobrescrever, o banco atual é preservado com o
    sufixo .antes-da-restauracao — restaurar o arquivo errado não pode ser irreversível.

.EXAMPLE
    ./restore.ps1 -BackupPath C:\backups\checklistplantao-20260806-190000.db -DatabasePath C:\dados\checklistplantao.db
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,

    [Parameter(Mandatory = $true)]
    [string]$DatabasePath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $BackupPath)) {
    throw "Backup não encontrado: $BackupPath"
}

Write-Warning 'O servidor precisa estar PARADO antes de restaurar.'

if (-not $PSCmdlet.ShouldProcess($DatabasePath, "Restaurar a partir de $BackupPath")) {
    return
}

if (Test-Path $DatabasePath) {
    $carimbo = Get-Date -Format 'yyyyMMdd-HHmmss'
    $preservado = "$DatabasePath.antes-da-restauracao-$carimbo"
    Move-Item $DatabasePath $preservado
    Write-Host "Banco atual preservado em $preservado"
}

# Os arquivos -wal e -shm pertencem ao banco antigo: mantê-los corromperia o restaurado.
foreach ($sufixo in @('-wal', '-shm')) {
    $caminho = "$DatabasePath$sufixo"
    if (Test-Path $caminho) {
        Remove-Item $caminho -Force
    }
}

Copy-Item $BackupPath $DatabasePath
Write-Host "Banco restaurado em $DatabasePath"
Write-Host 'Inicie o servidor e confira GET /health/ready.'
