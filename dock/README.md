# KairosDock

A premium, macOS-style dock for Windows — built in **C# / WPF / .NET 8**.

> *"The perfect moment when everything flows."*

A floating, frosted-glass dock anchored to the bottom-centre of your screen, with
**buttery-smooth, spring-physics magnification**: icons scale up fluidly as the
cursor approaches and neighbours lift along a continuous falloff curve, just like
macOS — no steps, no jank, GPU-composited at 60fps.

Part of the **Kairos** identity: deep dark (`#08080F` / `#0D0D1A`), violet accent
(`#5B5BF5`), soft purple (`#A78BFA`), glassmorphism, calm premium motion.

---

## Features

- **Floating glass dock**, bottom-centre, always-on-top, no taskbar entry, no
  Alt-Tab entry, never steals focus from your apps.
- **Click-through where empty** — only the glass panel and icons are interactive;
  clicks on blank space fall through to whatever is behind.
- **macOS magnification** with a smooth Gaussian falloff and per-icon **damped
  springs** for a lively, physical settle.
- **Real glass**: dark violet-tinted translucent panel, 24px rounded corners, a
  1px soft-light border and a soft drop shadow, plus a best-effort DWM blur-behind.
- **Launch or focus** — left-click launches the app, or foregrounds it if it's
  already running.
- **Subtle launch bounce** and a **gentle entrance slide-and-fade** on startup.
- **Violet "running" dot** under apps that are currently running.
- **Right-click menu** — *Remove from dock* (persisted to the config), with room
  for more options later.
- **Auto-hide** option (slide away when idle, reveal at the bottom edge) — a config
  toggle, **OFF by default**.
- **Graceful icon fallback** — apps whose icon can't be extracted (e.g. Store
  app-execution aliases) get a tasteful violet letter tile.
- High-DPI / Per-Monitor-v2 aware, crisp jumbo (256px) icons pulled straight from
  each executable.

---

## Requirements

- Windows 10 / 11
- **.NET 8 SDK** — <https://dotnet.microsoft.com/download/dotnet/8.0>

(The runtime alone is enough to *run* a published build, but the SDK is needed for
`dotnet run` / `dotnet build`.)

---

## Build & Run

```powershell
# from the repository root
dotnet run --project src/KairosDock/KairosDock.csproj
```

Or build and launch the executable:

```powershell
dotnet build -c Release
.\src\KairosDock\bin\Release\net8.0-windows\KairosDock.exe
```

The dock fades and slides up from the bottom-centre of your primary monitor. Move
your cursor across it to feel the magnification. To quit, end the `KairosDock`
process (there's intentionally no window chrome).

---

## Configuration — `kairos-dock.json`

A copy lives next to the built executable (it's copied from
`src/KairosDock/kairos-dock.json` on build). Edit that file and restart the dock.

```jsonc
{
  "autoHide": false,                 // slide away when idle; reveal at the screen's bottom edge

  "appearance": {
    "iconSize": 52,                  // resting icon size (DIPs)
    "iconSpacing": 16,               // gap between icons (DIPs)
    "maxScale": 1.9,                 // peak magnification of the icon under the cursor
    "influence": 95,                 // how far the magnification "bump" reaches (DIPs)
    "cornerRadius": 24,              // glass panel corner radius
    "bottomMargin": 14               // distance above the taskbar
  },

  "items": [
    {
      "name": "File Explorer",
      "path": "%WINDIR%\\explorer.exe"
    },
    {
      "name": "Notepad",
      "path": "%WINDIR%\\System32\\notepad.exe",
      "arguments": "",               // optional launch arguments
      "icon": "C:\\path\\to\\icon.png" // optional explicit .png/.ico; otherwise pulled from the exe
    }
  ]
}
```

- **Environment variables** in `path` / `icon` (e.g. `%WINDIR%`, `%LOCALAPPDATA%`)
  are expanded automatically.
- Icons are read from the target **executable** by default, or from an explicit
  `icon` file if you provide one. Apps without an extractable icon (such as the
  Windows Terminal `wt.exe` alias) fall back to a violet letter tile — set `icon`
  to give them real artwork.

---

## Tuning the feel

The magnification "feel" is deliberately isolated so it's easy to iterate on:

| What | Where |
| --- | --- |
| The falloff **curve** (Gaussian bump, peak scale, reach) | `Motion/MagnificationEngine.cs` |
| The **spring physics** (stiffness / damping / settle) | `Motion/Spring.cs` |
| Per-icon spring + **launch bounce** tuning | `DockIcon` ctor in `MainWindow.xaml.cs` |
| The dock-wide **entrance / auto-hide** slide spring | `_slideSpring` in `MainWindow.xaml.cs` |

A lower `damping` (relative to `stiffness`) gives more overshoot/bounce; a higher
`influence` spreads the magnification across more neighbours.

---

## Project structure

```
KairosDock.sln
src/KairosDock/
├─ App.xaml(.cs)            App startup + Kairos colour palette
├─ MainWindow.xaml(.cs)     Window setup, the 60fps animation loop, layout, input
├─ app.manifest            Per-Monitor-v2 DPI awareness
├─ kairos-dock.json        Example config (Explorer, Edge, Terminal, PowerShell, Notepad)
├─ Models/
│  ├─ DockConfig.cs        Config + appearance/motion knobs
│  └─ DockItem.cs          A single pinned app
├─ Motion/
│  ├─ MagnificationEngine.cs   Pure magnification math (Gaussian falloff)
│  └─ Spring.cs                Damped-spring integrator (the "physical" feel)
├─ Services/
│  ├─ ConfigService.cs     Load/save kairos-dock.json
│  └─ AppLauncher.cs       Launch-or-focus
└─ Interop/
   ├─ AcrylicGlass.cs      DWM blur-behind (best-effort)
   └─ IconExtractor.cs     Jumbo shell icons, no System.Drawing dependency
```

## How the smoothness works

There's a single `CompositionTarget.Rendering` loop (one clock, ~60fps). Each
frame it:

1. Reads the cursor X and asks `MagnificationEngine` for each icon's **target**
   scale via a smooth Gaussian — `1 + (maxScale−1)·e^(−d²/2σ²)` — so there are no
   kinks anywhere in the curve.
2. Advances each icon's scale toward that target with a **damped spring** (slightly
   under-damped, so icons keep gliding for a beat after the cursor stops — that
   trailing settle is what reads as "physical").
3. Re-lays-out the panel so its **width tracks the summed scaled-icon widths** and
   stays centred — the dock visibly grows and shrinks as you sweep across it.

All scaling is done with GPU-composited `RenderTransform`s, anchored at each icon's
bottom-centre so icons grow upward out of the glass — the signature macOS lift.

---

## Notes & limitations (v1)

- Shows on the **primary monitor** only (multi-monitor support is scaffolded via
  DPI awareness and is the natural next step).
- The DWM blur-behind is best-effort; if the OS declines it, the layered violet
  glass still looks great on its own.
- Quitting is via Task Manager / `Stop-Process KairosDock` — no tray icon yet.
