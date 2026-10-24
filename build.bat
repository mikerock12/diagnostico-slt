@echo off
rem Compila o Diagnostico & Reparo usando o csc.exe do .NET Framework 4.x,
rem presente em qualquer Windows 10/11 (nao precisa de Visual Studio).
rem /codepage:65001 -> os fontes sao UTF-8 (acentos corretos no executavel).

setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist bin mkdir bin

"%CSC%" /nologo /target:winexe /codepage:65001 /out:bin\DiagnosticoReparo.exe ^
  /win32manifest:app.manifest ^
  /win32icon:icon.ico ^
  /resource:assets\logo.png,logo.png ^
  /r:System.Management.dll ^
  /r:System.ServiceProcess.dll ^
  /r:System.Core.dll ^
  /recurse:src\*.cs

if %errorlevel% neq 0 (
  echo.
  echo *** FALHA NA COMPILACAO ***
  exit /b 1
)
echo.
echo OK: bin\DiagnosticoReparo.exe gerado.

rem ------------------------------------------------------------------
rem Assinatura digital (Authenticode) - OPCIONAL.
rem So roda se o certificado estiver configurado nesta variavel; caso
rem contrario o build termina normalmente, apenas sem assinar.
rem
rem   set DIAG_CERT_SUBJECT=Smells Like Tech Informatica
rem ------------------------------------------------------------------
if "%DIAG_CERT_SUBJECT%"=="" goto :fim

set SIGNTOOL=
for %%p in (signtool.exe) do if not "%%~$PATH:p"=="" set SIGNTOOL=%%~$PATH:p
if "%SIGNTOOL%"=="" (
  for /f "delims=" %%f in ('dir /b /s "%ProgramFiles(x86)%\Windows Kits\10\bin\*\x64\signtool.exe" 2^>nul') do set SIGNTOOL=%%f
)
if "%SIGNTOOL%"=="" (
  echo AVISO: signtool.exe nao encontrado - executavel NAO assinado.
  goto :fim
)

echo Assinando com o certificado "%DIAG_CERT_SUBJECT%"...
"%SIGNTOOL%" sign /n "%DIAG_CERT_SUBJECT%" /fd SHA256 ^
  /tr http://timestamp.digicert.com /td SHA256 ^
  /d "Diagnostico e Reparo" ^
  /du "https://www.smellsliketech.com.br" ^
  bin\DiagnosticoReparo.exe
if %errorlevel% neq 0 (
  echo AVISO: falha ao assinar - executavel gerado, porem NAO assinado.
) else (
  echo OK: executavel assinado digitalmente.
)

:fim
endlocal
