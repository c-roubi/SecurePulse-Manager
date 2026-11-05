@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

where csc >nul 2>nul
if errorlevel 1 set "PATH=%PATH%;%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"

echo [*] S4M1A build v2.0

if not exist bin mkdir bin
csc /nologo /t:exe /out:bin\Builder.exe Builder.cs
if errorlevel 1 goto :err

bin\Builder.exe %*
if errorlevel 1 goto :err

if not exist dist mkdir dist

set SRCS=
for /r "..\Implant" %%F in (*.cs) do set "SRCS=!SRCS! "%%F""

csc /nologo /t:winexe /out:dist\implant.exe /optimize+ /platform:anycpu ^
    /r:System.Web.Extensions.dll /r:System.Runtime.Serialization.dll !SRCS!
if errorlevel 1 goto :err

echo [*] dist\implant.exe ready
goto :end

:err
echo [!] build failed
:end
endlocal