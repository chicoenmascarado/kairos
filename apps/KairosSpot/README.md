# KaiSpot

Lanzador / buscador global de Kairos. Estilo Spotlight (macOS), estetica glass
violeta. Se invoca con **Alt + Space**.

## Que hace (v1)

- **Lanzar apps** instaladas (indexa los accesos del menu inicio).
- **Buscar archivos recientes** (carpeta Recent de Windows).
- **Calculos rapidos**: escribe `2+2`, `15*1.21`, `(8+2)^2`... y Enter copia el resultado.
- **Ajustes del sistema**: escribe `wifi`, `bluetooth`, `pantalla`, `tareas`... y abre el panel directo.
- **Busqueda web**: siempre disponible como ultima opcion.
- **Navegacion**: flechas arriba/abajo, Enter para abrir, Esc para cerrar.
- Se oculta solo al perder el foco.

## Como compilar (en tu PC real, con .NET 8 SDK)

1. Abre una terminal en esta carpeta.
2. Ejecuta `publish.bat` (o el comando de abajo a mano).
3. El ejecutable queda en:
   `bin\Release\net8.0-windows\win-x64\publish\KairosSpot.exe`

Comando de publicacion (identico al patron del dock):

```
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

## Como probar en la VM

1. Copia `KairosSpot.exe` a la VM (p. ej. a `C:\Kairos\KaiSpot\`).
2. Ejecutalo (doble clic). No abre ninguna ventana: queda residente.
3. Pulsa **Alt + Space** -> aparece KaiSpot centrado.
4. Escribe, navega con flechas, Enter para abrir, Esc para cerrar.

Para que arranque con Windows: crea un acceso directo a `KairosSpot.exe` en
`shell:startup` (lo automatizaremos en el playbook mas adelante).

## Arquitectura del codigo

| Archivo | Responsabilidad |
|---|---|
| `App.xaml(.cs)` | Arranque. Crea la ventana, fuerza su handle y la deja oculta. |
| `MainWindow.xaml` | UI glass (caja de busqueda + lista de resultados). |
| `MainWindow.xaml.cs` | Acrylic DWM, hotkey global, navegacion, ejecucion. |
| `Interop.cs` | Llamadas Win32/DWM (backdrop, esquinas, hotkey). |
| `SearchEngine.cs` | Orquesta y puntua resultados por relevancia. |
| `AppIndexer.cs` | Indexa apps desde los .lnk del menu inicio. |
| `SettingsCatalog.cs` | Mapeo palabra-clave -> panel de ajustes. |
| `Calculator.cs` | Evaluador aritmetico propio (sin dependencias). |
| `SearchResult.cs` | Modelo de un resultado. |

## Notas tecnicas (build 26200)

- El glass usa el **backdrop nativo de Win11** (`DWMWA_SYSTEMBACKDROP_TYPE` = Acrylic),
  que respeta esquinas redondeadas. Hay fallback al acrylic legacy si no estuviera.
- `AllowsTransparency` esta en **False** a proposito: es incompatible con el backdrop
  nativo de DWM. No lo cambies a True o perderas el glass del sistema.
- El hotkey global usa `RegisterHotKey` (Alt+Space). Si otra app ya lo tiene tomado,
  el registro falla en silencio; en ese caso elegiremos otra combinacion.

## Pendiente / siguientes pasos

- Iconos reales de las apps (ahora se usa un glifo generico glass).
- Indexado de archivos mas alla de "recientes" (Windows Search API).
- Persistir historial de uso para ordenar por frecuencia.
- Integrar en el playbook + arranque automatico.
- Base sobre la que vivira la IA (Fase 3): el campo de KaiSpot sera tambien la
  entrada del asistente.
