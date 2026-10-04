# ============================================
# SF Tecnologias - Empacotador de Sistema (System Contract)
# Gera dist/systems/<id> (estrutura do contrato) + dist/packages/*.zip
# ============================================

param(
    [string]$SystemId = "h2-conveniencia",
    [string]$Name = "H2 Conveniencia",
    [string]$Version = "",
    [string]$PlatformVersion = "",
    # Reutiliza artefatos ja publicados (dist/api e frontend dist) em vez de rebuild
    [switch]$SkipApi,
    [switch]$SkipFrontend,
    [switch]$SkipPackage,
    # Sistema usa renderer customizado (copia frontend para o pacote). Padrao: usa renderer da plataforma
    [switch]$CustomRenderer
)

$ErrorActionPreference = "Stop"
$RootDir = Split-Path $PSScriptRoot -Parent
$StagingDir = Join-Path $RootDir "dist\systems\$SystemId"
$PackagesDir = Join-Path $RootDir "dist\packages"

function Show-Step { param([string]$Msg) Write-Host "[*] $Msg" -ForegroundColor Yellow }
function Show-Ok   { param([string]$Msg) Write-Host "  OK: $Msg" -ForegroundColor Green }
function Show-Info { param([string]$Msg) Write-Host "  $Msg" -ForegroundColor Gray }
function Show-Fail { param([string]$Msg) Write-Host "  ERRO: $Msg" -ForegroundColor Red; exit 1 }

# Escreve JSON UTF-8 SEM BOM (JSON.parse do Node rejeita BOM)
function Write-JsonNoBom {
    param([object]$Object, [string]$Path)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, ($Object | ConvertTo-Json -Depth 5), $utf8NoBom)
}

# Versao: parametro > src/desktop/version.json
if (-not $Version) {
    $desktopVersionFile = Join-Path $RootDir "src\desktop\version.json"
    if (-not (Test-Path $desktopVersionFile)) { Show-Fail "version.json do desktop nao encontrado" }
    $Version = (Get-Content $desktopVersionFile -Raw | ConvertFrom-Json).version
}
if (-not $PlatformVersion) { $PlatformVersion = $Version }

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " SF Tecnologias - Empacotador de Sistema" -ForegroundColor Cyan
Write-Host " Sistema: $SystemId v$Version (plataforma min. $PlatformVersion)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# ============================================
# 1. ESTRUTURA DO CONTRATO (limpa e recria)
# ============================================
Show-Step "Preparando estrutura do sistema em $StagingDir..."
if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
# app/executable NAO e mais obrigatorio (API compartilhada da plataforma)
# So cria app/resources/renderer para o frontend do sistema
New-Item -Path (Join-Path $StagingDir "app\resources\renderer") -ItemType Directory -Force | Out-Null
New-Item -Path (Join-Path $StagingDir "configuration\defaults") -ItemType Directory -Force | Out-Null
New-Item -Path (Join-Path $StagingDir "migrations") -ItemType Directory -Force | Out-Null
# Compress-Archive ignora pastas vazias: sem isto o zip perde migrations/ e o
# System Contract rejeita o pacote (required_directory_missing)
Set-Content -Path (Join-Path $StagingDir "migrations\.keep") -Value "placeholder" -Encoding ASCII
Show-Ok "Estrutura criada (system.json, version.json, app/resources/renderer/, configuration/, migrations/)"

# ============================================
# 2. API (compartilhada pela plataforma - nao copia para o sistema)
# ============================================
Show-Step "API: usando API compartilhada da plataforma (nao empacotada no sistema)"
Show-Ok "API omitida do pacote do sistema (plataforma fornece SFTecnologiasApi.exe em resources/api/)"

