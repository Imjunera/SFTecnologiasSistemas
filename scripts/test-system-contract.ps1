param(
    [string]$PlatformVersion = "1.0.0",
    [string]$SystemId = "h2-conveniencia",
    [string]$SystemVersion = "1.0.0",
    [switch]$Keep
)

# ============================================
# SF Tecnologias - E2E do System Contract
# Executa o SF.Updater.exe REAL contra um install/data simulados e valida:
#   - instalacao inicial de sistema (Systems\<id>)
#   - nenhum re-download de sistema ja atualizado
#   - atualizacao independente (dados preservados)
#   - rejeicao de pacote invalido / hash invalido / id divergente / incompativel
#   - isolamento entre dados de sistemas
#
# Requer: dist\updater\SF.Updater.exe e a API respondendo em http://localhost:5000/health
# (o health check do updater reprova a atualizacao inteira quando a API nao responde).
# ============================================

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot
$UpdaterExe = Join-Path $RootDir "dist\updater\SF.Updater.exe"
$TestDir = Join-Path $RootDir "test-systems"
$InstallDir = Join-Path $TestDir "install"
$DataRoot = Join-Path $TestDir "programdata"
$DataDir = Join-Path $DataRoot "data"
$PackagesDir = Join-Path $TestDir "packages"
$SfRoot = $DataRoot
$StateFile = Join-Path $SfRoot "config\updater-state.json"
$LogFile = Join-Path $SfRoot ("logs\updater-{0}.log" -f (Get-Date -Format "yyyy-MM-dd"))

$script:Passed = 0
$script:Failed = 0

function Check {
    param([string]$Name, [bool]$Condition)
    if ($Condition) {
        Write-Host "  PASS: $Name" -ForegroundColor Green
        $script:Passed++
    } else {
        Write-Host "  FAIL: $Name" -ForegroundColor Red
        $script:Failed++
    }
}

function Write-Step { param([string]$Message) Write-Host ""; Write-Host "=== $Message" -ForegroundColor Cyan }

function Write-File {
    param([string]$Path, [string]$Content)
    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) { New-Item -Path $dir -ItemType Directory -Force | Out-Null }
    Set-Content -Path $Path -Value $Content -Encoding UTF8
}

function New-SystemStructure {
    param([string]$BaseDir)
    foreach ($dir in @(
            "app\executable", "app\libraries", "migrations",
            "modules\core", "modules\features", "assets\images", "configuration\defaults"
        )) {
        $full = Join-Path $BaseDir $dir
        New-Item -Path $full -ItemType Directory -Force | Out-Null
        # Compress-Archive ignora pastas vazias: .keep preserva a estrutura no zip
        Set-Content -Path (Join-Path $full ".keep") -Value "placeholder" -Encoding ASCII
    }
}

function New-SystemPackage {
    param(
        [string]$Id,
        [string]$Version,
        [string]$StagingDir,
        [string]$ZipPath,
        [string]$PlatformRequirement = "",
        [string]$EntryPoint = "",
        [switch]$OmitManifest,
        [switch]$OmitEntryPointFile
    )

    if (Test-Path $StagingDir) { Remove-Item -Path $StagingDir -Recurse -Force }
    New-SystemStructure -BaseDir $StagingDir

    if (-not $EntryPoint) { $EntryPoint = "app/executable/$Id.exe" }

    if (-not $OmitEntryPointFile) {
        Write-File (Join-Path $StagingDir ($EntryPoint -replace '/', '\')) "$Id v$Version"
    }

    if (-not $OmitManifest) {
        $manifest = [ordered]@{
            id              = $Id
            name            = "Sistema $Id"
            publisher       = "SF Tecnologias"
            systemVersion   = $Version
            entryPoint      = $EntryPoint
            dataDirectory   = "data/$Id"
            description     = "Pacote de teste do sistema $Id"
        }
        if ($PlatformRequirement) { $manifest.platformVersion = $PlatformRequirement }
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $StagingDir "system.json") -Encoding UTF8
    }

    [ordered]@{
        systemId = $Id
        version  = $Version
        build    = (Get-Date -Format "yyyy.MM.dd")
    } | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $StagingDir "version.json") -Encoding UTF8

    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path "$StagingDir\*" -DestinationPath $ZipPath -Force
    return $ZipPath
}

