@echo off
setlocal enabledelayedexpansion

:: Verificar privilegios de administrador
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Solicitando elevacao de privilegios...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"

:MENU
cls
echo ============================================
echo   SF Tecnologias - Instalador
echo ============================================
echo.
echo   1. Instalar SF Tecnologias
echo   2. Desinstalar SF Tecnologias
echo   3. Ver Status
echo   4. Registrar API como Servico
echo   5. Remover Servico
echo   6. Sair
echo.
echo ============================================
set /p choice="Selecione uma opcao [1-6]: "

if "%choice%"=="1" goto INSTALL
if "%choice%"=="2" goto UNINSTALL
if "%choice%"=="3" goto STATUS
if "%choice%"=="4" goto SERVICE_INSTALL
if "%choice%"=="5" goto SERVICE_REMOVE
if "%choice%"=="6" goto END
goto MENU

:INSTALL
cls
echo ============================================
echo   INSTALAR SF TECNOLOGIAS
echo ============================================
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0Setup-SF.ps1" install
echo.
pause
goto MENU

:UNINSTALL
cls
echo ============================================
echo   DESINSTALAR SF TECNOLOGIAS
echo ============================================
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0Setup-SF.ps1" uninstall
echo.
pause
goto MENU

:STATUS
cls
echo ============================================
echo   STATUS DO SISTEMA
echo ============================================
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0Setup-SF.ps1" status
echo.
pause
goto MENU

:SERVICE_INSTALL
cls
echo ============================================
echo   REGISTRAR API COMO SERVICO
echo ============================================
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0Setup-SF.ps1" service-install
echo.
pause
goto MENU

:SERVICE_REMOVE
cls
echo ============================================
echo   REMOVER SERVICO
echo ============================================
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0Setup-SF.ps1" service-uninstall
echo.
pause
goto MENU

:END
cls
echo Saindo...
exit /b
