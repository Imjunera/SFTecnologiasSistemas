# ============================================
# SF Tecnologias - E2E DE FALHAS DE INSTALACAO/ATUALIZACAO (§28)
# Executa o SF.Updater.exe REAL (modo --system) contra install/data simulados:
#   [1] falta de espaço      -> guarda sizeBytes recusa ANTES do download
#   [2] download interrompido -> arquivo truncado x sha256 do pacote completo
#   [3] pacote corrompido    -> bytes invalidos COM sha256 valido (extracao falha)
#   [4] processo ja aberto   -> updater mata o entry point em execucao e conclui
#   [5] interrupcao na instalacao -> restos .staging/.old + estado corrompido
#       sao limpos e a proxima execucao conclui normalmente
# Em TODOS os cenarios de falha: versao anterior permanece instalada e
# nenhuma pasta de trabalho (.staging-*/.old-*) fica para tras.
# Nao requer API. Requer: dist\updater\SF.Updater.exe
#   (.\build-release.ps1 -SkipApi -SkipFrontend -SkipDesktop)
# ============================================

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot
$UpdaterExe = Join-Path $RootDir "dist\updater\SF.Updater.exe"
$TestDir = Join-Path $RootDir "test-install-failures"
$InstallDir = Join-Path $TestDir "install"
$DataRoot = Join-Path $TestDir "programdata"
$DataDir = Join-Path $DataRoot "data"
$PackagesDir = Join-Path $TestDir "packages"
$ManifestDir = Join-Path $TestDir "manifests"
$BuildDir = Join-Path $TestDir "build"
$StateFile = Join-Path $DataRoot "config\updater-state.json"
$TempDir = Join-Path $DataRoot "updates\temp"
$LogFile = Join-Path $DataRoot ("logs\updater-{0}.log" -f (Get-Date -Format "yyyy-MM-dd"))
$SystemsDir = Join-Path $InstallDir "Systems"

$SystemId = "h2-conveniencia"
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

function Write-Step { param([string]$Message) Write-Host "" Write-Host "=== $Message" -ForegroundColor Cyan }

function Write-File {
    param([string]$Path, [string]$Content)
    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) { New-Item -Path $dir -ItemType Directory -Force | Out-Null }
    Set-Content -Path $Path -Value $Content -Encoding UTF8
}

function New-SystemPackage {
    param([string]$Id, [string]$Version, [string]$ZipPath)

    $staging = Join-Path $BuildDir "$Id-$Version"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    foreach ($dir in @("app\executable", "migrations")) {
        $full = Join-Path $staging $dir
        New-Item -Path $full -ItemType Directory -Force | Out-Null
        Set-Content -Path (Join-Path $full ".keep") -Value "estrutura" -Encoding UTF8
    }

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

    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path "$staging\*" -DestinationPath $ZipPath -Force
    return $ZipPath
}

