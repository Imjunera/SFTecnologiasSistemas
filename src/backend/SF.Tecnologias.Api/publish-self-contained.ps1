# ============================================
# SF Tecnologias - Script de Publicação
# Publica a API como self-contained (sem exigir .NET Runtime)
# ============================================

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "..\..\dist\api"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " SF Tecnologias - Publicacao API" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Navigate to API project
Set-Location $scriptDir

Write-Host "[1/4] Limpando build anterior..." -ForegroundColor Yellow
dotnet clean --configuration $Configuration --nologo -v q

Write-Host "[2/4] Restaurando pacotes..." -ForegroundColor Yellow
dotnet restore --nologo -v q

Write-Host "[3/4] Publicando API (self-contained, win-x64)..." -ForegroundColor Yellow
$outputPath = Join-Path $scriptDir $OutputDir
dotnet publish --configuration $Configuration `
    --self-contained true `
    --runtime win-x64 `
    --output $outputPath `
    --nologo -v q

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERRO: Falha na publicacao!" -ForegroundColor Red
    exit 1
}

Write-Host "[4/4] Limpando artefatos desnecessarios..." -ForegroundColor Yellow

# Remove PDB files (debug symbols not needed in release)
Get-ChildItem -Path $outputPath -Filter '*.pdb' -ErrorAction SilentlyContinue | Remove-Item -Force
Write-Host "  PDB files removidos" -ForegroundColor Green

# Remove SQLite DB files (will be created at runtime)
Get-ChildItem -Path $outputPath -Filter '*.db' -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -Path $outputPath -Filter '*.db-*' -ErrorAction SilentlyContinue | Remove-Item -Force
Write-Host "  SQLite DB files removidos" -ForegroundColor Green

# Remove development config (not needed in production)
$devSettings = Join-Path $outputPath "appsettings.Development.json"
if (Test-Path $devSettings) { Remove-Item $devSettings -Force }
Write-Host "  Configs de desenvolvimento removidas" -ForegroundColor Green

# Report results
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Publicacao concluida!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

$publishedFiles = Get-ChildItem -Path $outputPath -File
$totalSize = ($publishedFiles | Measure-Object -Property Length -Sum).Sum
$totalSizeMB = [math]::Round($totalSize / 1MB, 2)

Write-Host "Saida: $outputPath" -ForegroundColor Cyan
Write-Host "Tamanho total: $totalSizeMB MB" -ForegroundColor Cyan
Write-Host ""
Write-Host "Para executar:" -ForegroundColor Yellow
Write-Host "  .\SFTecnologiasApi.exe" -ForegroundColor White
Write-Host ""
