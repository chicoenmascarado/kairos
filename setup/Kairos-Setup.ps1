<#
.SYNOPSIS
    Kairos Setup — deja un Windows 11 recién instalado con la ISO de Kairos listo
    de una vez: drivers, español, runtimes para plugins de audio, ajustes de
    estudio (baja latencia), herramientas ASUS y la capa Kairos completa.

.DESCRIPTION
    Pensado para el ASUS ROG Strix G15 (G513IC) y un productor musical (FL Studio,
    plugins VST, interfaz de audio ASIO). Se lanza con INSTALAR-KAIROS.cmd, que pide
    permisos de administrador. Cada paso es independiente: si uno falla se apunta
    en el informe y se sigue con el siguiente.

    Qué NO toca, a propósito:
      - Windows Defender y Windows Update siguen activos (el equipo es para otra
        persona que instalará plugins). Solo se evitan reinicios con sesión abierta.
      - No sustituye la fuente del sistema (Segoe UI) para no romper interfaces de
        plugins; Inter se instala para quien la quiera usar.

.PARAMETER SinIdioma   No instala el paquete de idioma español.
.PARAMETER SinInternet Salta todo lo que necesita descargar.
#>
#Requires -RunAsAdministrator
param([switch]$SinIdioma, [switch]$SinInternet)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path     # ...\KairosSetup\setup
$Pkg = Split-Path -Parent $Root                              # ...\KairosSetup
$KairosDir = 'C:\Kairos'
New-Item -ItemType Directory -Force $KairosDir | Out-Null
$LogFile = Join-Path $KairosDir 'setup.log'
$Report = [ordered]@{}

