# ============================================
# SF Tecnologias - Build de Distribuicao
# Gera todos os artefatos para release
# ============================================

param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [switch]$SkipApi,
    [switch]$SkipFrontend,
    [switch]$SkipDesktop,
    [switch]$SkipUpdater
)

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot
$DistDir = Join-Path $RootDir "dist"
$ApiOutputDir = Join-Path $DistDir "api"
$FrontendOutputDir = Join-Path $RootDir "src\frontend\sf-tecnologias-web\dist"
$DesktopOutputDir = Join-Path $DistDir "release"
$UpdaterOutputDir = Join-Path $DistDir "updater"
$PackagesDir = Join-Path $DistDir "packages"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " SF Tecnologias - Build de Distribuicao" -ForegroundColor Cyan
Write-Host " Versao: $Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ============================================
# FUNCOES AUXILIARES
# ============================================
function Show-Step {
    param([int]$Current, [int]$Total, [string]$Message)
    Write-Host "[$Current/$Total] $Message" -ForegroundColor Yellow
}

function Show-Ok {
    param([string]$Message)
    Write-Host "  OK: $Message" -ForegroundColor Green
}

function Show-Fail {
    param([string]$Message)
    Write-Host "  ERRO: $Message" -ForegroundColor Red
    exit 1
}

function Show-Info {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Gray
}

# ============================================
# LIMPEZA
# ============================================
Write-Host "Limpando diretorios de build..." -ForegroundColor Yellow

if (Test-Path $ApiOutputDir) { Remove-Item -Path $ApiOutputDir -Recurse -Force }
if (Test-Path $FrontendOutputDir) { Remove-Item -Path $FrontendOutputDir -Recurse -Force }
if (Test-Path $DesktopOutputDir) { Remove-Item -Path $DesktopOutputDir -Recurse -Force }
if (Test-Path $UpdaterOutputDir) { Remove-Item -Path $UpdaterOutputDir -Recurse -Force }
if (Test-Path $PackagesDir) { Remove-Item -Path $PackagesDir -Recurse -Force }

New-Item -Path $PackagesDir -ItemType Directory -Force | Out-Null

Write-Host "  Diretorios limpos" -ForegroundColor Green
Write-Host ""

# ============================================
# PASSO 1: PUBLICAR API (SELF-CONTAINED)
# ============================================
if (-not $SkipApi) {
    $totalSteps = if (-not $SkipFrontend) { 4 } else { 3 }
    if (-not $SkipDesktop) { $totalSteps++ }
    if (-not $SkipUpdater) { $totalSteps++ }
    
    Show-Step 1 $totalSteps "Publicando API (self-contained, win-x64)..."
    
    $apiProject = Join-Path $RootDir "src\backend\SF.Tecnologias.Api\SF.Tecnologias.Api.csproj"
    
    # Clean
    & dotnet clean $apiProject --configuration $Configuration --nologo -v q 2>&1 | Out-Null
    
    # Restore
    & dotnet restore $apiProject --nologo -v q 2>&1 | Out-Null
    
    # Publish
    & dotnet publish $apiProject `
        --configuration $Configuration `
        --self-contained true `
        --runtime win-x64 `
        --output $ApiOutputDir `
        --nologo -v q
    
    if ($LASTEXITCODE -ne 0) { Show-Fail "Falha na publicacao da API" }
    
    # Limpar artefatos desnecessarios
    Get-ChildItem -Path $ApiOutputDir -Filter '*.pdb' -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem -Path $ApiOutputDir -Filter '*.db' -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem -Path $ApiOutputDir -Filter '*.db-*' -ErrorAction SilentlyContinue | Remove-Item -Force
    $devSettings = Join-Path $ApiOutputDir "appsettings.Development.json"
    if (Test-Path $devSettings) { Remove-Item $devSettings -Force }
    
    Show-Ok "API publicada em $ApiOutputDir"
    
    # Verificar tamanho
    $apiSize = (Get-ChildItem -Path $ApiOutputDir -File | Measure-Object -Property Length -Sum).Sum
    $apiSizeMB = [math]::Round($apiSize / 1MB, 2)
    Show-Info "Tamanho da API: $apiSizeMB MB"
}

# ============================================
# PASSO 2: BUILD DO FRONTEND
# ============================================
if (-not $SkipFrontend) {
    $step = if (-not $SkipApi) { 2 } else { 1 }
    $totalSteps = if (-not $SkipApi) { 4 } else { 3 }
    if (-not $SkipDesktop) { $totalSteps++ }
    if (-not $SkipUpdater) { $totalSteps++ }
    
    Show-Step $step $totalSteps "Buildando frontend..."
    
    $frontendDir = Join-Path $RootDir "src\frontend\sf-tecnologias-web"
    
    Push-Location $frontendDir
    try {
        & npm run build 2>&1
        if ($LASTEXITCODE -ne 0) { Show-Fail "Falha no build do frontend" }
    } finally {
        Pop-Location
    }
    
    if (-not (Test-Path $FrontendOutputDir)) {
        Show-Fail "Diretorio de saida do frontend nao encontrado: $FrontendOutputDir"
    }
    
    Show-Ok "Frontend buildado"
}

# ============================================
# PASSO 3: BUILD DO DESKTOP (ELECTRON-BUILDER)
# ============================================
if (-not $SkipDesktop) {
    $step = if (-not $SkipApi) { 3 } else { 2 }
    $step = if (-not $SkipFrontend) { $step } else { $step }
    $totalSteps = if (-not $SkipApi) { 4 } else { 3 }
    if (-not $SkipFrontend) { $totalSteps++ }
    if (-not $SkipUpdater) { $totalSteps++ }
    
    Show-Step $step $totalSteps "Buildando Desktop (Electron)..."
    
    $desktopDir = Join-Path $RootDir "src\desktop"
    
    Push-Location $desktopDir
    try {
        # Verificar se node_modules existe
        if (-not (Test-Path "node_modules")) {
            Write-Host "  Instalando dependencias..." -ForegroundColor Gray
            & npm install 2>&1
        }
        
        # Build com electron-builder
        Write-Host "  Executando electron-builder..." -ForegroundColor Gray
        & npx electron-builder --win portable --x64 2>&1
        if ($LASTEXITCODE -ne 0) { Show-Fail "Falha no build do Desktop" }
    } finally {
        Pop-Location
    }
    
    if (-not (Test-Path $DesktopOutputDir)) {
        Show-Fail "Diretorio de saida do Desktop nao encontrado: $DesktopOutputDir"
    }
    
    Show-Ok "Desktop buildado"
}

# ============================================
# PASSO 4: BUILD DO UPDATER
# ============================================
if (-not $SkipUpdater) {
    $step = if (-not $SkipApi) { 4 } else { 3 }
    $step = if (-not $SkipFrontend) { $step } else { $step }
    $step = if (-not $SkipDesktop) { $step } else { $step }
    $totalSteps = if (-not $SkipApi) { 4 } else { 3 }
    if (-not $SkipFrontend) { $totalSteps++ }
    if (-not $SkipDesktop) { $totalSteps++ }
    
    Show-Step $step $totalSteps "Buildando Updater..."
    
    $updaterProject = Join-Path $RootDir "src\updater\SF.Updater.csproj"
    
    if (Test-Path $updaterProject) {
        & dotnet publish $updaterProject `
            --configuration $Configuration `
            --self-contained true `
            --runtime win-x64 `
            --output $UpdaterOutputDir `
            --nologo -v q
        
        if ($LASTEXITCODE -ne 0) { Show-Fail "Falha na publicacao do Updater" }
        
        # Limpar PDBs
        Get-ChildItem -Path $UpdaterOutputDir -Filter '*.pdb' -ErrorAction SilentlyContinue | Remove-Item -Force
        
        Show-Ok "Updater publicado"
    } else {
        Show-Info "Projeto do Updater nao encontrado. Pule este passo ou crie o projeto primeiro."
    }
}

