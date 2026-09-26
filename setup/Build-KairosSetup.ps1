<#
.SYNOPSIS
    Monta la carpeta KairosSetup (la que va al USB junto a la ISO) desde el repo:
    compila las 5 apps, copia el post-install, los assets y, con -ExportDrivers,
    los drivers del equipo donde se ejecuta.

.EXAMPLE
    .\Build-KairosSetup.ps1 -Out D:\KairosSetup
    .\Build-KairosSetup.ps1 -Out D:\KairosSetup -ExportDrivers   # como administrador
#>
param(
    [Parameter(Mandatory)] [string]$Out,
    [switch]$ExportDrivers,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
New-Item -ItemType Directory -Force $Out, "$Out\setup", "$Out\apps", "$Out\assets" | Out-Null

# 1. Apps (exe único autocontenido: funcionan sin instalar .NET)
if (-not $SkipBuild) {
    # Un SDK instalado por usuario manda sobre el dotnet del sistema (que puede ser solo runtime).
    if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") { $env:Path = "$env:USERPROFILE\.dotnet;" + $env:Path }
    $ErrorActionPreference = 'Continue'   # dotnet escribe avisos por stderr
    $projects = [ordered]@{
        KairosDock  = "$repo\dock\src\KairosDock\KairosDock.csproj"
        KairosSpot  = "$repo\apps\KairosSpot\KairosSpot.csproj"
        KairosMenu  = "$repo\apps\KairosMenu\KairosMenu.csproj"
        KairosFiles = "$repo\apps\KairosFiles\KairosFiles.csproj"
        KairosKeys  = "$repo\apps\KairosKeys\KairosKeys.csproj"
    }
    foreach ($name in $projects.Keys) {
        Write-Host "Compilando $name..."
        dotnet publish $projects[$name] -c Release -r win-x64 --self-contained true `
            /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
            -o "$Out\apps\$name" -v q -nologo
        if ($LASTEXITCODE -ne 0) { throw "Falló la compilación de $name" }
        Remove-Item "$Out\apps\$name\*.pdb" -ErrorAction SilentlyContinue
    }
}

# 2. Post-install y assets
Copy-Item "$repo\setup\Kairos-Setup.ps1" "$Out\setup\" -Force
Copy-Item "$repo\setup\INSTALAR-KAIROS.cmd", "$repo\setup\LEEME.txt" $Out -Force
Copy-Item "$repo\assets\*" "$Out\assets\" -Recurse -Force

# 3. Drivers del equipo actual (solo hardware del portátil, nada de apps de terceros)
if ($ExportDrivers) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')
    if (-not $isAdmin) { throw 'Exportar drivers necesita PowerShell como administrador.' }
    $skip = 'Oracle|VirtualBox|DEV47|DroidCam|Apple|Focusrite|VMware|Parallels|OpenVPN|WireGuard|Mullvad|TAP-Windows|Wintun|Cheat|Logitech Gaming'
    $dest = "$Out\drivers"
    Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $dest | Out-Null
    $kept = 0
    foreach ($d in Get-WindowsDriver -Online) {
        if ("$($d.ProviderName) $($d.OriginalFileName)" -match $skip) { continue }
        $sub = Join-Path $dest ("{0}_{1}" -f ($d.ProviderName -replace '[^\w]', ''), [IO.Path]::GetFileNameWithoutExtension($d.OriginalFileName))
        pnputil /export-driver $d.Driver $sub | Out-Null
        $kept++
    }
    $size = (Get-ChildItem $dest -Recurse -File | Measure-Object Length -Sum).Sum / 1GB
    Write-Host ("Drivers exportados: {0} paquetes, {1:N1} GB" -f $kept, $size)
}

$total = (Get-ChildItem $Out -Recurse -File | Measure-Object Length -Sum).Sum / 1GB
Write-Host ("KairosSetup listo en {0} ({1:N1} GB)" -f $Out, $total)
