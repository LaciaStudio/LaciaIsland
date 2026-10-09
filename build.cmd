@echo off
rem Builds a self-contained single-file LaciaIsland.exe (Windows x64) into .\publish
setlocal
cd /d "%~dp0"
dotnet publish src\LaciaIsland\LaciaIsland.csproj -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)
echo.
echo Done: %~dp0publish\LaciaIsland.exe
pause
