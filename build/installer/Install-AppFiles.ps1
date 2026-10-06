# Prepara a nova versão antes de trocar a instalação existente. Não cria atalhos nem altera o registro.
function Install-AppFiles([string]$Source, [string]$Target, [string]$Uninstaller) {
    $Source = [IO.Path]::GetFullPath($Source)
    $Target = [IO.Path]::GetFullPath($Target)
    $parent = [IO.Path]::GetDirectoryName($Target)
    if ([IO.Path]::GetFileName($Target) -ne 'CSharpLab' -or -not $parent) {
        throw 'Pasta de instalação inválida.'
    }
    if ($Source.Equals($Target, [StringComparison]::OrdinalIgnoreCase) -or
        $Source.StartsWith($Target + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Extraia o instalador fora da pasta de instalação.'
    }
    $stage = Join-Path $parent ('CSharpLab-stage-' + [Guid]::NewGuid().ToString('N'))
    $backup = Join-Path $parent ('CSharpLab-backup-' + [Guid]::NewGuid().ToString('N'))
    # Todos os caminhos de movimentação/remoção são filhos diretos do mesmo diretório.
    foreach ($path in @($Target, $stage, $backup)) {
        if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path)) -ne $parent) {
            throw 'Caminho fora da pasta de instalação.'
        }
        if ((Test-Path -LiteralPath $path) -and
            ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'A pasta de instalação não pode ser um link.'
        }
    }
    $oldMoved = $false
    $installed = $false
    try {
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $stage -Recurse -Force -ErrorAction Stop
        Copy-Item -LiteralPath $Uninstaller -Destination $stage -Force -ErrorAction Stop
        if (-not (Test-Path -LiteralPath (Join-Path $stage 'CSharpLab.exe') -PathType Leaf)) {
            throw 'O pacote não contém CSharpLab.exe.'
        }
        if (Test-Path -LiteralPath $Target) {
            Move-Item -LiteralPath $Target -Destination $backup -ErrorAction Stop
            $oldMoved = $true
        }
        Move-Item -LiteralPath $stage -Destination $Target -ErrorAction Stop
        $installed = $true
    }
    catch {
        if ($oldMoved -and -not (Test-Path -LiteralPath $Target)) {
            Move-Item -LiteralPath $backup -Destination $Target -ErrorAction Stop
        }
        throw
    }
    finally {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
        if ($installed -and (Test-Path -LiteralPath $backup)) {
            Remove-Item -LiteralPath $backup -Recurse -Force
        }
    }
}
