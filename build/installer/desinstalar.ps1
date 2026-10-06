# Remove o CSharp Lab instalado para o usuário atual. Seus projetos não são tocados.
$target = Join-Path $env:LOCALAPPDATA "Programs\CSharpLab"
if (Get-Process CSharpLab -ErrorAction SilentlyContinue) {
    Write-Host "Feche o CSharp Lab antes de desinstalar." -ForegroundColor Yellow
    exit 1
}
Remove-Item -Force (Join-Path ([Environment]::GetFolderPath("Desktop")) "CSharp Lab.lnk") -ErrorAction SilentlyContinue
Remove-Item -Force (Join-Path ([Environment]::GetFolderPath("Programs")) "CSharp Lab.lnk") -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CSharpLab" -ErrorAction SilentlyContinue
# O script está dentro da pasta; a remoção final acontece após ele terminar.
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$target`"" -WindowStyle Hidden
Write-Host "CSharp Lab removido. Preferências em %LocalAppData%\CSharpLab foram mantidas."
