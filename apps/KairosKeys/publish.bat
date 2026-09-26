@echo off
REM ============================================================
REM  KairosKeys - Publicacion self-contained single-file (win-x64)
REM ============================================================

echo.
echo === Compilando KairosKeys (Release, win-x64, self-contained) ===
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
echo   bin\Release\net8.0-windows\win-x64\publish\KairosKeys.exe
echo.
echo Copialo a la VM y ejecutalo (queda residente, sin ventana).
echo.
pause