function New-SystemEntry {
    param(
        [string]$Id,
        [string]$Version,
        [string]$ZipPath,
        [string]$ShaOverride = "",
        [string]$UrlOverride = "",
        [long]$SizeOverride = -1
    )
    $size = if ($SizeOverride -ge 0) { $SizeOverride } else { (Get-Item $ZipPath).Length }
    $entry = [ordered]@{
        id        = $Id
        name      = "Sistema $Id"
        version   = $Version
        packageUrl = if ($UrlOverride) { $UrlOverride } else { "file:///" + ($ZipPath -replace '\\', '/') }
        sha256    = if ($ShaOverride) { $ShaOverride } else { (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant() }
        fileName  = (Split-Path $ZipPath -Leaf)
        sizeBytes = $size
        required  = $false
    }
    return $entry
}

function New-Manifest {
    param([object]$SystemEntry, [string]$Path)
    [ordered]@{
        product               = "SF.Tecnologias"
        version               = "1.0.0"
        minimumUpdaterVersion = "1.0.0"
        components            = [ordered]@{ systems = @($SystemEntry) }
        mandatory             = $false
        releaseNotes          = "E2E de falhas de instalacao (28)"
    } | ConvertTo-Json -Depth 10 | Set-Content -Path $Path -Encoding UTF8
    return $Path
}

function Invoke-SystemUpdater {
    param([string]$ManifestPath)
    $manifestUri = "file:///" + ($ManifestPath -replace '\', '/')
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

function Get-InstalledVersion {
    $f = Join-Path $SystemsDir "$SystemId\version.json"
    if (-not (Test-Path $f)) { return $null }
    return (Get-Content $f -Raw | ConvertFrom-Json).version
}

function Get-State {
    if (-not (Test-Path $StateFile)) { return $null }
    return Get-Content $StateFile -Raw | ConvertFrom-Json
}

function Test-WorkLeftovers {
    # pastas de trabalho (.staging-*) e versoes antigas (.old-*) nao podem sobrar
    $leftovers = @(Get-ChildItem $SystemsDir -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name.StartsWith(".") -or $_.Name.Contains(".old-") })
    return ($leftovers.Count -eq 0)
}

# ============================================
# PREPARACAO
# ============================================
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " SF Tecnologias - E2E de Falhas (28)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if (-not (Test-Path $UpdaterExe)) {
    Write-Host "SF.Updater.exe nao encontrado em dist\updater." -ForegroundColor Red
    Write-Host "  .\build-release.ps1 -SkipApi -SkipFrontend -SkipDesktop" -ForegroundColor Yellow
    exit 2
}

if (Test-Path $TestDir) { Remove-Item $TestDir -Recurse -Force }
New-Item -Path $InstallDir, $DataDir, $PackagesDir, $ManifestDir, $BuildDir, $SystemsDir -ItemType Directory -Force | Out-Null
Write-File (Join-Path $InstallDir "version.json") '{"version":"1.0.0","buildDate":"2026-10-03"}'

# Instalacao baseline saudavel: v1.0.0
$zip100 = New-SystemPackage -Id $SystemId -Version "1.0.0" -ZipPath (Join-Path $PackagesDir "$SystemId-1.0.0.zip")
$m100 = New-Manifest -SystemEntry (New-SystemEntry -Id $SystemId -Version "1.0.0" -ZipPath $zip100) -Path (Join-Path $ManifestDir "m-100.json")
$run = Invoke-SystemUpdater -ManifestPath $m100
Check "baseline instalado (1.0.0, exit=0)" (($run.ExitCode -eq 0) -and ((Get-InstalledVersion) -eq "1.0.0"))

# Dados operacionais do sistema: qualquer cenario de falha deve preservar.
$DbFile = Join-Path $DataDir "$SystemId\database.sqlite"
Write-File $DbFile "conteudo do banco (preservar em qualquer falha)"

# ============================================
# [1] FALTA DE ESPACO (sizeBytes = 1 TB) -> recusa ANTES do download
# ============================================
Write-Step "[1] Falha: manifesto declara 1 TB de pacote"
$zip200 = New-SystemPackage -Id $SystemId -Version "2.0.0" -ZipPath (Join-Path $PackagesDir "$SystemId-2.0.0.zip")
$entry200 = New-SystemEntry -Id $SystemId -Version "2.0.0" -ZipPath $zip200 -SizeOverride 1099511627776
$m200 = New-Manifest -SystemEntry $entry200 -Path (Join-Path $ManifestDir "m-200.json")
$run = Invoke-SystemUpdater -ManifestPath $m200
$state = Get-State
Check "exit != 0 (guarda de espaco)" ($run.ExitCode -ne 0)
Check "estado Failed / Insufficient disk space" (($state.Status -eq "Failed") -and ($state.Message -like "*disk space*"))
Check "NADA foi baixado (guarda antes do download)" (-not (Test-Path (Join-Path $TempDir "$SystemId-2.0.0.zip")))
Check "versao 1.0.0 permanece" ((Get-InstalledVersion) -eq "1.0.0")
Check "sem restos de trabalho" (Test-WorkLeftovers)

# ============================================
# [2] DOWNLOAD INTERROMPIDO (arquivo truncado x sha do pacote completo)
# ============================================
Write-Step "[2] Falha: download truncado (sha256 do pacote completo)"
$zipFull = Join-Path $PackagesDir "$SystemId-2.0.0-full.zip"
$zipTrunc = Join-Path $PackagesDir "$SystemId-2.0.0-truncado.zip"
$null = New-SystemPackage -Id $SystemId -Version "2.0.0" -ZipPath $zipFull
$fullBytes = [System.IO.File]::ReadAllBytes($zipFull)
$shaFull = (Get-FileHash -Path $zipFull -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllBytes($zipTrunc, $fullBytes[0..([Math]::Floor($fullBytes.Length * 0.6))])
$entryTrunc = New-SystemEntry -Id $SystemId -Version "2.0.0" -ZipPath $zipTrunc -ShaOverride $shaFull
$mTrunc = New-Manifest -SystemEntry $entryTrunc -Path (Join-Path $ManifestDir "m-trunc.json")
$run = Invoke-SystemUpdater -ManifestPath $mTrunc
$state = Get-State
Check "exit != 0 (sha256 do download != sha do pacote)" ($run.ExitCode -ne 0)
Check "estado Failed / hash mismatch" (($state.Status -eq "Failed") -and ($state.Message -like "*hash mismatch*"))
Check "versao 1.0.0 permanece" ((Get-InstalledVersion) -eq "1.0.0")
Check "sem restos de trabalho" (Test-WorkLeftovers)

# ============================================
# [3] PACOTE CORROMPIDO COM SHA VALIDO (extracao falha)
# ============================================
Write-Step "[3] Falha: bytes corrompidos com sha256 correspondente (validacao de estrutura)"
$zipCorrupt = Join-Path $PackagesDir "$SystemId-3.0.0.zip"
$null = New-SystemPackage -Id $SystemId -Version "3.0.0" -ZipPath $zipCorrupt
# corrompe no MEIO mantendo o tamanho: sha256 dos bytes corrompidos e valido
$bytes = [System.IO.File]::ReadAllBytes($zipCorrupt)
$mid = [Math]::Floor($bytes.Length / 2)
for ($i = $mid; $i -lt $mid + 512 -and $i -lt $bytes.Length; $i++) { $bytes[$i] = 0xFF }
[System.IO.File]::WriteAllBytes($zipCorrupt, $bytes)
$shaCorrupt = (Get-FileHash -Path $zipCorrupt -Algorithm SHA256).Hash.ToLowerInvariant()
$entryCorrupt = New-SystemEntry -Id $SystemId -Version "3.0.0" -ZipPath $zipCorrupt -ShaOverride $shaCorrupt
$mCorrupt = New-Manifest -SystemEntry $entryCorrupt -Path (Join-Path $ManifestDir "m-corrupt.json")
$run = Invoke-SystemUpdater -ManifestPath $mCorrupt
$state = Get-State
Check "exit != 0 (extracao do zip falhou)" ($run.ExitCode -ne 0)
Check "estado Failed / Extraction failed" (($state.Status -eq "Failed") -and ($state.Message -like "*Extraction failed*"))
Check "versao 1.0.0 permanece" ((Get-InstalledVersion) -eq "1.0.0")
Check "staging limpo apos falha de extracao" (Test-WorkLeftovers)

# ============================================
# [4] PROCESSO JA ABERTO (entry point em execucao) -> atualizacao conclui
# ============================================
Write-Step "[4] Cenario: processo do sistema ja aberto durante a atualizacao"
# substitui o entry point instalado por um executavel real que fica rodando
$pingSrc = Join-Path $env:SystemRoot "System32\ping.exe"
$entryInstalled = Join-Path $SystemsDir "$SystemId\app\executable\$SystemId.exe"
Copy-Item -Path $pingSrc -Destination $entryInstalled -Force
$helper = Start-Process -FilePath $entryInstalled -ArgumentList "127.0.0.1", "-t" -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 1
Check "processo do entry point em execucao" ($null -ne (Get-Process -Name $SystemId -ErrorAction SilentlyContinue))

$zip110 = New-SystemPackage -Id $SystemId -Version "1.1.0" -ZipPath (Join-Path $PackagesDir "$SystemId-1.1.0.zip")
$m110 = New-Manifest -SystemEntry (New-SystemEntry -Id $SystemId -Version "1.1.0" -ZipPath $zip110) -Path (Join-Path $ManifestDir "m-110.json")
$run = Invoke-SystemUpdater -ManifestPath $m110
Start-Sleep -Milliseconds 500
Check "exit 0 (updater parou o processo e trocou a versao)" ($run.ExitCode -eq 0)
Check "versao 1.1.0 instalada" ((Get-InstalledVersion) -eq "1.1.0")
Check "processo encerrado pelo updater" ($null -eq (Get-Process -Name $SystemId -ErrorAction SilentlyContinue))
Check "sem restos de trabalho" (Test-WorkLeftovers)
# limpeza defensiva (se a falha deixou o helper vivo)
Get-Process -Name $SystemId -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# ============================================
# [5] INTERRUPCAO DURANTE INSTALACAO (restos de crash + estado corrompido)
# ============================================
Write-Step "[5] Cenario: instalacao interrompida (pastas .staging/.old + estado em Downloading)"
Write-File (Join-Path $SystemsDir ".staging-$SystemId-crash01\junk.txt") "resto de staging"
Write-File (Join-Path $SystemsDir "$SystemId.old-20260101010101\junk.txt") "resto de versao antiga"
Write-File $StateFile '{"Status":"Downloading","Message":"Installing ' + $SystemId + '","Timestamp":"2026-01-01T00:00:00Z","Systems":[]}'

$zip120 = New-SystemPackage -Id $SystemId -Version "1.2.0" -ZipPath (Join-Path $PackagesDir "$SystemId-1.2.0.zip")
$m120 = New-Manifest -SystemEntry (New-SystemEntry -Id $SystemId -Version "1.2.0" -ZipPath $zip120) -Path (Join-Path $ManifestDir "m-120.json")
$run = Invoke-SystemUpdater -ManifestPath $m120
$state = Get-State
Check "recuperacao conclui (exit=0)" ($run.ExitCode -eq 0)
Check "versao 1.2.0 instalada" ((Get-InstalledVersion) -eq "1.2.0")
Check "estado Completed sobrescreveu o estado de crash" ($state.Status -eq "Completed")
Check "restos .staging-*/.old-* removidos" (Test-WorkLeftovers)
Check "banco de dados intacto apos todas as falhas" ((Get-Content $DbFile -Raw).Trim() -eq "conteudo do banco (preservar em qualquer falha)")

# ============================================
# RESUMO
# ============================================
Write-Host ""
Write-Host "========================================" -ForegroundColor $(if ($script:Failed -eq 0) { "Green" } else { "Red" })
Write-Host " Total: $script:Passed passaram, $script:Failed falharam" -ForegroundColor $(if ($script:Failed -eq 0) { "Green" } else { "Red" })
Write-Host "========================================" -ForegroundColor $(if ($script:Failed -eq 0) { "Green" } else { "Red" })

if ($script:Failed -gt 0) { exit 1 }
exit 0
