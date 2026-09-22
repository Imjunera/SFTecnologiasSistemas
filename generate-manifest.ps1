# ============================================
# SF Tecnologias - Gerador de Manifesto
# Gera o manifesto de atualizacao para releases
# ============================================

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [string]$PackagesDir = "dist\packages",
    [string]$OutputDir = "dist\release-assets",
    [string]$GitHubOwner = "",
    [string]$GitHubRepo = "",
    [string]$MinimumUpdaterVersion = "1.0.0",
    [switch]$Mandatory,
    [string]$ReleaseNotes = ""
)

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " SF Tecnologias - Gerador de Manifesto" -ForegroundColor Cyan
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

function Get-FileSha256 {
    param([string]$FilePath)
    $hash = (Get-FileHash -Path $FilePath -Algorithm SHA256).Hash
    return $hash.ToLowerInvariant()
}

# ============================================
# VALIDACOES
# ============================================
$totalSteps = 5
$step = 0

Show-Step (++$step) $totalSteps "Verificando pacotes..."

$packagesPath = Join-Path $RootDir $PackagesDir
if (-not (Test-Path $packagesPath)) {
    Show-Fail "Diretorio de pacotes nao encontrado: $packagesPath"
    Show-Info "Execute primeiro: .\build-release.ps1 -Version $Version"
}

$desktopPackage = Get-ChildItem -Path $packagesPath -Filter "sf-tecnologias-desktop-$Version.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
$apiPackage = Get-ChildItem -Path $packagesPath -Filter "sf-tecnologias-api-$Version.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
$updaterPackage = Get-ChildItem -Path $packagesPath -Filter "sf-tecnologias-updater-$Version.zip" -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $desktopPackage -and -not $apiPackage) {
    Show-Fail "Nenhum pacote encontrado para versao $Version"
}

if ($desktopPackage) { Show-Ok "Pacote Desktop: $($desktopPackage.Name)" }
if ($apiPackage) { Show-Ok "Pacote API: $($apiPackage.Name)" }
if ($updaterPackage) { Show-Ok "Pacote Updater: $($updaterPackage.Name)" }

# ============================================
# CALCULAR SHA-256
# ============================================
Show-Step (++$step) $totalSteps "Calculando SHA-256..."

$desktopHash = if ($desktopPackage) { Get-FileSha256 -FilePath $desktopPackage.FullName } else { $null }
$apiHash = if ($apiPackage) { Get-FileSha256 -FilePath $apiPackage.FullName } else { $null }
$updaterHash = if ($updaterPackage) { Get-FileSha256 -FilePath $updaterPackage.FullName } else { $null }

if ($desktopHash) { Show-Info "Desktop SHA-256: $desktopHash" }
if ($apiHash) { Show-Info "API SHA-256: $apiHash" }
if ($updaterHash) { Show-Info "Updater SHA-256: $updaterHash" }

# ============================================
# CONSTRUIR URLs
# ============================================
Show-Step (++$step) $totalSteps "Construindo URLs de download..."

if ($GitHubOwner -and $GitHubRepo) {
    $baseUrl = "https://github.com/$GitHubOwner/$GitHubRepo/releases/download/v$Version"
    $desktopUrl = if ($desktopPackage) { "$baseUrl/$($desktopPackage.Name)" } else { $null }
    $apiUrl = if ($apiPackage) { "$baseUrl/$($apiPackage.Name)" } else { $null }
    $updaterUrl = if ($updaterPackage) { "$baseUrl/$($updaterPackage.Name)" } else { $null }
    
    Show-Info "Base URL: $baseUrl"
} else {
    Show-Info "GitHub owner/repo nao fornecido. URLs serao relativas."
    $desktopUrl = if ($desktopPackage) { $desktopPackage.Name } else { $null }
    $apiUrl = if ($apiPackage) { $apiPackage.Name } else { $null }
    $updaterUrl = if ($updaterPackage) { $updaterPackage.Name } else { $null }
}

