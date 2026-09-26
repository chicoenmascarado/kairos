# Kairos ISO v0.2 con NTLite

Parte de `Kairos-v0.1.iso`. Unos 20-30 minutos, casi todo esperando.

## 1. Cargar
1. NTLite → **Añadir → Archivo de imagen (ISO)** → `Downloads\Kairos-v0.1.iso`. Deja que extraiga.

## 2. Solo Windows 11 Pro
2. En la lista de ediciones, selecciona **todas menos "Windows 11 Pro"** → clic derecho → **Eliminar**.
   (De 11 ediciones a 1: la ISO baja de 8,5 GB a unos 5 GB.)
3. Doble clic en **Windows 11 Pro** para cargarla.

## 3. Quitar Defender
4. **Componentes** → busca `Defender`.
5. Marca para quitar **Microsoft Defender Antivirus / Windows Defender** (y lo que cuelgue de él).
   Si NTLite avisa de dependencias, acepta. **No** quites SmartScreen si lo pide otra cosa que uses.
6. En **Compatibilidad**, deja activado lo que ya estaba (Windows Update, Store, WLAN, USB...).

## 4. Edge / WebView2 al día
7. **Configuración → Tareas de Windows Update** → `EdgeUpdate` → **Predeterminado / Activado**.
   Sin esto WebView2 no se actualiza, y lo usan los instaladores de plugins (Native Access, Splice...).

## 5. Sin respuestas propias en la ISO
8. **Desatendido** → **desactívalo** (interruptor arriba).
   El archivo de respuestas lo pone Ventoy (`ventoy\kairos-unattend.xml`): región, teclado,
   hora, cuenta local y el arranque automático de Kairos Setup. Si la ISO llevara el suyo,
   seguiría apuntando a la edición nº 6, que ya no existe.

## 6. Crear
9. **Aplicar** → marca **Guardar la imagen** y **Crear ISO** → nombre `Kairos-v0.2.iso`,
   etiqueta `KAIROS`, en `Downloads`.
10. Cuando termine, prepara el USB:

```powershell
.\setup\Preparar-USB.ps1 -Usb E:
```

## Qué NO hace falta meter en la ISO
- **Drivers**: van en `USB:\$WinPEDriver$` y Windows Setup los instala solo.
- **Español**: lo instala Kairos Setup (necesita internet). Región, teclado y hora ya salen
  en español desde la instalación gracias al archivo de Ventoy.