# ============================================
# 3. RENDERER (frontend do sistema) - OPCIONAL
# ============================================
# Padrao: usa renderer compartilhado da plataforma (resources/renderer/)
# Se -CustomRenderer: copia frontend para app/resources/renderer do sistema
if ($CustomRenderer) {
    $rendererDest = Join-Path $StagingDir "app\resources\renderer"
    $frontendDir = Join-Path $RootDir "src\frontend\sf-tecnologias-web"
    if ($SkipFrontend) {
        Show-Step "Renderer customizado: reutilizando build do frontend..."
        $frontendDist = Join-Path $frontendDir "dist"
        if (-not (Test-Path (Join-Path $frontendDist "index.html"))) { Show-Fail "frontend dist nao encontrado (rode sem -SkipFrontend)" }
        Copy-Item -Path "$frontendDist\*" -Destination $rendererDest -Recurse -Force
        Show-Ok "Renderer customizado copiado do frontend dist"
    } else {
        Show-Step "Renderer customizado: buildando frontend..."
        Push-Location $frontendDir
        try {
            npm run build 2>&1 | Out-Host
            if ($LASTEXITCODE -ne 0) { Show-Fail "build do frontend falhou" }
        } finally { Pop-Location }
        Copy-Item -Path (Join-Path $frontendDir "dist\*") -Destination $rendererDest -Recurse -Force
        Show-Ok "Renderer customizado buildado e copiado"
    }
} else {
    Show-Step "Renderer: usando renderer compartilhado da plataforma (nao empacotado no sistema)"
    Show-Ok "Renderer omitido do pacote do sistema (plataforma fornece em resources/renderer/)"
}

# ============================================
# 4. SYSTEM CONTRACT (system.json + version.json)
# ============================================
Show-Step "Escrevendo system.json e version.json..."

$systemJson = [ordered]@{
    id              = $SystemId
    name            = $Name
    publisher       = "SF Tecnologias"
    systemVersion   = $Version
    platformVersion = $PlatformVersion
    dataDirectory   = "data/$SystemId"
}
if ($CustomRenderer) {
    $systemJson.renderer = "app/resources/renderer/index.html"
}
Write-JsonNoBom $systemJson (Join-Path $StagingDir "system.json")

$versionJson = [ordered]@{
    systemId = $SystemId
    version  = $Version
    build    = (Get-Date -Format "yyyyMMdd")
}
Write-JsonNoBom $versionJson (Join-Path $StagingDir "version.json")

Show-Ok "system.json + version.json escritos"

# ============================================
# 5. PACOTE (zip + SHA-256 + manifest do sistema)
# ============================================
if ($SkipPackage) {
    Show-Ok "Empacotamento pulado (-SkipPackage); staging em $StagingDir"
    exit 0
}

Show-Step "Empacotando..."
New-Item -Path $PackagesDir -ItemType Directory -Force | Out-Null
$zipName = "sf-system-$SystemId-$Version.zip"
$zipPath = Join-Path $PackagesDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Push-Location $StagingDir
try {
    Compress-Archive -Path * -DestinationPath $zipPath -Force
} finally { Pop-Location }

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $zipPath).Length

$manifest = [ordered]@{
    product                 = $SystemId
    systemId                = $SystemId
    name                    = $Name
    version                 = $Version
    minimumPlatformVersion  = $PlatformVersion
    platformVersion         = $PlatformVersion
    packageUrl              = ""
    sha256                  = $hash
    fileName                = $zipName
    sizeBytes               = $size
    required                = $false
    releaseNotes            = ""
    generatedAt             = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
}
$manifestPath = Join-Path $PackagesDir "system-manifest-$SystemId-$Version.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

Show-Ok "Pacote: $zipPath ($([math]::Round($size / 1MB, 1)) MB)"
Show-Info "SHA-256: $hash"
Show-Ok "Manifesto: $manifestPath"
Write-Host ""
Write-Host "Sistema pronto. Estrutura em: $StagingDir" -ForegroundColor White
Write-Host "Para publicar: crie um release no repositorio do sistema e suba o zip + manifest." -ForegroundColor White
