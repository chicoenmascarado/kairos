using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KairosFiles
{
    // Logica de navegacion: lista el contenido de una carpeta, mantiene el
    // historial para atras/adelante y filtra por busqueda.
    public class FileBrowser
    {
        private readonly List<string> _history = new();
        private int _historyIndex = -1;

        public string CurrentPath { get; private set; } = "";

        public bool CanGoBack => _historyIndex > 0;
        public bool CanGoForward => _historyIndex < _history.Count - 1;

        // Navega a una ruta nueva (la agrega al historial).
        public List<FileItem> Navigate(string path)
        {
            if (string.IsNullOrEmpty(path)) return new List<FileItem>();

            // Truncar el "futuro" si veniamos de un atras y navegamos a algo nuevo.
            if (_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

            _history.Add(path);
            _historyIndex = _history.Count - 1;
            CurrentPath = path;
            return List(path);
        }

        public List<FileItem> GoBack()
        {
            if (!CanGoBack) return List(CurrentPath);
            _historyIndex--;
            CurrentPath = _history[_historyIndex];
            return List(CurrentPath);
        }

        public List<FileItem> GoForward()
        {
            if (!CanGoForward) return List(CurrentPath);
            _historyIndex++;
            CurrentPath = _history[_historyIndex];
            return List(CurrentPath);
        }

        public List<FileItem> GoUp()
        {
            try
            {
                var parent = Directory.GetParent(CurrentPath);
                if (parent != null) return Navigate(parent.FullName);
            }
            catch { }
            return List(CurrentPath);
        }

        // Refresca la carpeta actual (tras una operacion de archivo).
        public List<FileItem> Refresh() => List(CurrentPath);

        // Lista el contenido de una carpeta: carpetas primero, luego archivos.
        private List<FileItem> List(string path)
        {
            var items = new List<FileItem>();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return items;

            try
            {
                var di = new DirectoryInfo(path);

                foreach (var d in di.GetDirectories())
                {
                    if ((d.Attributes & FileAttributes.Hidden) != 0) continue;
                    if ((d.Attributes & FileAttributes.System) != 0) continue;
                    items.Add(new FileItem
                    {
                        Name = d.Name,
                        FullPath = d.FullName,
                        IsDirectory = true,
                        Modified = d.LastWriteTime
                    });
                }

                foreach (var f in di.GetFiles())
                {
                    if ((f.Attributes & FileAttributes.Hidden) != 0) continue;
                    if ((f.Attributes & FileAttributes.System) != 0) continue;
                    items.Add(new FileItem
                    {
                        Name = f.Name,
                        FullPath = f.FullName,
                        IsDirectory = false,
                        Size = f.Length,
                        Modified = f.LastWriteTime
                    });
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception) { }

            return items;
        }

        // Filtra la lista por texto (en memoria, sobre lo ya listado).
        public List<FileItem> Filter(List<FileItem> source, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return source;
            var q = query.Trim().ToLowerInvariant();
            return source.Where(i => i.Name.ToLowerInvariant().Contains(q)).ToList();
        }
    }
}
