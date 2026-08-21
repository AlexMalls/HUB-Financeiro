@echo off
echo ========================================
echo   PUBLICACAO - HUB FINANCEIRO
echo ========================================
echo.

REM Limpa builds anteriores
echo [1/2] Limpando builds antigos...
C:\Dotnet\dotnet.exe clean --configuration Release
if errorlevel 1 (
    echo ERRO ao limpar projeto!
    pause
    exit /b 1
)

REM Publicacao
echo.
echo [2/2] Publicando aplicacao...
C:\Dotnet\dotnet.exe publish ^
    --configuration Release ^
    --output "C:\Apps\HubFinanceiro\app" ^
    --self-contained false ^
    /p:PublishReadyToRun=false ^
    /p:PublishSingleFile=false
if errorlevel 1 (
    echo ERRO ao publicar!
    pause
    exit /b 1
)

echo.
echo ========================================
echo   PUBLICACAO CONCLUIDA!
echo ========================================
echo.
echo Pasta: C:\Apps\HubFinanceiro\app
echo.
pause