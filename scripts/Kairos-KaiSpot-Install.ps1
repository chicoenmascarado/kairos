# ============================================================
#  Kairos-KaiSpot-Install.ps1
#  Instala KaiSpot en la VM: copia el .exe a una ruta fija,
#  lo registra para arrancar con Windows y lo lanza.
#  Lanzar con:
#    powershell -ExecutionPolicy Bypass -File "Kairos-KaiSpot-Install.ps1"
# ============================================================

$ErrorActionPreference = "Stop"

function Write-Step($msg) { Write-Host "[KaiSpot] $msg" -ForegroundColor Magenta }
function Write-Ok($msg)   { Write-Host "[OK] $msg"      -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "[!] $msg"       -ForegroundColor Yellow }

Write-Host ""
Write-Host "=== Instalador de KaiSpot (Kairos) ===" -ForegroundColor Cyan
Write-Host ""

# ------------------------------------------------------------
# 1. Localizar el ejecutable compilado
# ------------------------------------------------------------
Write-Step "Buscando KairosSpot.exe..."

$ExeName  = "KairosSpot.exe"
$Desktop  = [Environment]::GetFolderPath("Desktop")
$ExePath  = $null

# Rutas candidatas tipicas, en orden de preferencia
$Candidates = @(
    (Join-Path $Desktop "KairosSpot\bin\Release\net8.0-windows\win-x64\publish\$ExeName"),
    (Join-Path $Desktop "KairosSpot\$ExeName"),
    (Join-Path $Desktop $ExeName)
)

foreach ($c in $Candidates) {
    if (Test-Path $c) { $ExePath = $c; break }
}

# Respaldo: buscar recursivamente por todo el escritorio
if (-not $ExePath) {
    Write-Warn "No esta en las rutas tipicas. Buscando en el escritorio..."
    $found = Get-ChildItem -Path $Desktop -Filter $ExeName -Recurse -File -ErrorAction SilentlyContinue |
             Select-Object -First 1
    if ($found) { $ExePath = $found.FullName }
}

if (-not $ExePath) {
    Write-Host ""
    Write-Warn "No se encontro $ExeName en el escritorio."
    Write-Warn "Compila primero en el PC (publish.bat) y copia el .exe a la VM,"
    Write-Warn "dejandolo en el escritorio o dentro de la carpeta KairosSpot."
    Write-Host ""
    exit 1
}

Write-Ok "Encontrado: $ExePath"

# ------------------------------------------------------------
# 2. Cerrar instancia previa si esta corriendo
# ------------------------------------------------------------
Write-Step "Cerrando instancias previas de KaiSpot..."
Get-Process -Name "KairosSpot" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
Write-Ok "Listo."

# ------------------------------------------------------------
# 3. Copiar a la ruta de instalacion fija
# ------------------------------------------------------------
$InstallDir = Join-Path $env:LOCALAPPDATA "Kairos\KaiSpot"
$InstallExe = Join-Path $InstallDir $ExeName

Write-Step "Instalando en: $InstallDir"
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path $ExePath -Destination $InstallExe -Force
Write-Ok "Copiado."

# ------------------------------------------------------------
# 4. Registrar arranque automatico con Windows
#    (acceso directo en la carpeta Startup del usuario)
# ------------------------------------------------------------
Write-Step "Registrando arranque automatico..."
$StartupDir = [Environment]::GetFolderPath("Startup")
$ShortcutPath = Join-Path $StartupDir "KaiSpot.lnk"

$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath       = $InstallExe
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description       = "KaiSpot - lanzador de Kairos (Alt+Space)"
$Shortcut.Save()
Write-Ok "Arrancara automaticamente al iniciar sesion."

# ------------------------------------------------------------
# 5. Lanzar ahora
# ------------------------------------------------------------
Write-Step "Arrancando KaiSpot..."
Start-Process -FilePath $InstallExe -WorkingDirectory $InstallDir
Start-Sleep -Milliseconds 600

$proc = Get-Process -Name "KairosSpot" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Ok "KaiSpot esta en marcha (residente, sin ventana)."
} else {
    Write-Warn "No se detecta el proceso. Puede haber fallado el arranque."
    Write-Warn "Prueba a ejecutarlo a mano: $InstallExe"
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " KaiSpot instalado." -ForegroundColor Cyan
Write-Host " Pulsa  Alt + Space  para abrirlo." -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
