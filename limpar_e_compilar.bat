@echo off
echo ========================================
echo   LIMPEZA COMPLETA E RECOMPILACAO
echo ========================================
echo.

echo [1/5] Deletando bin e obj...
if exist "bin" rd /s /q "bin"
if exist "obj" rd /s /q "obj"
echo OK - Pastas deletadas

echo.
echo [2/5] Limpando projeto...
C:\Dotnet\dotnet.exe clean
echo OK - Projeto limpo

echo.
echo [3/5] Restaurando pacotes...
C:\Dotnet\dotnet.exe restore
echo OK - Pacotes restaurados

echo.
echo [4/5] Recompilando do zero...
C:\Dotnet\dotnet.exe build --configuration Release --no-incremental
if errorlevel 1 (
    echo.
    echo ========================================
    echo   ERRO AO COMPILAR!
    echo ========================================
    echo.
    echo Verifique se:
    echo   1. Program.cs esta na pasta raiz do projeto
    echo   2. HubFinanceiro.csproj foi substituido
    echo.
    pause
    exit /b 1
)

echo.
echo [5/5] Testando execucao...
C:\Dotnet\dotnet.exe run --configuration Release --no-build

pause
