@echo off
rem Compila ClasificarMemes.cs con el compilador de C# que ya trae Windows y lo abre.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo No encontre el compilador de C# de Windows ^(.NET Framework 4^).
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize /out:ClasificarMemes.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll ClasificarMemes.cs
if errorlevel 1 (
  echo No se pudo compilar.
  pause
  exit /b 1
)
start "" ClasificarMemes.exe
