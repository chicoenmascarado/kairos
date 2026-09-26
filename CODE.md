# Kairos — source code

Pre-alpha. Every component is a native Windows app in C# / WPF / .NET 8.

| Folder | What it is |
|---|---|
| `dock/` | **KairosDock** — macOS-style dock: spring magnification, live window previews, control center, system tray. See `dock/README.md`. |
| `apps/KairosSpot` | **KaiSpot** — global launcher and search (`Alt+Space`): apps, recent files, quick maths, settings, web. |
| `apps/KairosMenu` | Kairos start menu (app index + usage ranking). |
| `apps/KairosFiles` | Kairos file browser. |
| `apps/KairosKeys` | Global keyboard shortcuts. |
| `scripts/` | PowerShell setup for a Kairos machine: post-install, theme, debloat, cleanup, audit, lock screen, Inter font, "Layer 1" visuals and per-app installers. **Run as admin inside a test VM, not on your main PC.** |
| `assets/` | Logo, wallpapers, startup sound and Layer 1 icons. |

The Kairos ISO and the landing page (`chicoenmascarado/kairosweb`) live elsewhere.

## Build

Requires the .NET 8 SDK. Each app ships as a self-contained single-file exe:

```
dotnet publish <Project>.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

KairosDock targets `net8.0-windows10.0.19041.0` (WinRT APIs for radios and media).
