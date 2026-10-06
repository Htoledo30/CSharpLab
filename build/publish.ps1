# Gera o pacote Windows x64 do CSharp Lab (self-contained: não precisa de runtime instalado).
#   powershell -ExecutionPolicy Bypass -File build\publish.ps1
param([string]$Version = "1.0.0", [string]$UpdateRepository = $env:GITHUB_REPOSITORY, [string]$OutputDirectory)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root "artifacts" }
$pkg = Join-Path $out "CSharpLab-win-x64"
$app = Join-Path $pkg "app"

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use uma versão estável no formato 1.2.3.' }
if ($UpdateRepository -and $UpdateRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'UpdateRepository deve ter o formato usuario/repositorio.'
}
if (-not $UpdateRepository) {
    Write-Host 'Pacote sem repositório de atualizações configurado.' -ForegroundColor Yellow
}
foreach ($artifactPath in @($pkg, (Join-Path $out 'CSharpLab-win-x64.zip'))) {
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($artifactPath)) -ne [IO.Path]::GetFullPath($out)) {
        throw 'Destino fora da pasta artifacts.'
    }
    if ((Test-Path -LiteralPath $artifactPath) -and
        ((Get-Item -LiteralPath $artifactPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'O destino de publicação não pode ser um link.'
    }
}

if (Test-Path -LiteralPath $pkg) { Remove-Item -LiteralPath $pkg -Recurse -Force }
New-Item -ItemType Directory -Force $app | Out-Null

dotnet publish (Join-Path $root "src\CSharpLab\CSharpLab.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -p:Version=$Version `
    "-p:UpdateRepository=$UpdateRepository" `
    -o $app
if ($LASTEXITCODE -ne 0) { throw "Falha no publish" }

Copy-Item (Join-Path $PSScriptRoot "installer\*") $pkg
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $app
Copy-Item (Join-Path $root "README.md") (Join-Path $pkg "LEIA-ME.md")

$zip = Join-Path $out "CSharpLab-win-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
Compress-Archive -Path $pkg -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zip + '.sha256', "$hash  CSharpLab-win-x64.zip`n", [Text.Encoding]::ASCII)
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Pacote: $zip ($size MB)"
Write-Host "SHA-256: $zip.sha256"
