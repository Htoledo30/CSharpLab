param(
    [int]$ProcessId,
    [string]$Source,
    [string]$Target,
    [string]$Version,
    [string]$Log,
    [string]$UpdatesRoot,
    [switch]$Relaunch
)
$ErrorActionPreference = 'Stop'
function Write-Log([string]$text) {
    try { Add-Content -LiteralPath $Log -Value ("[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $text) -Encoding UTF8 } catch { }
}
function Full-Path([string]$path) { [IO.Path]::GetFullPath($path).TrimEnd([IO.Path]::DirectorySeparatorChar) }
function Inside-Path([string]$root, [string]$relative) {
    $path = Full-Path (Join-Path $root $relative)
    if ([IO.Path]::IsPathRooted($relative) -or -not $path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Caminho inválido no manifesto de atualização.'
    }
    return $path
}
function Safe-Files([string]$dir) {
    $item = Get-Item -LiteralPath $dir -Force
    if (-not $item.PSIsContainer) { throw 'A pasta do aplicativo é inválida.' }
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Link dentro da pasta do aplicativo.' }
    foreach ($child in Get-ChildItem -LiteralPath $dir -Force) {
        if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Link dentro da pasta do aplicativo.' }
        if ($child.PSIsContainer) { Safe-Files $child.FullName } else { $child.FullName }
    }
}
function Verify-Files([string]$dir, $files, [bool]$exact) {
    $disk = @(Safe-Files $dir)
    $properties = @($files.PSObject.Properties)
    if ($properties.Count -eq 0 -or ($exact -and $disk.Count -ne $properties.Count)) { throw 'Pacote de atualização incompleto.' }
    foreach ($file in $properties) {
        $path = Inside-Path $dir $file.Name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Value) {
            throw ('Arquivo de atualização inválido: ' + $file.Name)
        }
    }
}

$lock = $null
$stage = $null
$backup = $null
$oldMoved = $false
$oldCopied = $false
$old = $null
$installed = $false
$canRelaunch = $false
$pathsValidated = $false
$exitCode = 1
try {
    $UpdatesRoot = Full-Path $UpdatesRoot
    # Este processo não pode ficar "dentro" da pasta que vai ser trocada.
    if (Test-Path -LiteralPath $UpdatesRoot) { Set-Location -LiteralPath $UpdatesRoot }
    $Source = Full-Path $Source
    $Target = Full-Path $Target
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versão inválida.' }
    $readyRoot = Full-Path (Join-Path $UpdatesRoot $Version)
    $parent = [IO.Path]::GetDirectoryName($Target)
    if (-not $parent -or $Target -eq [IO.Path]::GetPathRoot($Target).TrimEnd('\') -or
        $Target.StartsWith($UpdatesRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not $Source.StartsWith($readyRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Destino ou origem inválidos para a atualização.'
    }
    foreach ($dir in @($UpdatesRoot, $readyRoot, $Target)) {
        if ((Test-Path -LiteralPath $dir) -and
            ((Get-Item -LiteralPath $dir -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'A pasta de atualização não pode ser um link.'
        }
    }
    if ((Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
        Wait-Process -Id $ProcessId -Timeout 120 -ErrorAction Stop
    }
    # A mesma trava do editor impede outra abertura durante a troca dos arquivos.
    $dataRoot = [IO.Path]::GetDirectoryName($UpdatesRoot)
    $lock = [IO.FileStream]::new((Join-Path $dataRoot 'instance.lock'),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $canRelaunch = $true
    $manifest = Get-Content -LiteralPath (Join-Path $readyRoot 'pronto.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.Version -ne $Version -or (Inside-Path $readyRoot $manifest.AppDirectory) -ne $Source -or
        [IO.Path]::GetFileName($Source) -ne 'app' -or -not $manifest.Files.PSObject.Properties['CSharpLab.exe']) {
        throw 'Manifesto de atualização inválido.'
    }
    Verify-Files $Source $manifest.Files $true
    Write-Log "Aplicando versão $Version em $Target"
    $suffix = [Guid]::NewGuid().ToString('N')
    $stage = Join-Path $parent ([IO.Path]::GetFileName($Target) + '.update-' + $suffix)
    $backup = Join-Path $parent ([IO.Path]::GetFileName($Target) + '.backup-' + $suffix)
    foreach ($path in @($Target, $stage, $backup)) {
        if ([IO.Path]::GetDirectoryName((Full-Path $path)) -ne $parent) { throw 'Caminho fora da pasta do aplicativo.' }
    }
    $pathsValidated = $true
    New-Item -ItemType Directory -Path $stage | Out-Null
    if (Test-Path -LiteralPath $Target) {
        $null = @(Safe-Files $Target)
        Get-ChildItem -LiteralPath $Target -Force | Copy-Item -Destination $stage -Recurse -Force
        # Só remove arquivos conhecidos de um pacote anterior. Arquivos pessoais e o desinstalador ficam.
        $oldInventory = Join-Path $stage '.csharplab-package.json'
        if (Test-Path -LiteralPath $oldInventory) {
            $old = Get-Content -LiteralPath $oldInventory -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($file in $old.PSObject.Properties) {
                if (-not $manifest.Files.PSObject.Properties[$file.Name]) {
                    $oldFile = Inside-Path $stage $file.Name
                    if (Test-Path -LiteralPath $oldFile -PathType Leaf) { Remove-Item -LiteralPath $oldFile -Force }
                }
            }
        }
    }
    Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $stage -Recurse -Force
    Verify-Files $stage $manifest.Files $false
    $manifest.Files | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage '.csharplab-package.json') -Encoding UTF8
    $inPlace = $false
    if (Test-Path -LiteralPath $Target) {
        # O antivírus ou o indexador podem segurar a pasta por um instante: tenta de novo antes de desistir.
        for ($try = 1; -not $oldMoved -and -not $inPlace; $try++) {
            try { Move-Item -LiteralPath $Target -Destination $backup; $oldMoved = $true }
            catch {
                if ($try -lt 10) { Start-Sleep -Milliseconds 500 }
                else { Write-Log ('A pasta não pôde ser trocada inteira: ' + $_.Exception.Message); $inPlace = $true }
            }
        }
    }
    if ($inPlace) {
        # Algum programa está "dentro" da pasta (uma janela do Explorer, um terminal…): troca arquivo por
        # arquivo, guardando antes uma cópia para voltar atrás se algo der errado.
        Copy-Item -LiteralPath $Target -Destination $backup -Recurse
        $oldCopied = $true
        Get-ChildItem -LiteralPath $stage -Force | Copy-Item -Destination $Target -Recurse -Force
        if ($old) {
            foreach ($file in $old.PSObject.Properties) {
                if (-not $manifest.Files.PSObject.Properties[$file.Name]) {
                    $oldFile = Inside-Path $Target $file.Name
                    if (Test-Path -LiteralPath $oldFile -PathType Leaf) { Remove-Item -LiteralPath $oldFile -Force }
                }
            }
        }
        Verify-Files $Target $manifest.Files $false
        Write-Log 'Arquivos trocados um por um.'
    }
    else {
        Move-Item -LiteralPath $stage -Destination $Target
    }
    $installed = $true
    $exitCode = 0
    Write-Log "Atualizado para $Version."
    try {
        $failureFile = Join-Path $UpdatesRoot 'falha.json'
        if (Test-Path -LiteralPath $failureFile) { Remove-Item -LiteralPath $failureFile -Force }
    } catch { Write-Log $_.Exception.Message }
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CSharpLab'
    try {
        if ((Test-Path $key) -and (Get-ItemProperty $key).InstallLocation -eq $Target) { Set-ItemProperty $key DisplayVersion $Version }
    } catch { Write-Log $_.Exception.Message }
}
catch {
    $failureMessage = $_.Exception.Message
    Write-Log ('Falhou: ' + $failureMessage)
    try {
        if ($oldMoved -and -not $installed -and -not (Test-Path -LiteralPath $Target)) {
            Move-Item -LiteralPath $backup -Destination $Target
            Write-Log 'Versão anterior restaurada.'
        }
        elseif ($oldCopied -and -not $installed) {
            Get-ChildItem -LiteralPath $backup -Force | Copy-Item -Destination $Target -Recurse -Force
            Write-Log 'Versão anterior restaurada.'
        }
        if ($UpdatesRoot -and (Test-Path -LiteralPath $UpdatesRoot)) {
            @{ Version = $Version; Message = $failureMessage } | ConvertTo-Json |
                Set-Content -LiteralPath (Join-Path $UpdatesRoot 'falha.json') -Encoding UTF8
        }
    }
    catch { Write-Log ('Falha ao restaurar: ' + $_.Exception.Message); $canRelaunch = $false }
}
finally {
    if ($pathsValidated -and $stage -and (Test-Path -LiteralPath $stage)) {
        try { Remove-Item -LiteralPath $stage -Recurse -Force } catch { Write-Log $_.Exception.Message }
    }
    if ($pathsValidated -and $installed -and $backup -and (Test-Path -LiteralPath $backup)) {
        try { Remove-Item -LiteralPath $backup -Recurse -Force } catch { Write-Log $_.Exception.Message }
    }
    if ($lock) { $lock.Dispose() }
}
if ($Relaunch -and $canRelaunch -and (Test-Path -LiteralPath (Join-Path $Target 'CSharpLab.exe'))) {
    Start-Process -FilePath (Join-Path $Target 'CSharpLab.exe') -WorkingDirectory ([Environment]::GetFolderPath('UserProfile')) -WindowStyle Hidden
}
exit $exitCode
