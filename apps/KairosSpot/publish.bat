@echo off
REM ============================================================
REM  KaiSpot - Publicacion self-contained single-file (win-x64)
REM  Ejecutar en el PC real (no en la VM), con .NET 8 SDK.
REM  Mismo patron que el dock KairosDock.
REM ============================================================

echo.
echo === Compilando KaiSpot (Release, win-x64, self-contained) ===
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
echo   bin\Release\net8.0-windows\win-x64\publish\KairosSpot.exe
echo.
echo Copia ese KairosSpot.exe a la VM y ejecutalo.
echo Pulsa Alt+Space para abrir KaiSpot.
echo.
pause
