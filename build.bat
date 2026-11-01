@echo off
rem Compile Split.exe avec le compilateur C# fourni avec Windows (.NET Framework 4) : rien a installer.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Compilateur C# introuvable ^(.NET Framework 4^).
    exit /b 1
)
if not exist src\split.ico powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
rem Ferme proprement l'instance en cours pour liberer Split.exe
if exist "%~dp0Split.exe" (
    "%~dp0Split.exe" --quit
    ping -n 2 127.0.0.1 >nul
)
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /warn:4 ^
    /win32manifest:src\app.manifest /win32icon:src\split.ico /out:Split.exe ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll src\*.cs
if errorlevel 1 (
    echo Echec de la compilation. Si Split est lance, quittez-le d'abord.
    exit /b 1
)
echo Split.exe compile.
