# ============================================
# SF Tecnologias - E2E do alvo de sistema (Fase 3/4)
# Executa o SF.Updater.exe REAL com --system <id> contra install/data simulados:
#   - instalacao individual de um sistema ausente
#   - nenhum re-download quando a versao local ja e a disponivel
#   - atualizacao individual (outro sistema nao e tocado)
#   - retencao de backup (maximo 3)
#   - hash invalido / manifesto sem o sistema => nada e substituido
#   - falha apos a troca => rollback para a versao anterior
#   - dados (SQLite) e isolamento entre sistemas preservados
#
# Nao requer API: o modo --system nao faz health check nem reinicia a plataforma.
# Requer: dist\updater\SF.Updater.exe (.\build-release.ps1 -SkipApi -SkipFrontend -SkipDesktop)
# ============================================

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot
$UpdaterExe = Join-Path $RootDir "dist\updater\SF.Updater.exe"
$TestDir = Join-Path $RootDir "test-system-target"
$InstallDir = Join-Path $TestDir "install"
$DataRoot = Join-Path $TestDir "programdata"
$DataDir = Join-Path $DataRoot "data"
$PackagesDir = Join-Path $TestDir "packages"
$BuildDir = Join-Path $TestDir "build"
$SfRoot = $DataRoot
$StateFile = Join-Path $SfRoot "config\updater-state.json"
$LogFile = Join-Path $SfRoot ("logs\updater-{0}.log" -f (Get-Date -Format "yyyy-MM-dd"))
$SystemBackupsDir = Join-Path $SfRoot "backups\systems"

$SystemId = "h2-conveniencia"
$OtherSystemId = "livraria-papelaria-lider"

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
    foreach ($dir in @("app\executable", "migrations", "configuration")) {
        $full = Join-Path $BaseDir $dir
        New-Item -Path $full -ItemType Directory -Force | Out-Null
        # Compress-Archive ignora pastas vazias: garante um arquivo para o zip
        # conter a estrutura exigida pelo System Contract.
        Set-Content -Path (Join-Path $full ".keep") -Value "estrutura do pacote" -Encoding UTF8
    }
}

function New-SystemPackage {
    param([string]$Id, [string]$Version, [string]$ZipPath)

    $staging = Join-Path $BuildDir "$Id-$Version"
    if (Test-Path $staging) { Remove-Item -Path $staging -Recurse -Force }
    New-SystemStructure -BaseDir $staging

    $entryPoint = "app/executable/$Id.exe"
    Write-File (Join-Path $staging ($entryPoint -replace '/', '\')) "$Id v$Version"

    [ordered]@{
        id              = $Id
        name            = "Sistema $Id"
        publisher       = "SF Tecnologias"
        systemVersion   = $Version
        platformVersion = "1.0.0"
        entryPoint      = $entryPoint
        dataDirectory   = "data/$Id"
    } | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $staging "system.json") -Encoding UTF8

    [ordered]@{ systemId = $Id; version = $Version; build = (Get-Date -Format "yyyyMMdd") } |
        ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $staging "version.json") -Encoding UTF8

    if (Test-Path $ZipPath) { Remove-Item -Path $ZipPath -Force }
    Compress-Archive -Path "$staging\*" -DestinationPath $ZipPath -Force
    return $ZipPath
}

function New-Manifest {
    param([object]$SystemEntry, [string]$Path, [string]$OmitSystem = "")

    $systems = @()
    if ($SystemEntry) { $systems += $SystemEntry }

    $manifest = [ordered]@{
        product               = "SF.Tecnologias"
        version               = "1.0.0"
        minimumUpdaterVersion = "1.0.0"
        components            = [ordered]@{
            systems = $systems
        }
        mandatory             = $false
        releaseNotes          = "E2E do alvo de sistema"
    }

    if ($OmitSystem) {
        $manifest.components.systems = @()
    }

    $manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $Path -Encoding UTF8
    return $Path
}

