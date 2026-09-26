using System;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace KairosFiles
{
    // Operaciones de archivo. Eliminar va a la PAPELERA (no borrado permanente),
    // que es lo seguro y lo esperable. Devuelven (ok, mensajeError).
    public static class FileOps
    {
        public static (bool ok, string error) Rename(string fullPath, string newName)
        {
            try
            {
                var dir = Path.GetDirectoryName(fullPath);
                if (dir == null) return (false, "Ruta invalida.");
                var dest = Path.Combine(dir, newName);
                if (Directory.Exists(fullPath))
                    Directory.Move(fullPath, dest);
                else
                    File.Move(fullPath, dest);
                return (true, "");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public static (bool ok, string error) Delete(string fullPath)
        {
            try
            {
                if (Directory.Exists(fullPath))
                    FileSystem.DeleteDirectory(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                else
                    FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return (true, "");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public static (bool ok, string error) CopyTo(string sourcePath, string destDir)
        {
            try
            {
                var name = Path.GetFileName(sourcePath);
                var dest = Path.Combine(destDir, name);
                if (Directory.Exists(sourcePath))
                    FileSystem.CopyDirectory(sourcePath, dest, UIOption.OnlyErrorDialogs);
                else
                    FileSystem.CopyFile(sourcePath, dest, UIOption.OnlyErrorDialogs);
                return (true, "");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public static (bool ok, string error) MoveTo(string sourcePath, string destDir)
        {
            try
            {
                var name = Path.GetFileName(sourcePath);
                var dest = Path.Combine(destDir, name);
                if (Directory.Exists(sourcePath))
                    FileSystem.MoveDirectory(sourcePath, dest, UIOption.OnlyErrorDialogs);
                else
                    FileSystem.MoveFile(sourcePath, dest, UIOption.OnlyErrorDialogs);
                return (true, "");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }
    }
}