# ---------------------------------------------------------------------------
#  Utilidades
# ---------------------------------------------------------------------------
function Log([string]$msg, [string]$lvl = 'INFO') {
    $line = '{0} [{1}] {2}' -f (Get-Date -Format 'HH:mm:ss'), $lvl, $msg
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
    $color = @{ INFO = 'Gray'; OK = 'Green'; AVISO = 'Yellow'; ERROR = 'Red'; PASO = 'Magenta' }[$lvl]
    Write-Host $line -ForegroundColor $color
}
function Step([string]$name) { Write-Host ''; Log "== $name ==" 'PASO' }
function Result([string]$step, [string]$status, [string]$detail = '') {
    $Report[$step] = if ($detail) { "$status — $detail" } else { $status }
}
function Set-Reg([string]$path, [string]$name, $value, [string]$type = 'DWord') {
    try {
        if (-not (Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
        Set-ItemProperty -Path $path -Name $name -Value $value -Type $type -ErrorAction Stop
    } catch { Log "No se pudo escribir $path\$name ($($_.Exception.Message))" 'AVISO' }
}
function Test-Online {
    try { (Invoke-WebRequest 'http://www.msftconnecttest.com/connecttest.txt' -UseBasicParsing -TimeoutSec 8).Content -match 'Microsoft' }
    catch { $false }
}

Write-Host ''
Write-Host '  K A I R O S' -ForegroundColor Magenta
Write-Host '  The perfect moment when everything flows.' -ForegroundColor DarkGray
Write-Host ''
Log "Kairos Setup en $env:COMPUTERNAME, usuario $env:USERNAME, Windows $([Environment]::OSVersion.Version)"

$build = [int](Get-CimInstance Win32_OperatingSystem).BuildNumber
if ($build -lt 22000) { Log "Esto no es Windows 11 (build $build). Algunos pasos pueden fallar." 'AVISO' }

# ---------------------------------------------------------------------------
#  1. Drivers del portátil (sin internet: así funciona el Wi-Fi desde el minuto 1)
# ---------------------------------------------------------------------------
Step 'Drivers'
$drivers = Join-Path $Pkg 'drivers'
$cs = Get-CimInstance Win32_ComputerSystem
$isVM = "$($cs.Manufacturer) $($cs.Model)" -match 'VirtualBox|innotek|VMware|QEMU|Virtual Machine|Parallels'
if ($isVM) {
    Log 'Máquina virtual: los drivers del portátil no aplican, se saltan.' 'INFO'
    Result 'Drivers' 'SALTADO' 'máquina virtual'
} elseif (Test-Path $drivers) {
    $out = pnputil /add-driver "$drivers\*.inf" /subdirs /install 2>&1
    $added = ($out | Select-String -Pattern 'Added driver packages|Paquetes de controladores agregados|Total driver packages' | Select-Object -Last 1)
    if ($added) { Log $added.Line.Trim() 'OK' } else { Log 'pnputil terminado' 'OK' }
    Result 'Drivers' 'OK' 'instalados desde la copia del portátil; Windows Update traerá versiones más nuevas'
} elseif (Get-PSDrive -PSProvider FileSystem | Where-Object { Test-Path (Join-Path $_.Root '$WinPEDriver$') }) {
    Log 'Los drivers del portátil ya se instalaron durante la instalación de Windows ($WinPEDriver$).' 'OK'
    Result 'Drivers' 'OK' 'integrados durante la instalación de Windows'
} else {
    Log 'No hay copia de drivers: se usan los de Windows y Windows Update.' 'AVISO'
    Result 'Drivers' 'AVISO' 'sin copia de drivers en el USB'
}

# ---------------------------------------------------------------------------
#  2. Internet
# ---------------------------------------------------------------------------
$online = $false
if (-not $SinInternet) {
    $online = Test-Online
    while (-not $online) {
        Write-Host ''
        Write-Host '  No hay internet. Conéctate a una red Wi-Fi o por cable y pulsa Enter.' -ForegroundColor Yellow
        $r = Read-Host '  (escribe S y Enter para seguir sin internet)'
        if ($r -match '^[sS]') { break }
        $online = Test-Online
    }
}
Log ("Internet: " + ($(if ($online) { 'sí' } else { 'no — se saltan idioma, runtimes y descargas' })))

# ---------------------------------------------------------------------------
#  3. Español: se descarga en segundo plano mientras sigue todo lo demás
#     (Windows tarda en instalarlo; al final del setup se espera y se aplica)
# ---------------------------------------------------------------------------
Step 'Idioma español'
$langJob = $null
if ($SinIdioma) {
    Result 'Idioma' 'SALTADO'
} elseif (-not $online) {
    Result 'Idioma' 'SALTADO' 'sin internet'
} else {
    # Solo idioma + interfaz: sin escritura a mano, voz ni OCR (tardan y no se usan).
    $langJob = Start-Job -ScriptBlock { Install-Language -Language es-ES -CopyToSettings -ExcludeFeatures -ErrorAction Stop | Out-Null }
    Log 'Instalando el español en segundo plano; el resto sigue mientras tanto.' 'OK'
}

# ---------------------------------------------------------------------------
#  4. Runtimes que piden FL Studio y los plugins
# ---------------------------------------------------------------------------
Step 'Runtimes para plugins (Visual C++, DirectX, .NET 3.5)'
if (-not $online) {
    Result 'Runtimes' 'SALTADO' 'sin internet'
} else {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe -ErrorAction SilentlyContinue
        $env:Path += ";$env:LOCALAPPDATA\Microsoft\WindowsApps"
        $winget = Get-Command winget -ErrorAction SilentlyContinue
    }
    $failed = @()
    if ($winget) {
        # Muchos plugins (sobre todo antiguos) exigen versiones concretas del runtime de C++.
        $ids = @(
            'Microsoft.VCRedist.2005.x86', 'Microsoft.VCRedist.2005.x64',
            'Microsoft.VCRedist.2008.x86', 'Microsoft.VCRedist.2008.x64',
            'Microsoft.VCRedist.2010.x86', 'Microsoft.VCRedist.2010.x64',
            'Microsoft.VCRedist.2012.x86', 'Microsoft.VCRedist.2012.x64',
            'Microsoft.VCRedist.2013.x86', 'Microsoft.VCRedist.2013.x64',
            'Microsoft.VCRedist.2015+.x86', 'Microsoft.VCRedist.2015+.x64',
            'Microsoft.DirectX',
            '7zip.7zip'                                          # muchos plugins vienen en .rar/.7z
        )
        foreach ($id in $ids) {
            Log "winget: $id"
            winget install --id $id -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity | Out-Null
            # 0 instalado · 3010 pide reinicio · los negativos = ya estaba instalado / sin cambios
            if ($LASTEXITCODE -notin 0, 3010, -1978335189, -1978335135, -1978334963) { $failed += $id; Log "  $id no se instaló (código $LASTEXITCODE)" 'AVISO' }
        }
    } else {
        $failed += 'winget no disponible'
    }


    if ($failed.Count -eq 0) { Result 'Runtimes' 'OK' 'Visual C++ 2005-2022, DirectX 9, 7-Zip' }
    else { Result 'Runtimes' 'AVISO' ("no se instalaron: " + ($failed -join ', ')) }
}

# ---------------------------------------------------------------------------
#  5. Modo estudio: baja latencia de audio sin tocar la seguridad
# ---------------------------------------------------------------------------
Step 'Modo estudio (latencia de audio)'
try {
    # Plan de energía propio a partir de "Máximo rendimiento".
    $dup = powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 2>$null
    if (-not ($dup -match '([0-9a-f-]{36})')) { $dup = powercfg -duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c }
    $plan = [regex]::Match(($dup -join ' '), '[0-9a-f]{8}-[0-9a-f-]{27}').Value
    powercfg -changename $plan 'Kairos Estudio' 'Rendimiento constante para producir música' | Out-Null
    $ac = @(
        @('54533251-82be-4824-96c1-47b60b740d00', '893dee8e-2bef-41e0-89c6-b55d0929964c', 100),  # CPU mínima 100 %
        @('54533251-82be-4824-96c1-47b60b740d00', 'bc5038f7-23e0-4960-96da-33abaf5935ec', 100),  # CPU máxima 100 %
        @('54533251-82be-4824-96c1-47b60b740d00', '0cc5b647-c1df-4637-891a-dec35c318583', 100),  # sin aparcar núcleos
        @('2a737441-1930-4402-8d77-b2bebba308a3', '48e6b7a6-50f5-4782-a5d4-53bb8f07e226', 0),    # USB nunca en suspensión (interfaz de audio)
        @('501a4d13-42af-4429-9fd1-a8218c268e20', 'ee12f906-d277-404b-b6da-e5fa1a576df5', 0),    # PCIe sin ahorro
        @('0012ee47-9041-4b5d-9b77-535fba8b1442', '6738e2c4-e8a5-4a42-b16a-e040e769756e', 0),    # disco nunca se apaga
        @('238c9fa8-0aad-41ed-83f4-97be242c8f20', '29f6c1db-86da-48c5-9fdb-f2b67b1f44da', 0),    # enchufado: nunca suspender
        @('19cbb8fa-5279-450e-9fac-8a3d5fedd0c1', '12bbebe6-58d6-4636-95bb-3217ef867c1a', 0),    # Wi-Fi sin ahorro
        @('7516b95f-f776-4464-8c53-06167f40cc99', '3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e', 1800)  # pantalla: 30 min
    )
    foreach ($s in $ac) { powercfg -setacvalueindex $plan $s[0] $s[1] $s[2] | Out-Null }
    # Con batería: USB sigue sin suspenderse (para grabar fuera), lo demás ahorra.
    powercfg -setdcvalueindex $plan 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0 | Out-Null
    powercfg -setactive $plan | Out-Null
    powercfg -hibernate off | Out-Null
    Log 'Plan de energía "Kairos Estudio" activo.' 'OK'

    # Planificador multimedia: más CPU reservada para el audio en tiempo real.
    $mm = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
    Set-Reg $mm 'SystemResponsiveness' 10
    Set-Reg $mm 'NetworkThrottlingIndex' 0xFFFFFFFF
    # Prioridad a servicios en segundo plano: lo que recomienda Steinberg para ASIO.
    Set-Reg 'HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl' 'Win32PrioritySeparation' 0x18

    # Los hubs USB no se apagan para ahorrar energía (cortes de la interfaz de audio).
    Get-CimInstance -Namespace root\wmi -ClassName MSPower_DeviceEnable -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceName -like 'USB\ROOT_HUB*' -or $_.InstanceName -like 'USB\VID_*' } |
        ForEach-Object { Set-CimInstance -InputObject $_ -Property @{ Enable = $false } -ErrorAction SilentlyContinue }

    # Sin sonidos del sistema en mitad de una sesión (solo el de arranque de Kairos).
    Set-Reg 'HKCU:\AppEvents\Schemes' '(Default)' '.None' 'String'
    Get-ChildItem 'HKCU:\AppEvents\Schemes\Apps' -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -eq '.Current' } |
        ForEach-Object { Set-ItemProperty -Path $_.PSPath -Name '(Default)' -Value '' -ErrorAction SilentlyContinue }

    # Nada de grabación de juegos en segundo plano.
    Set-Reg 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled' 0
    Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR' 'AllowGameDVR' 0

    # Windows Update sigue activo, pero no reinicia con la sesión abierta ni sube a otros PCs.
    Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'NoAutoRebootWithLoggedOnUsers' 1
    Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' 'DODownloadMode' 0

    # Apps en segundo plano y consejos fuera.
    Set-Reg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications' 'GlobalUserDisabled' 1
    Set-Reg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338389Enabled' 0
    Set-Service -Name DiagTrack -StartupType Disabled -ErrorAction SilentlyContinue

    Result 'Modo estudio' 'OK' 'plan Kairos Estudio, USB sin suspensión, MMCSS, sin sonidos del sistema'
} catch {
    Log "Modo estudio: $($_.Exception.Message)" 'ERROR'
    Result 'Modo estudio' 'ERROR' $_.Exception.Message
}