# ============================================
# GERAR MANIFESTO
# ============================================
Show-Step (++$step) $totalSteps "Gerando manifesto..."

$manifest = @{
    product = "SF.Tecnologias"
    version = $Version
    minimumUpdaterVersion = $MinimumUpdaterVersion
    components = @{}
    mandatory = $Mandatory.IsPresent
    releaseNotes = $ReleaseNotes
    generatedAt = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
}

if ($desktopPackage) {
    $manifest.components.desktop = @{
        version = $Version
        packageUrl = $desktopUrl
        sha256 = $desktopHash
        fileName = $desktopPackage.Name
        sizeBytes = $desktopPackage.Length
    }
}

if ($apiPackage) {
    $manifest.components.api = @{
        version = $Version
        packageUrl = $apiUrl
        sha256 = $apiHash
        fileName = $apiPackage.Name
        sizeBytes = $apiPackage.Length
    }
}

if ($updaterPackage) {
    $manifest.components.updater = @{
        version = $Version
        packageUrl = $updaterUrl
        sha256 = $updaterHash
        fileName = $updaterPackage.Name
        sizeBytes = $updaterPackage.Length
    }
}

# ============================================
# SALVAR MANIFESTO
# ============================================
Show-Step (++$step) $totalSteps "Salvando manifesto..."

$outputPath = Join-Path $RootDir $OutputDir
New-Item -Path $outputPath -ItemType Directory -Force | Out-Null

$manifestFile = Join-Path $outputPath "manifest-$Version.json"
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestFile -Encoding UTF8
Show-Ok "Manifesto salvo: $manifestFile"

# Also save as latest
$latestFile = Join-Path $outputPath "manifest-latest.json"
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $latestFile -Encoding UTF8
Show-Ok "Manifesto latest: $latestFile"

# Copy packages to release assets
if ($desktopPackage) {
    Copy-Item -Path $desktopPackage.FullName -Destination $outputPath -Force
    Show-Ok "Pacote Desktop copiado para $outputPath"
}
if ($apiPackage) {
    Copy-Item -Path $apiPackage.FullName -Destination $outputPath -Force
    Show-Ok "Pacote API copiado para $outputPath"
}
if ($updaterPackage) {
    Copy-Item -Path $updaterPackage.FullName -Destination $outputPath -Force
    Show-Ok "Pacote Updater copiado para $outputPath"
}

# ============================================
# GERAR SHA256SUMS
# ============================================
Show-Step (++$step) $totalSteps "Gerando SHA256SUMS..."

$hashFile = Join-Path $outputPath "SHA256SUMS.txt"
$hashContent = @()

if ($desktopPackage) {
    $hashContent += "$desktopHash  $($desktopPackage.Name)"
}
if ($apiPackage) {
    $hashContent += "$apiHash  $($apiPackage.Name)"
}
if ($updaterPackage) {
    $hashContent += "$updaterHash  $($updaterPackage.Name)"
}

$hashContent | Set-Content -Path $hashFile -Encoding UTF8
Show-Ok "SHA256SUMS salvo: $hashFile"

# ============================================
# RESUMO
# ============================================
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Manifesto gerado com sucesso!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Arquivos gerados:" -ForegroundColor Cyan
Get-ChildItem -Path $outputPath -File | ForEach-Object {
    $sizeKB = [math]::Round($_.Length / 1KB, 1)
    Write-Host "  $($_.Name) ($sizeKB KB)" -ForegroundColor White
}
Write-Host ""
Write-Host "Para publicar no GitHub Releases:" -ForegroundColor Yellow
Write-Host "  1. Crie uma release com tag v$Version" -ForegroundColor White
Write-Host "  2. Upload todos os arquivos de $outputPath" -ForegroundColor White
Write-Host "  3. O manifesto deve ser acessivel via HTTPS" -ForegroundColor White