function New-Manifest {
    param(
        [string]$Version,
        [string]$DesktopZip,
        [hashtable]$SystemEntry,
        [string]$Path
    )

    $desktopHash = (Get-FileHash -Path $DesktopZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $components = [ordered]@{
        desktop = [ordered]@{
            version    = $Version
            packageUrl = "file:///" + ($DesktopZip -replace '\\', '/')
            sha256     = $desktopHash
        }
    }
    if ($SystemEntry) {
        $components.systems = @($SystemEntry)
    }

    [ordered]@{
        product               = "SF.Tecnologias"
        version               = $Version
        minimumUpdaterVersion = "1.0.0"
        components            = $components
        mandatory             = $false
        releaseNotes          = "E2E do System Contract"
    } | ConvertTo-Json -Depth 10 | Set-Content -Path $Path -Encoding UTF8

    return $Path
}

function New-SystemEntry {
    param(
        [string]$Id,
        [string]$Version,
        [string]$ZipPath,
        [string]$MinimumPlatformVersion = "",
        [bool]$Required = $true
    )

    $entry = [ordered]@{
        id        = $Id
        name      = "Sistema $Id"
        version   = $Version
        packageUrl = "file:///" + ($ZipPath -replace '\\', '/')
        sha256    = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        fileName  = (Split-Path $ZipPath -Leaf)
        sizeBytes = (Get-Item $ZipPath).Length
        required  = $Required
    }
    if ($MinimumPlatformVersion) { $entry.minimumPlatformVersion = $MinimumPlatformVersion }
    return $entry
}

function Invoke-Updater {
    param([string]$ManifestPath)

    $manifestUri = "file:///" + ($ManifestPath -replace '\\', '/')
    $logMark = (Get-Date).ToString("HH:mm:ss")

    $proc = Start-Process -FilePath $UpdaterExe -ArgumentList @(
        "--manifest", "`"$manifestUri`"",
        "--install-dir", "`"$InstallDir`"",
        "--data-dir", "`"$DataDir`""
    ) -Wait -PassThru -NoNewWindow

    return @{ ExitCode = $proc.ExitCode; Mark = $logMark }
}

function Get-InstalledSystemVersion {
    param([string]$Id)
    $versionFile = Join-Path $InstallDir "Systems\$Id\version.json"
    if (-not (Test-Path $versionFile)) { return $null }
    return (Get-Content $versionFile -Raw | ConvertFrom-Json).version
}

function Get-UpdaterState {
    if (-not (Test-Path $StateFile)) { return $null }
    return Get-Content $StateFile -Raw | ConvertFrom-Json
}

# ============================================
# PRE-REQUISITOS
# ============================================
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " SF Tecnologias - E2E do System Contract" -ForegroundColor Cyan
Write-Host " system=$SystemId versao=$SystemVersion plataforma=$PlatformVersion" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if (-not (Test-Path $UpdaterExe)) {
    Write-Host "SF.Updater.exe nao encontrado em dist\updater." -ForegroundColor Red
    Write-Host "  .\build-release.ps1 -SkipApi -SkipFrontend -SkipDesktop" -ForegroundColor Yellow
    exit 2
}

try {
    $health = Invoke-WebRequest -Uri "http://localhost:5000/health" -UseBasicParsing -TimeoutSec 5
    Check "API respondendo em /health (necessario para o health check do updater)" ($health.StatusCode -eq 200)
} catch {
    Write-Host "AVISO: API nao respondeu em http://localhost:5000/health." -ForegroundColor Yellow
    Write-Host "O updater reprova a atualizacao (rollback) sem API saudavel." -ForegroundColor Yellow
    Check "API respondendo em /health (necessario para o health check do updater)" $false
    Write-Host "Abortando: suba a API (dist\api\SFTecnologiasApi.exe) antes do E2E." -ForegroundColor Red
    exit 2
}

# ============================================
# AMBIENTE SIMULADO (InstallDir x DataRoot)
# ============================================
if (Test-Path $TestDir) { Remove-Item -Path $TestDir -Recurse -Force }
New-Item -Path $InstallDir -ItemType Directory -Force | Out-Null
New-Item -Path $DataDir -ItemType Directory -Force | Out-Null
New-Item -Path $PackagesDir -ItemType Directory -Force | Out-Null

# Plataforma: executavel real e inofensivo (o updater reinicia o "desktop" ao final)
Copy-Item -Path (Join-Path $env:SystemRoot "System32\whoami.exe") -Destination (Join-Path $InstallDir "SF Tecnologias BETA.exe") -Force
Copy-Item -Path $UpdaterExe -Destination (Join-Path $InstallDir "SF.Updater.exe") -Force
Write-File (Join-Path $InstallDir "version.json") "{`"version`": `"$PlatformVersion`", `"buildDate`": `"2026-10-02`"}"

# Dados do sistema: SQLite simulado + dados de OUTRO sistema (isolamento)
Write-File (Join-Path $DataDir "$SystemId\database.sqlite") "SQLite database content (preservar)"
Write-File (Join-Path $DataDir "$SystemId\configuration\empresa.json") '{"nome":"Empresa H2"}'
Write-File (Join-Path $DataDir "outro-sistema\database.sqlite") "SQLite do outro sistema (nunca tocar)"

# Pacote desktop (mantem o updater satisfeito: pelo menos um pacote por release).
# O "desktop" e um executavel real e inofensivo: o updater reinicia o desktop ao final
# e nao queremos um dialogo do Windows nem um app abrindo durante o teste.
$desktopStaging = Join-Path $TestDir "build\desktop"
New-Item -Path $desktopStaging -ItemType Directory -Force | Out-Null
Copy-Item -Path (Join-Path $env:SystemRoot "System32\whoami.exe") -Destination (Join-Path $desktopStaging "SF Tecnologias BETA.exe") -Force
Write-File (Join-Path $desktopStaging "version.json") "{`"version`": `"$PlatformVersion`"}"
$desktopZip = Join-Path $PackagesDir "sf-tecnologias-desktop-$PlatformVersion.zip"
Compress-Archive -Path "$desktopStaging\*" -DestinationPath $desktopZip -Force

# ============================================
# 1) INSTALACAO INICIAL DO SISTEMA
# ============================================
Write-Step "1) Instalacao inicial do sistema (release $SystemVersion)"

$zip100 = New-SystemPackage -Id $SystemId -Version $SystemVersion -StagingDir (Join-Path $TestDir "build\$SystemId-$SystemVersion") -ZipPath (Join-Path $PackagesDir "$SystemId-$SystemVersion.zip")
$entry = New-SystemEntry -Id $SystemId -Version $SystemVersion -ZipPath $zip100
$manifest = New-Manifest -Version $PlatformVersion -DesktopZip $desktopZip -SystemEntry $entry -Path (Join-Path $PackagesDir "manifest-1.json")

$run = Invoke-Updater -ManifestPath $manifest
$state = Get-UpdaterState

Check "exit code 0" ($run.ExitCode -eq 0)
Check "estado final Completed" ($state.Status -eq "Completed")
Check "Systems\$SystemId\system.json existe" (Test-Path (Join-Path $InstallDir "Systems\$SystemId\system.json"))
Check "version.json do sistema = $SystemVersion" ((Get-InstalledSystemVersion -Id $SystemId) -eq $SystemVersion)
Check "entry point extraido" (Test-Path (Join-Path $InstallDir "Systems\$SystemId\app\executable\$SystemId.exe"))
Check "estrutura do contrato (migrations/)" (Test-Path (Join-Path $InstallDir "Systems\$SystemId\migrations"))
Check "sem pastas de trabalho (.staging/.old) remanescentes" (@(Get-ChildItem (Join-Path $InstallDir "Systems") -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like ".*" -or $_.Name -like "*.old-*" }).Count -eq 0)
Check "banco SQLite do sistema preservado" ((Get-Content (Join-Path $DataDir "$SystemId\database.sqlite") -Raw).Trim() -eq "SQLite database content (preservar)")
Check "configuracao do sistema preservada" (Test-Path (Join-Path $DataDir "$SystemId\configuration\empresa.json"))
Check "dados de OUTRO sistema intactos (isolamento)" ((Get-Content (Join-Path $DataDir "outro-sistema\database.sqlite") -Raw).Trim() -eq "SQLite do outro sistema (nunca tocar)")
Check "sistema registrado no updater-state.json" ($state.Systems.Count -ge 1 -and $state.Systems[0].Id -eq $SystemId -and $state.Systems[0].Version -eq $SystemVersion)