# ---------------------------------------------------------------------------
#  6. ASUS: G-Helper en vez de Armoury Crate (ligero, sin servicios pesados)
# ---------------------------------------------------------------------------
Step 'ASUS (G-Helper)'
$isAsus = (Get-CimInstance Win32_ComputerSystem).Manufacturer -match 'ASUS'
if (-not $isAsus) {
    Result 'ASUS' 'SALTADO' 'no es un portátil ASUS'
} elseif (-not $online) {
    Result 'ASUS' 'SALTADO' 'sin internet'
} else {
    try {
        $rel = Invoke-RestMethod 'https://api.github.com/repos/seerge/g-helper/releases/latest' -TimeoutSec 20
        $asset = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
        $dest = Join-Path $KairosDir 'GHelper'
        New-Item -ItemType Directory -Force $dest | Out-Null
        $zip = Join-Path $env:TEMP $asset.name
        Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing
        Expand-Archive $zip -DestinationPath $dest -Force
        Remove-Item $zip -Force
        $ws = New-Object -ComObject WScript.Shell
        $lnk = $ws.CreateShortcut("$env:ProgramData\Microsoft\Windows\Start Menu\Programs\G-Helper.lnk")
        $lnk.TargetPath = (Get-ChildItem $dest -Filter GHelper.exe -Recurse | Select-Object -First 1).FullName
        $lnk.Save()
        Log "G-Helper $($rel.tag_name) en $dest" 'OK'
        Result 'ASUS' 'OK' "G-Helper $($rel.tag_name): ventiladores, modo GPU y batería. Ábrelo una vez y activa 'Run on Startup'"
    } catch {
        Log "G-Helper: $($_.Exception.Message)" 'AVISO'
        Result 'ASUS' 'AVISO' 'no se pudo descargar G-Helper (github.com/seerge/g-helper)'
    }
}

