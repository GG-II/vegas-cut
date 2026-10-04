@echo off
rem Compila Memes.cs con el compilador de C# que ya trae Windows y lo abre.
rem (Si ya tienes Memes.exe, puedes abrirlo directo.)
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo No encontre el compilador de C# de Windows ^(.NET Framework 4^).
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize /out:Memes.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Memes.cs
if errorlevel 1 (
  echo No se pudo compilar.
  pause
  exit /b 1
)
start "" Memes.exe
