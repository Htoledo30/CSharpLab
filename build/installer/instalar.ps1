# Copia o CSharp Lab para %LocalAppData%\Programs\CSharpLab e cria os atalhos.
$ErrorActionPreference = "Stop"
$source = Join-Path $PSScriptRoot "app"
$target = Join-Path $env:LOCALAPPDATA "Programs\CSharpLab"
$exe = Join-Path $target "CSharpLab.exe"

if (-not (Test-Path (Join-Path $source "CSharpLab.exe"))) {
    Write-Host "Pasta 'app' não encontrada ao lado deste script. Extraia o .zip inteiro antes de instalar." -ForegroundColor Red
    exit 1
}

if (Get-Process CSharpLab -ErrorAction SilentlyContinue) {
    Write-Host "Feche o CSharp Lab antes de instalar." -ForegroundColor Yellow
    exit 1
}

Write-Host "Instalando em $target ..."
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item -Recurse -Force (Join-Path $source "*") $target
Copy-Item -Force (Join-Path $PSScriptRoot "desinstalar.ps1") $target

$shell = New-Object -ComObject WScript.Shell
function New-Shortcut([string]$path) {
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $target
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = "CSharp Lab — editor de C#"
    $lnk.Save()
}
New-Shortcut (Join-Path ([Environment]::GetFolderPath("Desktop")) "CSharp Lab.lnk")
New-Shortcut (Join-Path ([Environment]::GetFolderPath("Programs")) "CSharp Lab.lnk")

# Aparece em Configurações > Aplicativos instalados.
$key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CSharpLab"
New-Item -Force $key | Out-Null
$version = (Get-Item $exe).VersionInfo.ProductVersion
Set-ItemProperty $key DisplayName "CSharp Lab"
Set-ItemProperty $key DisplayVersion $version
Set-ItemProperty $key Publisher "Henrique"
Set-ItemProperty $key DisplayIcon "$exe,0"
Set-ItemProperty $key InstallLocation $target
Set-ItemProperty $key UninstallString "powershell -NoProfile -ExecutionPolicy Bypass -File `"$target\desinstalar.ps1`""
Set-ItemProperty $key NoModify 1 -Type DWord
Set-ItemProperty $key NoRepair 1 -Type DWord

Write-Host "Pronto! Atalho criado na Área de Trabalho e no Menu Iniciar." -ForegroundColor Green

# O editor abre sem o SDK, mas para executar programas é preciso o SDK .NET 10.
$hasSdk = $false
try { $hasSdk = (& dotnet --list-sdks 2>$null) -match "^10\." } catch { }
if (-not $hasSdk) {
    Write-Host ""
    Write-Host "Atenção: para EXECUTAR programas instale o SDK .NET 10:" -ForegroundColor Yellow
    Write-Host "https://dotnet.microsoft.com/pt-br/download/dotnet/10.0"
}
