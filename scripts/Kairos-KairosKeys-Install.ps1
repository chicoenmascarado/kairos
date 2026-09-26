# ============================================================
#  Kairos-KairosKeys-Install.ps1
#  Instala KairosKeys (gestor de atajos) en la VM y lo deja
#  arrancando con Windows.
#  Lanzar con:
#    powershell -ExecutionPolicy Bypass -File "Kairos-KairosKeys-Install.ps1"
# ============================================================

$ErrorActionPreference = "Stop"

function Write-Step($msg) { Write-Host "[KairosKeys] $msg" -ForegroundColor Magenta }
function Write-Ok($msg)   { Write-Host "[OK] $msg"         -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "[!] $msg"          -ForegroundColor Yellow }

Write-Host ""
Write-Host "=== Instalador de KairosKeys (Kairos) ===" -ForegroundColor Cyan
Write-Host ""

$ExeName  = "KairosKeys.exe"
$Desktop  = [Environment]::GetFolderPath("Desktop")
$ExePath  = $null

$Candidates = @(
    (Join-Path $Desktop "KairosKeys\bin\Release\net8.0-windows\win-x64\publish\$ExeName"),
    (Join-Path $Desktop "KairosKeys\$ExeName"),
    (Join-Path $Desktop $ExeName)
)
foreach ($c in $Candidates) { if (Test-Path $c) { $ExePath = $c; break } }

if (-not $ExePath) {
    Write-Warn "Buscando en el escritorio..."
    $found = Get-ChildItem -Path $Desktop -Filter $ExeName -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $ExePath = $found.FullName }
}

if (-not $ExePath) {
    Write-Warn "No se encontro $ExeName. Compila primero en el PC y copia el .exe a la VM."
    exit 1
}
Write-Ok "Encontrado: $ExePath"

Write-Step "Cerrando instancias previas..."
Get-Process -Name "KairosKeys" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

$InstallDir = Join-Path $env:LOCALAPPDATA "Kairos\KairosKeys"
$InstallExe = Join-Path $InstallDir $ExeName
Write-Step "Instalando en: $InstallDir"
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path $ExePath -Destination $InstallExe -Force
Write-Ok "Copiado."

Write-Step "Registrando arranque automatico..."
$StartupDir = [Environment]::GetFolderPath("Startup")
$ShortcutPath = Join-Path $StartupDir "KairosKeys.lnk"
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath       = $InstallExe
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description       = "KairosKeys - atajos globales de Kairos"
$Shortcut.Save()
Write-Ok "Arrancara automaticamente al iniciar sesion."

Write-Step "Arrancando KairosKeys..."
Start-Process -FilePath $InstallExe -WorkingDirectory $InstallDir
Start-Sleep -Milliseconds 600
$proc = Get-Process -Name "KairosKeys" -ErrorAction SilentlyContinue
if ($proc) { Write-Ok "KairosKeys esta en marcha (residente)." }
else { Write-Warn "No se detecta el proceso." }

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " KairosKeys instalado. Atajos activos:" -ForegroundColor Cyan
Write-Host "  Win+Alt+E      Abrir explorador Kairos" -ForegroundColor Cyan
Write-Host "  Win+Alt+S      Captura de region" -ForegroundColor Cyan
Write-Host "  Win+Alt+D      Mostrar escritorio" -ForegroundColor Cyan
Write-Host "  Win+Alt+L      Bloquear pantalla" -ForegroundColor Cyan
Write-Host "  Win+Alt+V      Pegar como texto plano" -ForegroundColor Cyan
Write-Host "  Win+Alt+RePag/AvPag/M  Volumen +/-/silenciar" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
