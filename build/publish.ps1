# Gera o pacote Windows x64 do CSharp Lab (self-contained: não precisa de runtime instalado).
#   powershell -ExecutionPolicy Bypass -File build\publish.ps1
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts"
$pkg = Join-Path $out "CSharpLab-win-x64"
$app = Join-Path $pkg "app"

if (Test-Path $pkg) { Remove-Item -Recurse -Force $pkg }
New-Item -ItemType Directory -Force $app | Out-Null

dotnet publish (Join-Path $root "src\CSharpLab\CSharpLab.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -p:Version=$Version `
    -o $app
if ($LASTEXITCODE -ne 0) { throw "Falha no publish" }

Copy-Item (Join-Path $PSScriptRoot "installer\*") $pkg
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $app
Copy-Item (Join-Path $root "README.md") (Join-Path $pkg "LEIA-ME.md")

$zip = Join-Path $out "CSharpLab-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $pkg -DestinationPath $zip
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Pacote: $zip ($size MB)"