# ---------------------------------------------------------------------------
#  7. Kairos: dock, KaiSpot, menú, explorador, atajos y aspecto
# ---------------------------------------------------------------------------
Step 'Capa Kairos'
try {
    $apps = Join-Path $Pkg 'apps'
    $inst = Join-Path $env:LOCALAPPDATA 'Kairos'
    Get-Process KairosDock, KairosSpot, KairosMenu, KairosFiles, KairosKeys -ErrorAction SilentlyContinue | Stop-Process -Force

    # Restos de versiones anteriores (VM de pruebas): el ayudante que ocultaba la
    # barra y los accesos de arranque viejos abrirían una segunda copia de cada app.
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'KairosHideTaskbar' -ErrorAction SilentlyContinue
    $startup = [Environment]::GetFolderPath('Startup')
    foreach ($l in 'KaiSpot.lnk', 'KairosSpot.lnk', 'KairosMenu.lnk', 'KairosKeys.lnk', 'KairosDock.lnk') {
        Remove-Item (Join-Path $startup $l) -Force -ErrorAction SilentlyContinue
    }
    Remove-Item 'C:\Kairos\Dock', "$env:LOCALAPPDATA\Kairos\KaiSpot" -Recurse -Force -ErrorAction SilentlyContinue
    foreach ($app in 'KairosDock', 'KairosSpot', 'KairosMenu', 'KairosFiles', 'KairosKeys') {
        New-Item -ItemType Directory -Force "$inst\$app" | Out-Null
        Copy-Item "$apps\$app\*" "$inst\$app\" -Recurse -Force
    }

    # Dock = shell completo: sustituye a la barra de Windows (bandeja incluida)
    # y reserva su franja para que FL Studio maximizado no quede debajo.
    @'
{
  "autoHide": false,
  "autoStart": true,
  "hideWindowsTaskbar": true,
  "appearance": {
    "iconSize": 44, "iconSpacing": 12, "maxScale": 1.6, "influence": 90,
    "cornerRadius": 22, "bottomMargin": 0, "blur": false, "showClock": true
  },
  "items": [
    { "name": "Inicio", "path": "kairos:start-menu" },
    { "name": "Explorador de archivos", "path": "%WINDIR%\\explorer.exe" },
    { "name": "IA de Kairos", "path": "kairos:ai" }
  ]
}
'@ | Set-Content "$inst\KairosDock\kairos-dock.json" -Encoding UTF8

    # Arranque con la sesión (sin permisos de administrador, como debe ser).
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-Reg $run 'KairosDock' "`"$inst\KairosDock\KairosDock.exe`"" 'String'
    Set-Reg $run 'KaiSpot' "`"$inst\KairosSpot\KairosSpot.exe`"" 'String'
    Set-Reg $run 'KairosMenu' "`"$inst\KairosMenu\KairosMenu.exe`"" 'String'
    Set-Reg $run 'KairosKeys' "`"$inst\KairosKeys\KairosKeys.exe`"" 'String'

    # --- Aspecto ---------------------------------------------------------------
    $assets = Join-Path $Pkg 'assets'
    New-Item -ItemType Directory -Force "$KairosDir\Wallpapers", "$KairosDir\Icons" | Out-Null
    Copy-Item "$assets\Kairos_Wall_*.png" "$KairosDir\Wallpapers\" -Force
    Copy-Item "$assets\Layer1-Materials\*.ico" "$KairosDir\Icons\" -Force
    Copy-Item "$assets\Kairos-Startup.wav" $KairosDir -Force
    $wall = "$KairosDir\Wallpapers\Kairos_Wall_4_Particles.png"

    $pers = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
    Set-Reg $pers 'AppsUseLightTheme' 0
    Set-Reg $pers 'SystemUsesLightTheme' 0
    Set-Reg $pers 'EnableTransparency' 1
    Set-Reg $pers 'ColorPrevalence' 1

    $accent = 0xFFF55B5B   # #5B5BF5 en ABGR
    $dwm = 'HKCU:\Software\Microsoft\Windows\DWM'
    Set-Reg $dwm 'AccentColor' $accent
    Set-Reg $dwm 'ColorizationColor' $accent
    Set-Reg $dwm 'ColorizationAfterglow' $accent
    Set-Reg $dwm 'ColorPrevalence' 1
    $acc = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'
    Set-Reg $acc 'AccentPalette' ([byte[]](0x9b,0x9b,0xfb,0,0x7e,0x7e,0xf9,0,0x5b,0x5b,0xf5,0,0x49,0x49,0xc4,0,0x37,0x37,0x93,0,0x25,0x25,0x62,0,0x13,0x13,0x31,0,0xf5,0x5b,0x5b,0)) 'Binary'
    Set-Reg $acc 'StartColorMenu' $accent
    Set-Reg $acc 'AccentColorMenu' $accent

    Set-Reg 'HKCU:\Control Panel\Desktop' 'WallpaperStyle' '10' 'String'
    Set-Reg 'HKCU:\Control Panel\Desktop' 'TileWallpaper' '0' 'String'
    Set-Reg 'HKCU:\Control Panel\Desktop' 'Wallpaper' $wall 'String'

    # Pantalla de bloqueo e inicio de sesión con el fondo de Kairos.
    $csp = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'
    Set-Reg $csp 'LockScreenImagePath' $wall 'String'
    Set-Reg $csp 'LockScreenImageUrl' $wall 'String'
    Set-Reg $csp 'LockScreenImageStatus' 1
    $cdm = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
    Set-Reg $cdm 'RotatingLockScreenEnabled' 0
    Set-Reg $cdm 'RotatingLockScreenOverlayEnabled' 0
    Set-Reg $cdm 'SubscribedContent-338387Enabled' 0
    Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'DisableAcrylicBackgroundOnLogon' 1

    # Iconos de papelera, carpetas y disco.
    $rb = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\DefaultIcon'
    Set-Reg $rb '(Default)' "$KairosDir\Icons\trash-empty.ico" 'String'
    Set-Reg $rb 'empty' "$KairosDir\Icons\trash-empty.ico" 'String'
    Set-Reg $rb 'full' "$KairosDir\Icons\trash-full.ico" 'String'
    $si = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons'
    Set-Reg $si '3' "$KairosDir\Icons\folder.ico" 'String'
    Set-Reg $si '4' "$KairosDir\Icons\folder-open.ico" 'String'
    Set-Reg 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\C\DefaultIcon' '(Default)' "$KairosDir\Icons\drive.ico" 'String'

    # Explorador: extensiones visibles (.wav, .flp...), sin recientes ni sugerencias.
    $adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
    Set-Reg $adv 'HideFileExt' 0
    Set-Reg $adv 'TaskbarAl' 1
    Set-Reg $adv 'ShowTaskViewButton' 0

    # Sonido de arranque de Kairos.
    Set-Reg 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation' 'DisableStartupSound' 0
    Set-Reg 'HKCU:\AppEvents\Schemes\Apps\.Default\WindowsLogon\.Current' '(Default)' "$KairosDir\Kairos-Startup.wav" 'String'

    # Fuente Inter (instalada, sin sustituir la del sistema).
    if ($online) {
        try {
            $interZip = Join-Path $env:TEMP 'inter.zip'
            Invoke-WebRequest 'https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip' -OutFile $interZip -UseBasicParsing
            $tmp = Join-Path $env:TEMP 'inter'
            Expand-Archive $interZip -DestinationPath $tmp -Force
            Get-ChildItem $tmp -Recurse -Filter 'Inter*.ttf' | Where-Object { $_.DirectoryName -notmatch 'variable' } | ForEach-Object {
                if (Test-Path "$env:WINDIR\Fonts\$($_.Name)") { return }   # ya instalada (y en uso)
                Copy-Item $_.FullName "$env:WINDIR\Fonts\" -Force
                Set-Reg 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts' "$($_.BaseName) (TrueType)" $_.Name 'String'
            }
            Remove-Item $interZip, $tmp -Recurse -Force -ErrorAction SilentlyContinue
        } catch { Log "Inter: $($_.Exception.Message)" 'AVISO' }
    }

    Log 'Kairos instalado. Arranca solo en el próximo inicio de sesión.' 'OK'
    Result 'Kairos' 'OK' 'dock como shell con bandeja propia, KaiSpot (Alt+Espacio), menú (Ctrl+Mayús+Espacio), atajos Win+Alt'
} catch {
    Log "Kairos: $($_.Exception.Message)" 'ERROR'
    Result 'Kairos' 'ERROR' $_.Exception.Message
}

