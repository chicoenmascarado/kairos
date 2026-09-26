# ============================================================
#  Kairos-KairosMenu-Install.ps1
#  Instala KairosMenu en la VM: copia el .exe a una ruta fija,
#  lo registra para arrancar con Windows y lo lanza.
#  Lanzar con:
#    powershell -ExecutionPolicy Bypass -File "Kairos-KairosMenu-Install.ps1"
# ============================================================

$ErrorActionPreference = "Stop"

function Write-Step($msg) { Write-Host "[KairosMenu] $msg" -ForegroundColor Magenta }
function Write-Ok($msg)   { Write-Host "[OK] $msg"         -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "[!] $msg"          -ForegroundColor Yellow }

Write-Host ""
Write-Host "=== Instalador de KairosMenu (Kairos) ===" -ForegroundColor Cyan
Write-Host ""

# ------------------------------------------------------------
# 1. Localizar el ejecutable compilado
# ------------------------------------------------------------
Write-Step "Buscando KairosMenu.exe..."

$ExeName  = "KairosMenu.exe"
$Desktop  = [Environment]::GetFolderPath("Desktop")
$ExePath  = $null

$Candidates = @(
    (Join-Path $Desktop "KairosMenu\bin\Release\net8.0-windows\win-x64\publish\$ExeName"),
    (Join-Path $Desktop "KairosMenu\$ExeName"),
    (Join-Path $Desktop $ExeName)
)

foreach ($c in $Candidates) {
    if (Test-Path $c) { $ExePath = $c; break }
}

if (-not $ExePath) {
    Write-Warn "No esta en las rutas tipicas. Buscando en el escritorio..."
    $found = Get-ChildItem -Path $Desktop -Filter $ExeName -Recurse -File -ErrorAction SilentlyContinue |
             Select-Object -First 1
    if ($found) { $ExePath = $found.FullName }
}

if (-not $ExePath) {
    Write-Host ""
    Write-Warn "No se encontro $ExeName en el escritorio."
    Write-Warn "Compila primero en el PC (publish.bat) y copia el .exe a la VM."
    Write-Host ""
    exit 1
}

Write-Ok "Encontrado: $ExePath"

# ------------------------------------------------------------
# 2. Cerrar instancia previa si esta corriendo
# ------------------------------------------------------------
Write-Step "Cerrando instancias previas..."
Get-Process -Name "KairosMenu" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
Write-Ok "Listo."

# ------------------------------------------------------------
# 3. Copiar a la ruta de instalacion fija
# ------------------------------------------------------------
$InstallDir = Join-Path $env:LOCALAPPDATA "Kairos\KairosMenu"
$InstallExe = Join-Path $InstallDir $ExeName

Write-Step "Instalando en: $InstallDir"
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path $ExePath -Destination $InstallExe -Force
Write-Ok "Copiado."

# ------------------------------------------------------------
# 4. Registrar arranque automatico con Windows
# ------------------------------------------------------------
Write-Step "Registrando arranque automatico..."
$StartupDir = [Environment]::GetFolderPath("Startup")
$ShortcutPath = Join-Path $StartupDir "KairosMenu.lnk"

$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath       = $InstallExe
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description       = "KairosMenu - menu inicio de Kairos (Ctrl+Shift+Espacio)"
$Shortcut.Save()
Write-Ok "Arrancara automaticamente al iniciar sesion."

# ------------------------------------------------------------
# 5. Lanzar ahora
# ------------------------------------------------------------
Write-Step "Arrancando KairosMenu..."
Start-Process -FilePath $InstallExe -WorkingDirectory $InstallDir
Start-Sleep -Milliseconds 600

$proc = Get-Process -Name "KairosMenu" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Ok "KairosMenu esta en marcha (residente, sin ventana)."
} else {
    Write-Warn "No se detecta el proceso. Puede haber fallado el arranque."
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " KairosMenu instalado." -ForegroundColor Cyan
Write-Host " Pulsa  Ctrl + Shift + Espacio  para abrirlo." -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