# ============================================
# 2) NENHUM RE-DOWNLOAD QUANDO JA ESTA ATUALIZADO
# ============================================
Write-Step "2) Segunda execucao: sistema ja instalado e atualizado"

# O pacote do sistema e REMOVIDO: se o updater tentar baixar de novo, falha.
Remove-Item -Path $zip100 -Force
$logBefore = if (Test-Path $LogFile) { (Get-Content $LogFile -Raw).Length } else { 0 }

$run = Invoke-Updater -ManifestPath $manifest
$state = Get-UpdaterState
$logChunk = if (Test-Path $LogFile) { (Get-Content $LogFile -Raw).Substring([Math]::Min($logBefore, (Get-Content $LogFile -Raw).Length)) } else { "" }

Check "exit code 0 sem o pacote disponivel (nao houve re-download)" ($run.ExitCode -eq 0)
Check "log registra que nao houve download" ($logChunk -match "nenhum download")
Check "estado Completed" ($state.Status -eq "Completed")
Check "reuso registrado no estado (Reused=true)" ($state.Systems[0].Reused -eq $true)
Check "versao instalada mantida" ((Get-InstalledSystemVersion -Id $SystemId) -eq $SystemVersion)

# ============================================
# 3) ATUALIZACAO INDEPENDENTE DO SISTEMA (dados preservados)
# ============================================
Write-Step "3) Atualizacao do sistema $SystemVersion -> 1.1.0"