# ---------------------------------------------------------------------------
#  Español: esperar a que termine y aplicarlo
# ---------------------------------------------------------------------------
if ($langJob) {
    Step 'Terminando el español'
    Log 'Esperando a que Windows termine de instalar el idioma (puede tardar)...'
    Wait-Job $langJob | Out-Null
    try {
        Receive-Job $langJob -ErrorAction Stop | Out-Null
        Set-SystemPreferredUILanguage -Language es-ES
        Set-WinUILanguageOverride -Language es-ES
        $list = New-WinUserLanguageList -Language es-ES          # trae el teclado español
        Set-WinUserLanguageList -LanguageList $list -Force
        Set-Culture -CultureInfo es-ES
        Set-WinHomeLocation -GeoId 217                            # España
        Set-WinSystemLocale -SystemLocale es-ES
        tzutil /s 'Romance Standard Time'
        Copy-UserInternationalSettingsToSystem -WelcomeScreen $true -NewUser $true
        Log 'Español instalado (se ve completo tras reiniciar).' 'OK'
        Result 'Idioma' 'OK' 'español de España, teclado ES, hora de Madrid'
    } catch {
        Log "Idioma: $($_.Exception.Message)" 'ERROR'
        Result 'Idioma' 'ERROR' 'instálalo en Configuración > Hora e idioma'
    }
    Remove-Job $langJob -Force
}

