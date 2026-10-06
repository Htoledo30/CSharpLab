$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\build\installer\Install-AppFiles.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('csharplab-installer-' + [Guid]::NewGuid().ToString('N'))
$testRoot = [IO.Path]::GetFullPath($testRoot)
if (-not $testRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Pasta de teste fora do diretório temporário.'
}
$source = Join-Path $testRoot 'package'
$target = Join-Path $testRoot 'CSharpLab'
$uninstaller = Join-Path $testRoot 'desinstalar.ps1'
function Assert-True($value, [string]$message) { if (-not $value) { throw $message } }
try {
    New-Item -ItemType Directory -Path $source, $target -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $source 'CSharpLab.exe') -Value 'nova'
    Set-Content -LiteralPath (Join-Path $target 'CSharpLab.exe') -Value 'antiga'
    Set-Content -LiteralPath $uninstaller -Value '# teste'

    # Uma falha durante a preparação preserva a instalação anterior.
    $failed = $false
    try { Install-AppFiles $source $target (Join-Path $testRoot 'inexistente.ps1') }
    catch { $failed = $true }
    Assert-True $failed 'A cópia inválida deveria falhar.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $target 'CSharpLab.exe')) -eq 'antiga') 'A versão antiga foi perdida.'

    # Uma falha depois de mover a versão antiga restaura essa versão.
    function Move-Item([string]$LiteralPath, [string]$Destination, $ErrorAction) {
        if ($Destination -eq $target -and [IO.Path]::GetFileName($LiteralPath).StartsWith('CSharpLab-stage-')) {
            throw 'Falha simulada na troca de versão.'
        }
        Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination -ErrorAction Stop
    }
    $failed = $false
    try { Install-AppFiles $source $target $uninstaller }
    catch { $failed = $true }
    finally { Remove-Item Function:\Move-Item }
    Assert-True $failed 'A troca inválida deveria falhar.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $target 'CSharpLab.exe')) -eq 'antiga') 'O rollback não restaurou a versão antiga.'

    Install-AppFiles $source $target $uninstaller
    Assert-True ((Get-Content -LiteralPath (Join-Path $target 'CSharpLab.exe')) -eq 'nova') 'A nova versão não foi instalada.'
    Assert-True (Test-Path -LiteralPath (Join-Path $target 'desinstalar.ps1')) 'Falta o desinstalador.'
    Assert-True (-not (Get-ChildItem -LiteralPath $testRoot -Filter 'CSharpLab-*-*')) 'Sobrou uma pasta de preparação/backup.'
    Write-Host 'Instalador: preparação, rollback e troca aprovados.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