function New-SystemEntry {
    param([string]$Id, [string]$Version, [string]$ZipPath, [string]$BadHash = "")

    $entry = [ordered]@{
        id          = $Id
        name        = "Sistema $Id"
        version     = $Version
        packageUrl  = "file:///" + ($ZipPath -replace '\\', '/')
        sha256      = if ($BadHash) { $BadHash } else { (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant() }
        fileName    = (Split-Path $ZipPath -Leaf)
        sizeBytes   = (Get-Item $ZipPath).Length
        required    = $false
    }
    return $entry
}

function Invoke-TargetUpdater {
    param([string]$ManifestPath)

    $manifestUri = "file:///" + ($ManifestPath -replace '\\', '/')
    $logMark = (Get-Date).ToString("HH:mm:ss.fff")

    $proc = Start-Process -FilePath $UpdaterExe -ArgumentList @(
        "--manifest", "`"$manifestUri`"",
        "--system", $SystemId,
        "--install-dir", "`"$InstallDir`"",
        "--data-dir", "`"$DataDir`""
    ) -Wait -PassThru -NoNewWindow

    $logChunk = ""
    if (Test-Path $LogFile) {
        $all = Get-Content $LogFile -Raw
        $idx = $all.LastIndexOf("Alvo de sistema:")
        if ($idx -ge 0) { $logChunk = $all.Substring($idx) }
    }

    return @{ ExitCode = $proc.ExitCode; Log = $logChunk }
}

function Get-InstalledSystemVersion {
    param([string]$Id)
    $versionFile = Join-Path $InstallDir "Systems\$Id\version.json"
    if (-not (Test-Path $versionFile)) { return $null }
    return (Get-Content $versionFile -Raw | ConvertFrom-Json).version
}

function Get-InstalledSystemContent {
    param([string]$Id)
    $exe = Join-Path $InstallDir "Systems\$Id\app\executable\$Id.exe"
    if (-not (Test-Path $exe)) { return $null }
    return (Get-Content $exe -Raw).Trim()
}

function Get-UpdaterState {
    if (-not (Test-Path $StateFile)) { return $null }
    return Get-Content $StateFile -Raw | ConvertFrom-Json
}

function Get-SystemBackupCount {
    param([string]$Id)
    $root = Join-Path $SystemBackupsDir $Id
    if (-not (Test-Path $root)) { return 0 }
    return @(Get-ChildItem -Path $root -Directory).Count
}

function Get-WorkingDirCount {
    return @(Get-ChildItem (Join-Path $InstallDir "Systems") -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like ".*" -or $_.Name -like "*.old-*" -or $_.Name -like "*.staging-*" }).Count
}

# ============================================
# PRE-REQUISITOS
# ============================================
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " SF Tecnologias - E2E do alvo de sistema" -ForegroundColor Cyan
Write-Host " sistema=$SystemId (instalacao/atualizacao individual)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if (-not (Test-Path $UpdaterExe)) {
    Write-Host "SF.Updater.exe nao encontrado em dist\updater." -ForegroundColor Red
    Write-Host "  .\build-release.ps1 -SkipApi -SkipFrontend -SkipDesktop" -ForegroundColor Yellow
    exit 2
}

# ============================================
# AMBIENTE SIMULADO
# ============================================
if (Test-Path $TestDir) { Remove-Item -Path $TestDir -Recurse -Force }
New-Item -Path $InstallDir -ItemType Directory -Force | Out-Null
New-Item -Path $DataDir -ItemType Directory -Force | Out-Null
New-Item -Path $PackagesDir -ItemType Directory -Force | Out-Null
New-Item -Path $BuildDir -ItemType Directory -Force | Out-Null
if (Test-Path (Join-Path $SystemBackupsDir $SystemId)) {
    Remove-Item -Path (Join-Path $SystemBackupsDir $SystemId) -Recurse -Force
}

Write-File (Join-Path $InstallDir "version.json") "{`"version`": `"1.0.0`"}"

# Dados: SQLite do sistema alvo + dados de OUTRO sistema (isolamento)
Write-File (Join-Path $DataDir "$SystemId\database.sqlite") "SQLite database content (preservar)"
Write-File (Join-Path $DataDir "$SystemId\configuration\empresa.json") '{"nome":"Empresa H2"}'
Write-File (Join-Path $DataDir "$OtherSystemId\database.sqlite") "SQLite do outro sistema (nunca tocar)"

# ============================================
# 1) INSTALACAO INDIVIDUAL (sistema ausente)
# ============================================
Write-Step "1) Instalacao individual do sistema ausente"

$zip100 = New-SystemPackage -Id $SystemId -Version "1.0.0" -ZipPath (Join-Path $PackagesDir "$SystemId-1.0.0.zip")
$manifest = New-Manifest -SystemEntry (New-SystemEntry -Id $SystemId -Version "1.0.0" -ZipPath $zip100) -Path (Join-Path $PackagesDir "manifest-1.json")

Check "sistema ausente antes de instalar" (-not (Test-Path (Join-Path $InstallDir "Systems\$SystemId")))

$run = Invoke-TargetUpdater -ManifestPath $manifest
$state = Get-UpdaterState

Check "exit code 0" ($run.ExitCode -eq 0)
Check "estado final Completed" ($state.Status -eq "Completed")
Check "Systems\$SystemId\system.json existe" (Test-Path (Join-Path $InstallDir "Systems\$SystemId\system.json"))
Check "versao instalada = 1.0.0" ((Get-InstalledSystemVersion -Id $SystemId) -eq "1.0.0")
Check "entry point extraido" (Test-Path (Join-Path $InstallDir "Systems\$SystemId\app\executable\$SystemId.exe"))
Check "estrutura do contrato (migrations/)" (Test-Path (Join-Path $InstallDir "Systems\$SystemId\migrations"))
Check "sem pastas de trabalho remanescentes" ((Get-WorkingDirCount) -eq 0)
Check "banco SQLite do sistema preservado" ((Get-Content (Join-Path $DataDir "$SystemId\database.sqlite") -Raw).Trim() -eq "SQLite database content (preservar)")
Check "configuracao do sistema preservada" (Test-Path (Join-Path $DataDir "$SystemId\configuration\empresa.json"))
Check "dados de OUTRO sistema intactos" ((Get-Content (Join-Path $DataDir "$OtherSystemId\database.sqlite") -Raw).Trim() -eq "SQLite do outro sistema (nunca tocar)")
Check "sem backup em instalacao inicial" ((Get-SystemBackupCount -Id $SystemId) -eq 0)

# ============================================
# 2) SEM RE-DOWNLOAD QUANDO JA ESTA ATUALIZADO
# ============================================
Write-Step "2) Segunda execucao: ja instalado e atualizado (sem download)"

Remove-Item -Path $zip100 -Force   # se tentar baixar, falha
$run = Invoke-TargetUpdater -ManifestPath $manifest
$state = Get-UpdaterState

Check "exit code 0 sem o pacote disponivel (nao houve re-download)" ($run.ExitCode -eq 0)
Check "log registra que nao houve download" ($run.Log -match "nenhum download")
Check "estado Completed" ($state.Status -eq "Completed")
Check "reuso registrado (Reused=true)" ($state.Systems[0].Reused -eq $true)
Check "versao mantida em 1.0.0" ((Get-InstalledSystemVersion -Id $SystemId) -eq "1.0.0")

# ============================================
# 3) ATUALIZACAO INDIVIDUAL + RETENCAO DE BACKUP
# ============================================
Write-Step "3) Atualizacoes individuais 1.0.0 -> 1.4.0 (retencao de backup = 3)"

$versions = @("1.1.0", "1.2.0", "1.3.0", "1.4.0")
foreach ($v in $versions) {
    $zip = New-SystemPackage -Id $SystemId -Version $v -ZipPath (Join-Path $PackagesDir "$SystemId-$v.zip")
    $m = New-Manifest -SystemEntry (New-SystemEntry -Id $SystemId -Version $v -ZipPath $zip) -Path (Join-Path $PackagesDir "manifest-$v.json")
    $run = Invoke-TargetUpdater -ManifestPath $m

    Check "update ${v}: exit code 0" ($run.ExitCode -eq 0)
    Check "update ${v}: versao instalada" ((Get-InstalledSystemVersion -Id $SystemId) -eq $v)
    Check "update ${v}: conteudo do entry point atualizado" ((Get-InstalledSystemContent -Id $SystemId) -like "*v$v*")
    Check "update ${v}: sem pastas de trabalho" ((Get-WorkingDirCount) -eq 0)
    Check "update ${v}: banco SQLite preservado" ((Get-Content (Join-Path $DataDir "$SystemId\database.sqlite") -Raw).Trim() -eq "SQLite database content (preservar)")
    Check "update ${v}: dados do OUTRO sistema intactos" ((Get-Content (Join-Path $DataDir "$OtherSystemId\database.sqlite") -Raw).Trim() -eq "SQLite do outro sistema (nunca tocar)")
}

Check "apos 4 updates, no maximo 3 backups (retencao)" ((Get-SystemBackupCount -Id $SystemId) -le 3)
Check "apos 4 updates, ao menos 1 backup" ((Get-SystemBackupCount -Id $SystemId) -ge 1)

# ============================================
# 4) SHA-256 INVALIDO => nada e substituido
# ============================================
Write-Step "4) SHA-256 invalido no manifesto"

$zipBad = New-SystemPackage -Id $SystemId -Version "9.9.9" -ZipPath (Join-Path $PackagesDir "$SystemId-9.9.9.zip")
$entryBad = New-SystemEntry -Id $SystemId -Version "9.9.9" -ZipPath $zipBad -BadHash ("0" * 64)
$manifestBad = New-Manifest -SystemEntry $entryBad -Path (Join-Path $PackagesDir "manifest-hash-ruim.json")

$run = Invoke-TargetUpdater -ManifestPath $manifestBad
$state = Get-UpdaterState

Check "exit code != 0" ($run.ExitCode -ne 0)
Check "estado nao e Completed" ($state.Status -ne "Completed")
Check "versao anterior (1.4.0) permanece" ((Get-InstalledSystemVersion -Id $SystemId) -eq "1.4.0")
Check "banco preservado apos hash invalido" ((Get-Content (Join-Path $DataDir "$SystemId\database.sqlite") -Raw).Trim() -eq "SQLite database content (preservar)")
Check "sem pastas de trabalho apos falha" ((Get-WorkingDirCount) -eq 0)

# ============================================
# 5) MANIFESTO SEM O SISTEMA => nada e feito
# ============================================
Write-Step "5) Manifesto nao declara o sistema"

$manifestNoSystem = New-Manifest -SystemEntry "" -Path (Join-Path $PackagesDir "manifest-sem-sistema.json") -OmitSystem $SystemId
$run = Invoke-TargetUpdater -ManifestPath $manifestNoSystem

Check "exit code != 0 (sistema ausente do manifesto)" ($run.ExitCode -ne 0)
Check "versao anterior permanece" ((Get-InstalledSystemVersion -Id $SystemId) -eq "1.4.0")

# ============================================
# 6) FALHA APOS A TROCA => ROLLBACK PARA A VERSAO ANTERIOR
# ============================================
# Controle: o diretorio de dados do sistema e substituido por um ARQUIVO, o que
# faz a etapa pos-troca (garantia do diretorio de dados) falhar. O updater deve
# restaurar a versao anterior de software e nao tocar no banco.
Write-Step "6) Falha controlada apos a troca => rollback"

$realDataDir = Join-Path $DataDir $SystemId
$dataBackupForTest = Join-Path $TestDir "data-dir-saved"
Move-Item -Path $realDataDir -Destination $dataBackupForTest -Force
Write-File $realDataDir "isto nao e um diretorio"

$zipRb = New-SystemPackage -Id $SystemId -Version "2.0.0" -ZipPath (Join-Path $PackagesDir "$SystemId-2.0.0.zip")
$manifestRb = New-Manifest -SystemEntry (New-SystemEntry -Id $SystemId -Version "2.0.0" -ZipPath $zipRb) -Path (Join-Path $PackagesDir "manifest-rollback.json")

$run = Invoke-TargetUpdater -ManifestPath $manifestRb
$state = Get-UpdaterState

Check "exit code != 0 (falha apos a troca)" ($run.ExitCode -ne 0)
Check "estado nao e Completed" ($state.Status -ne "Completed")
Check "ROLLBACK: versao anterior (1.4.0) restaurada" ((Get-InstalledSystemVersion -Id $SystemId) -eq "1.4.0")
Check "ROLLBACK: conteudo da versao anterior restaurado" ((Get-InstalledSystemContent -Id $SystemId) -like "*v1.4.0*")
Check "ROLLBACK: sem pastas de trabalho" ((Get-WorkingDirCount) -eq 0)

# restaura o diretorio de dados real para o estado normal
Remove-Item -Path $realDataDir -Force
Move-Item -Path $dataBackupForTest -Destination $realDataDir -Force
Check "dados intactos apos rollback" ((Get-Content (Join-Path $DataDir "$SystemId\database.sqlite") -Raw).Trim() -eq "SQLite database content (preservar)")
Check "dados do OUTRO sistema intactos apos rollback" ((Get-Content (Join-Path $DataDir "$OtherSystemId\database.sqlite") -Raw).Trim() -eq "SQLite do outro sistema (nunca tocar)")

# ============================================
# 7) RECUPERACAO: instala a 2.0.0 depois da falha
# ============================================
Write-Step "7) Recuperacao: nova tentativa apos rollback"

$run = Invoke-TargetUpdater -ManifestPath $manifestRb
Check "exit code 0 apos normalizar o ambiente" ($run.ExitCode -eq 0)
Check "versao 2.0.0 instalada" ((Get-InstalledSystemVersion -Id $SystemId) -eq "2.0.0")

# ============================================
# RESUMO
# ============================================
Write-Host ""
Write-Host "========================================" -ForegroundColor $(if ($script:Failed -eq 0) { "Green" } else { "Red" })
Write-Host " Total: $script:Passed passaram, $script:Failed falharam" -ForegroundColor $(if ($script:Failed -eq 0) { "Green" } else { "Red" })
Write-Host "========================================" -ForegroundColor $(if ($script:Failed -eq 0) { "Green" } else { "Red" })
Write-Host "  InstallDir: $InstallDir"
Write-Host "  DataRoot:   $DataRoot"
Write-Host "  Backups:    $(Join-Path $SystemBackupsDir $SystemId)"
Write-Host "  Log:        $LogFile"

if (-not $Keep) {
    Write-Host "  (use -Keep para manter a arvore de teste)" -ForegroundColor Gray
    Remove-Item -Path (Join-Path $SystemBackupsDir $SystemId) -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:Failed -gt 0) { exit 1 }
exit 0