# ---------------------------------------------------------------------------
#  .NET Framework 3.5 (después del idioma: los dos usan el instalador de componentes)
# ---------------------------------------------------------------------------
& {
    Step '.NET Framework 3.5'   # con internet va por Windows Update; sin él, sale de la ISO del USB
    # .NET Framework 3.5 (lo usan instaladores y plugins viejos)
    $netfx = Enable-WindowsOptionalFeature -Online -FeatureName NetFx3 -All -NoRestart -ErrorAction SilentlyContinue
    if (-not $netfx) {
        # Sin Windows Update: usar la ISO de Kairos que está en el mismo USB.
        $iso = Get-ChildItem -Path ((Get-PSDrive -PSProvider FileSystem).Root) -Filter 'Kairos*.iso' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($iso) {
            $d = (Mount-DiskImage -ImagePath $iso.FullName -PassThru | Get-Volume).DriveLetter
            $netfx = Enable-WindowsOptionalFeature -Online -FeatureName NetFx3 -All -NoRestart -Source "${d}:\sources\sxs" -LimitAccess -ErrorAction SilentlyContinue
            Dismount-DiskImage -ImagePath $iso.FullName | Out-Null
        }
    }
    if ($netfx) { $Report['.NET 3.5'] = 'OK' } else { $Report['.NET 3.5'] = 'AVISO — actívalo en Características de Windows' }
}

