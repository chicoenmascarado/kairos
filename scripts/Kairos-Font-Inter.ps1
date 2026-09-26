# ============================================================
#  Kairos-Font-Inter.ps1
#  Descarga e instala la fuente Inter (SIL OFL, libre) y la
#  aplica como fuente del sistema sustituyendo Segoe UI.
#  Totalmente reversible.
#
#  Aplicar:
#    powershell -ExecutionPolicy Bypass -File "Kairos-Font-Inter.ps1"
#  Revertir:
#    powershell -ExecutionPolicy Bypass -File "Kairos-Font-Inter.ps1" -Revert
#
#  NOTA: requiere permisos de administrador (instala fuentes y
#  escribe en HKLM). Hay que cerrar sesion o reiniciar para ver
#  el cambio aplicado en toda la interfaz.
# ============================================================

param(
    [switch]$Revert
)

$ErrorActionPreference = "Stop"

function Write-Step($msg) { Write-Host "[Inter] $msg" -ForegroundColor Magenta }
function Write-Ok($msg)   { Write-Host "[OK] $msg"     -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "[!] $msg"      -ForegroundColor Yellow }
function Write-Err($msg)  { Write-Host "[X] $msg"      -ForegroundColor Red }

# Claves de registro implicadas
$FontsKey       = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"
$SubstitutesKey = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontSubstitutes"

# Nombres de las fuentes Segoe que se redirigen a Inter
$SegoeNames = @(
    "Segoe UI",
    "Segoe UI (TrueType)",
    "Segoe UI Bold (TrueType)",
    "Segoe UI Italic (TrueType)",
    "Segoe UI Bold Italic (TrueType)",
    "Segoe UI Semibold (TrueType)",
    "Segoe UI Light (TrueType)"
)

# ------------------------------------------------------------
# Comprobacion de administrador
# ------------------------------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Err "Este script necesita permisos de administrador."
    Write-Warn "Abre PowerShell como administrador y vuelve a ejecutarlo."
    exit 1
}

# ============================================================
#  MODO REVERTIR
# ============================================================
if ($Revert) {
    Write-Host ""
    Write-Host "=== Revirtiendo fuente Inter (volver a Segoe UI) ===" -ForegroundColor Cyan
    Write-Host ""

    Write-Step "Quitando sustituciones de fuente..."
    foreach ($name in @("Segoe UI")) {
        $existing = Get-ItemProperty -Path $SubstitutesKey -Name $name -ErrorAction SilentlyContinue
        if ($existing) {
            Remove-ItemProperty -Path $SubstitutesKey -Name $name -ErrorAction SilentlyContinue
            Write-Ok "Sustitucion '$name' eliminada."
        }
    }

    Write-Step "Restaurando entradas Segoe UI en el registro de fuentes..."
    # Restaura los nombres Segoe UI por defecto apuntando a sus ttf originales.
    $defaults = @{
        "Segoe UI (TrueType)"             = "segoeui.ttf"
        "Segoe UI Bold (TrueType)"        = "segoeuib.ttf"
        "Segoe UI Italic (TrueType)"      = "segoeuii.ttf"
        "Segoe UI Bold Italic (TrueType)" = "segoeuiz.ttf"
        "Segoe UI Semibold (TrueType)"    = "seguisb.ttf"
        "Segoe UI Light (TrueType)"       = "segoeuil.ttf"
    }
    foreach ($k in $defaults.Keys) {
        Set-ItemProperty -Path $FontsKey -Name $k -Value $defaults[$k] -ErrorAction SilentlyContinue
    }
    Write-Ok "Entradas Segoe UI restauradas."

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host " Revertido. Cierra sesion o reinicia" -ForegroundColor Cyan
    Write-Host " para volver a ver Segoe UI." -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""
    exit 0
}

# ============================================================
#  MODO APLICAR
# ============================================================
Write-Host ""
Write-Host "=== Instalando y aplicando la fuente Inter (Kairos) ===" -ForegroundColor Cyan
Write-Host ""

# ------------------------------------------------------------
# 1. Descargar Inter desde el release oficial de GitHub
# ------------------------------------------------------------
$Version  = "4.1"
$ZipUrl   = "https://github.com/rsms/inter/releases/download/v$Version/Inter-$Version.zip"
$TempDir  = Join-Path $env:TEMP "KairosInter"
$ZipPath  = Join-Path $TempDir "Inter.zip"
$Extract  = Join-Path $TempDir "extracted"