# ============================================
# GERAR PACOTES PARA RELEASE
# ============================================
Write-Host ""
Write-Host "Gerando pacotes para release..." -ForegroundColor Yellow

# Pacote Desktop
$desktopPackage = Join-Path $PackagesDir "sf-tecnologias-desktop-$Version.zip"
if (Test-Path $DesktopOutputDir) {
    Compress-Archive -Path "$DesktopOutputDir\*" -DestinationPath $desktopPackage -Force
    Show-Ok "Pacote Desktop: $desktopPackage"
}

# Pacote API
$apiPackage = Join-Path $PackagesDir "sf-tecnologias-api-$Version.zip"
if (Test-Path $ApiOutputDir) {
    Compress-Archive -Path "$ApiOutputDir\*" -DestinationPath $apiPackage -Force
    Show-Ok "Pacote API: $apiPackage"
}

# Pacote Updater
$updaterPackage = Join-Path $PackagesDir "sf-tecnologias-updater-$Version.zip"
if (Test-Path $UpdaterOutputDir) {
    Compress-Archive -Path "$UpdaterOutputDir\*" -DestinationPath $updaterPackage -Force
    Show-Ok "Pacote Updater: $updaterPackage"
}

# ============================================
# CALCULAR SHA-256
# ============================================
Write-Host ""
Write-Host "Calculando SHA-256..." -ForegroundColor Yellow

$hashes = @{}

$packages = Get-ChildItem -Path $PackagesDir -Filter "*.zip"
foreach ($pkg in $packages) {
    $hash = (Get-FileHash -Path $pkg.FullName -Algorithm SHA256).Hash
    $hashes[$pkg.Name] = $hash
    Show-Info "$($pkg.Name): $hash"
}

# Salvar hashes
$hashFile = Join-Path $PackagesDir "SHA256SUMS.txt"
$hashes.GetEnumerator() | ForEach-Object { "$($_.Value)  $($_.Key)" } | Set-Content -Path $hashFile
Show-Ok "Hashes salvos em $hashFile"

# ============================================
# RESUMO
# ============================================
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Build de distribuicao concluido!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Artefatos gerados:" -ForegroundColor Cyan
Write-Host "  API:     $ApiOutputDir" -ForegroundColor White
Write-Host "  Desktop: $DesktopOutputDir" -ForegroundColor White
Write-Host "  Updater: $UpdaterOutputDir" -ForegroundColor White
Write-Host "  Pacotes: $PackagesDir" -ForegroundColor White
Write-Host ""
Write-Host "Proximo passo:" -ForegroundColor Yellow
Write-Host "  .\generate-manifest.ps1 -Version $Version" -ForegroundColor Cyan