$zip110 = New-SystemPackage -Id $SystemId -Version "1.1.0" -StagingDir (Join-Path $TestDir "build\$SystemId-1.1.0") -ZipPath (Join-Path $PackagesDir "$SystemId-1.1.0.zip")
$entry110 = New-SystemEntry -Id $SystemId -Version "1.1.0" -ZipPath $zip110
$manifest110 = New-Manifest -Version $PlatformVersion -DesktopZip $desktopZip -SystemEntry $entry110 -Path (Join-Path $PackagesDir "manifest-110.json")

$run = Invoke-Updater -ManifestPath $manifest110
$state = Get-UpdaterState

Check "exit code 0" ($run.ExitCode -eq 0)
Check "versao instalada = 1.1.0" ((Get-InstalledSystemVersion -Id $SystemId) -eq "1.1.0")
Check "arquivos do sistema atualizados" ((Get-Content (Join-Path $InstallDir "Systems\$SystemId\app\executable\$SystemId.exe") -Raw) -like "*1.1.0*")
Check "banco SQLite preservado apos atualizacao" ((Get-Content (Join-Path $DataDir "$SystemId\database.sqlite") -Raw).Trim() -eq "SQLite database content (preservar)")
Check "estado registra a nova versao" ($state.Systems[0].Version -eq "1.1.0")
Check "sem versao antiga (.old-*) apos sucesso" (@(Get-ChildItem (Join-Path $InstallDir "Systems") -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*.old-*" }).Count -eq 0)

# ============================================
# 4) PACOTE INVALIDO (viola o contrato) => nao instala
# ============================================
Write-Step "4) Pacote sem system.json (viola o contrato)"

$badStaging = Join-Path $TestDir "build\$SystemId-quebrado"
$badZip = New-SystemPackage -Id $SystemId -Version "2.0.0" -StagingDir $badStaging -ZipPath (Join-Path $PackagesDir "$SystemId-2.0.0-quebrado.zip") -OmitManifest
$badEntry = New-SystemEntry -Id $SystemId -Version "2.0.0" -ZipPath $badZip
$badManifest = New-Manifest -Version $PlatformVersion -DesktopZip $desktopZip -SystemEntry $badEntry -Path (Join-Path $PackagesDir "manifest-quebrado.json")

$run = Invoke-Updater -ManifestPath $badManifest
$state = Get-UpdaterState

Check "exit code != 0 (sistema obrigatorio falhou)" ($run.ExitCode -ne 0)
Check "atualizacao nao foi considerada concluida" ($state.Status -ne "Completed")
Check "versao anterior (1.1.0) permanece instalada" ((Get-InstalledSystemVersion -Id $SystemId) -eq "1.1.0")
Check "dados preservados apos falha" ((Get-Content (Join-Path $DataDir "$SystemId\database.sqlite") -Raw).Trim() -eq "SQLite database content (preservar)")

# ============================================
# 5) SHA-256 INVALIDO
# ============================================
Write-Step "5) SHA-256 invalido no manifesto"

$zip111 = New-SystemPackage -Id $SystemId -Version "1.1.1" -StagingDir (Join-Path $TestDir "build\$SystemId-1.1.1") -ZipPath (Join-Path $PackagesDir "$SystemId-1.1.1.zip")
$entry111 = New-SystemEntry -Id $SystemId -Version "1.1.1" -ZipPath $zip111
$entry111.sha256 = ("0" * 64)
$manifest111 = New-Manifest -Version $PlatformVersion -DesktopZip $desktopZip -SystemEntry $entry111 -Path (Join-Path $PackagesDir "manifest-hash-ruim.json")

$run = Invoke-Updater -ManifestPath $manifest111
$state = Get-UpdaterState

Check "exit code != 0" ($run.ExitCode -ne 0)
Check "versao anterior permanece" ((Get-InstalledSystemVersion -Id $SystemId) -eq "1.1.0")
Check "nada foi registrado como instalado em 1.1.1" ($state.Systems[0].Version -eq "1.1.0")

# ============================================
# 6) PACOTE DE OUTRO SISTEMA (id divergente)
# ============================================
Write-Step "6) Pacote declara outro id"

$wrongZip = New-SystemPackage -Id "livraria" -Version "1.0.0" -StagingDir (Join-Path $TestDir "build\livraria-1.0.0") -ZipPath (Join-Path $PackagesDir "livraria-1.0.0.zip")
$wrongEntry = New-SystemEntry -Id $SystemId -Version "1.2.0" -ZipPath $wrongZip
$wrongManifest = New-Manifest -Version $PlatformVersion -DesktopZip $desktopZip -SystemEntry $wrongEntry -Path (Join-Path $PackagesDir "manifest-id-errado.json")

$run = Invoke-Updater -ManifestPath $wrongManifest

Check "exit code != 0 (id divergente rejeitado)" ($run.ExitCode -ne 0)
Check "versao anterior permanece" ((Get-InstalledSystemVersion -Id $SystemId) -eq "1.1.0")
Check "sistema 'livraria' NAO foi instalado" (-not (Test-Path (Join-Path $InstallDir "Systems\livraria")))

# ============================================
# 7) SISTEMA EXIGE PLATAFORMA MAIS NOVA
# ============================================
Write-Step "7) Sistema exige plataforma >= 9.0.0"

$futureZip = New-SystemPackage -Id $SystemId -Version "3.0.0" -StagingDir (Join-Path $TestDir "build\$SystemId-3.0.0") -ZipPath (Join-Path $PackagesDir "$SystemId-3.0.0.zip") -PlatformRequirement "9.0.0"
$futureEntry = New-SystemEntry -Id $SystemId -Version "3.0.0" -ZipPath $futureZip
$futureManifest = New-Manifest -Version $PlatformVersion -DesktopZip $desktopZip -SystemEntry $futureEntry -Path (Join-Path $PackagesDir "manifest-incompativel.json")

$run = Invoke-Updater -ManifestPath $futureManifest

Check "exit code != 0 (incompativel com a plataforma)" ($run.ExitCode -ne 0)
Check "versao anterior permanece" ((Get-InstalledSystemVersion -Id $SystemId) -eq "1.1.0")

# ============================================
# RESUMO
# ============================================
Write-Host ""
Write-Host "========================================" -ForegroundColor $(if ($script:Failed -eq 0) { "Green" } else { "Red" })
Write-Host " Total: $script:Passed passaram, $script:Failed falharam" -ForegroundColor $(if ($script:Failed -eq 0) { "Green" } else { "Red" })
Write-Host "========================================" -ForegroundColor $(if ($script:Failed -eq 0) { "Green" } else { "Red" })
Write-Host "  InstallDir: $InstallDir"
Write-Host "  DataRoot:   $DataRoot"
Write-Host "  Pacotes:    $PackagesDir"
Write-Host "  Log:        $LogFile"

if (-not $Keep) {
    Write-Host "  (use -Keep para manter a arvore de teste)" -ForegroundColor Gray
}

if ($script:Failed -gt 0) { exit 1 }
exit 0