Write-Step "Preparando carpeta temporal..."
if (Test-Path $TempDir) { Remove-Item $TempDir -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $TempDir -Force | Out-Null

Write-Step "Descargando Inter v$Version desde GitHub..."
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $ZipUrl -OutFile $ZipPath -UseBasicParsing
} catch {
    Write-Err "No se pudo descargar Inter. Revisa la conexion o el firewall."
    Write-Warn "URL: $ZipUrl"
    exit 1
}
Write-Ok "Descargado."

# ------------------------------------------------------------
# 2. Extraer y localizar los .ttf estaticos
# ------------------------------------------------------------
Write-Step "Extrayendo..."
Expand-Archive -Path $ZipPath -DestinationPath $Extract -Force

# Los TTF estaticos suelen estar en "Inter Desktop" o "extras/ttf".
# Buscamos recursivamente los pesos que nos interesan.
$wanted = @("Inter-Regular.ttf","Inter-Bold.ttf","Inter-Italic.ttf",
            "Inter-BoldItalic.ttf","Inter-Medium.ttf","Inter-Light.ttf",
            "Inter-SemiBold.ttf")

$ttfFiles = Get-ChildItem -Path $Extract -Recurse -Filter "*.ttf" -ErrorAction SilentlyContinue |
            Where-Object { $wanted -contains $_.Name }

if (-not $ttfFiles -or $ttfFiles.Count -eq 0) {
    # Respaldo: coger cualquier Inter-*.ttf que no sea variable
    $ttfFiles = Get-ChildItem -Path $Extract -Recurse -Filter "Inter-*.ttf" -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -notmatch "Variable|opsz" }
}

if (-not $ttfFiles -or $ttfFiles.Count -eq 0) {
    Write-Err "No se encontraron los .ttf de Inter dentro del paquete."
    exit 1
}
Write-Ok "Encontrados $($ttfFiles.Count) archivos de fuente."

# ------------------------------------------------------------
# 3. Instalar las fuentes (copiar a Fonts + registrar)
# ------------------------------------------------------------
Write-Step "Instalando fuentes en el sistema..."
$FontsDir = Join-Path $env:WINDIR "Fonts"

foreach ($f in $ttfFiles) {
    $dest = Join-Path $FontsDir $f.Name
    Copy-Item -Path $f.FullName -Destination $dest -Force -ErrorAction SilentlyContinue

    # Nombre amigable para el registro (sin extension, con sufijo TrueType)
    $regName = "$([IO.Path]::GetFileNameWithoutExtension($f.Name)) (TrueType)"
    Set-ItemProperty -Path $FontsKey -Name $regName -Value $f.Name -ErrorAction SilentlyContinue
}
Write-Ok "Fuentes instaladas."

# ------------------------------------------------------------
# 4. Sustituir Segoe UI por Inter (FontSubstitutes)
# ------------------------------------------------------------
Write-Step "Aplicando Inter como fuente del sistema..."

# La sustitucion mas efectiva: redirigir "Segoe UI" -> "Inter".
Set-ItemProperty -Path $SubstitutesKey -Name "Segoe UI" -Value "Inter" -ErrorAction SilentlyContinue

# Ademas, apuntar las entradas Segoe UI del registro de fuentes a los ttf de Inter,
# para cubrir los sitios que leen el nombre directamente.
$map = @{
    "Segoe UI (TrueType)"             = "Inter-Regular.ttf"
    "Segoe UI Bold (TrueType)"        = "Inter-Bold.ttf"
    "Segoe UI Italic (TrueType)"      = "Inter-Italic.ttf"
    "Segoe UI Bold Italic (TrueType)" = "Inter-BoldItalic.ttf"
    "Segoe UI Semibold (TrueType)"    = "Inter-SemiBold.ttf"
    "Segoe UI Light (TrueType)"       = "Inter-Light.ttf"
}
foreach ($k in $map.Keys) {
    $src = $ttfFiles | Where-Object { $_.Name -eq $map[$k] } | Select-Object -First 1
    if ($src) {
        Set-ItemProperty -Path $FontsKey -Name $k -Value $map[$k] -ErrorAction SilentlyContinue
    }
}
Write-Ok "Inter aplicada como fuente del sistema."

# ------------------------------------------------------------
# 5. Limpieza
# ------------------------------------------------------------
Remove-Item $TempDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " Inter instalada y aplicada." -ForegroundColor Cyan
Write-Host " CIERRA SESION o REINICIA para ver el" -ForegroundColor Cyan
Write-Host " cambio en toda la interfaz." -ForegroundColor Cyan
Write-Host "" -ForegroundColor Cyan
Write-Host " Para revertir:" -ForegroundColor Cyan
Write-Host "  powershell -ExecutionPolicy Bypass -File Kairos-Font-Inter.ps1 -Revert" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
