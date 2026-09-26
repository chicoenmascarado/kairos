@echo off
REM ============================================================
REM  KairosMenu - Publicacion self-contained single-file (win-x64)
REM  Ejecutar en el PC real, con .NET 8 SDK.
REM ============================================================

echo.
echo === Compilando KairosMenu (Release, win-x64, self-contained) ===
echo.

dotnet publish -c Release -r win-x64 --self-contained true ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true

if %ERRORLEVEL% NEQ 0 (
  echo.
  echo *** ERROR en la compilacion. Revisa el mensaje de arriba. ***
  pause
  exit /b 1
)

echo.
echo === LISTO ===
echo El .exe esta en:
echo   bin\Release\net8.0-windows\win-x64\publish\KairosMenu.exe
echo.
echo Copialo a la VM. Se abre con Ctrl+Esc.
echo.
pause