# ---------------------------------------------------------------------------
#  8. Licencia de Windows (clave legal; nunca se guarda en ningún archivo)
# ---------------------------------------------------------------------------
Step 'Licencia de Windows'
$lic = Get-CimInstance SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL AND Name LIKE 'Windows%'" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($lic -and $lic.LicenseStatus -eq 1) {
    Result 'Licencia' 'OK' 'Windows ya está activado'
} else {
    Write-Host ''
    Write-Host '  Windows no está activado. Si tienes una clave de Windows 11 Pro, escríbela ahora.' -ForegroundColor Yellow
    $secure = Read-Host '  Clave (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX) o Enter para saltar' -AsSecureString
    $key = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    if ($key -match '^[A-Za-z0-9]{5}(-[A-Za-z0-9]{5}){4}$') {
        cscript //nologo "$env:WINDIR\System32\slmgr.vbs" /ipk $key | Out-Null
        cscript //nologo "$env:WINDIR\System32\slmgr.vbs" /ato | Out-Null
        $key = $null
        $lic = Get-CimInstance SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL AND Name LIKE 'Windows%'" | Select-Object -First 1
        if ($lic.LicenseStatus -eq 1) { Result 'Licencia' 'OK' 'activado con la clave introducida' }
        else { Result 'Licencia' 'AVISO' 'clave instalada pero sin activar todavía (necesita internet)' }
    } else {
        Result 'Licencia' 'PENDIENTE' 'sin activar: Configuración > Sistema > Activación'
    }
}

