using System;
using System.Collections.Generic;

namespace KairosSpot
{
    // Atajos a ajustes del sistema. Usa los esquemas ms-settings: (panel nuevo)
    // y comandos clasicos. Cada entrada lista palabras clave que la disparan.
    public static class SettingsCatalog
    {
        public class Entry
        {
            public string Title = "";
            public string Target = "";
            public string[] Keywords = Array.Empty<string>();
            public string Glyph = "\uE713";
        }

        public static readonly List<Entry> Items = new()
        {
            new Entry { Title = "Wi-Fi y red", Target = "ms-settings:network-wifi", Keywords = new[]{"wifi","red","internet","network","wlan"}, Glyph="\uE701" },
            new Entry { Title = "Bluetooth", Target = "ms-settings:bluetooth", Keywords = new[]{"bluetooth","bt"}, Glyph="\uE702" },
            new Entry { Title = "Pantalla", Target = "ms-settings:display", Keywords = new[]{"pantalla","display","monitor","resolucion","brillo"}, Glyph="\uE7F4" },
            new Entry { Title = "Sonido", Target = "ms-settings:sound", Keywords = new[]{"sonido","audio","volumen","sound"}, Glyph="\uE767" },
            new Entry { Title = "Bateria", Target = "ms-settings:batterysaver", Keywords = new[]{"bateria","battery","energia"}, Glyph="\uE83F" },
            new Entry { Title = "Aplicaciones instaladas", Target = "ms-settings:appsfeatures", Keywords = new[]{"apps","aplicaciones","programas","desinstalar"}, Glyph="\uE71D" },
            new Entry { Title = "Personalizacion", Target = "ms-settings:personalization", Keywords = new[]{"personalizacion","tema","fondo","wallpaper","colores"}, Glyph="\uE771" },
            new Entry { Title = "Windows Update", Target = "ms-settings:windowsupdate", Keywords = new[]{"update","actualizar","actualizaciones"}, Glyph="\uE777" },
            new Entry { Title = "Configuracion (inicio)", Target = "ms-settings:", Keywords = new[]{"ajustes","configuracion","settings","opciones"}, Glyph="\uE713" },
            new Entry { Title = "Administrador de tareas", Target = "taskmgr.exe", Keywords = new[]{"tareas","taskmgr","procesos","task manager"}, Glyph="\uE7C4" },
            new Entry { Title = "Panel de control", Target = "control.exe", Keywords = new[]{"panel","control","control panel"}, Glyph="\uE713" },
        };
    }
}