# ---------------------------------------------------------------------------
#  Antivirus: solo se informa (la v0.2 de la ISO puede venir sin Defender)
# ---------------------------------------------------------------------------
if (Get-Service WinDefend -ErrorAction SilentlyContinue) { Result 'Antivirus' 'Defender activo' }
else { Result 'Antivirus' 'SIN ANTIVIRUS' 'ISO sin Defender: instala solo software de fuentes de confianza' }

# ---------------------------------------------------------------------------
#  Informe
# ---------------------------------------------------------------------------
$desktop = [Environment]::GetFolderPath('Desktop')
$txt = @('KAIROS — informe de instalación', ('=' * 40), (Get-Date -Format 'yyyy-MM-dd HH:mm'), '')
foreach ($k in $Report.Keys) { $txt += ('{0,-14} {1}' -f $k, $Report[$k]) }
$txt += ''
$txt += 'Siguientes pasos:'
$txt += '  1. Reinicia el equipo (idioma y Kairos se activan al entrar).'
$txt += '  2. Instala FL Studio desde image-line.com y tus plugins.'
$txt += '  3. Si usas una interfaz de audio, instala su driver ASIO del fabricante.'
$txt += '  4. En FL Studio: Opciones > Audio > dispositivo ASIO de tu interfaz'
$txt += '     (o "FL Studio ASIO" con el audio del portátil). Buffer 256-512 para mezclar.'
$txt += '  5. Abre G-Helper y deja el modo "Balanced" o "Turbo" enchufado.'
$txt += ''
$txt += "Registro completo: $LogFile"
$txt | Set-Content (Join-Path $desktop 'Kairos - Informe de instalación.txt') -Encoding UTF8
# Copia del registro junto al instalador (USB o carpeta compartida) para revisarlo fuera.
Copy-Item $LogFile (Join-Path $Pkg "setup-$env:COMPUTERNAME.log") -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host '  ================= RESUMEN =================' -ForegroundColor Magenta
foreach ($k in $Report.Keys) { Write-Host ('  {0,-14} {1}' -f $k, $Report[$k]) }
Write-Host ''
$r = Read-Host '  Hay que reiniciar para terminar. ¿Reiniciar ahora? (S/N)'
if ($r -match '^[sS]') { Restart-Computer -Force }
