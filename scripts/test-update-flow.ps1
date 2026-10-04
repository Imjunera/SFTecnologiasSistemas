# ============================================
# SF Tecnologias - Teste Local de Atualizacao
# Simula o fluxo de atualizacao 1.0.0 -> 1.0.1
# ============================================

param(
    [string]$FromVersion = "1.0.0",
    [string]$ToVersion = "1.0.1",
    [switch]$WithFailure
)

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot
$TestDir = Join-Path $RootDir "test-update"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " SF Tecnologias - Teste de Atualizacao" -ForegroundColor Cyan
Write-Host " $FromVersion -> $ToVersion" -ForegroundColor Cyan
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
}

function Show-Info {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Gray
}

function Create-TestFile {
    param([string]$Path, [string]$Content)
    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) { New-Item -Path $dir -ItemType Directory -Force | Out-Null }
    Set-Content -Path $Path -Value $Content -Encoding UTF8
}

# ============================================
# PREPARAR AMBIENTE DE TESTE
# ============================================
Write-Host "Preparando ambiente de teste..." -ForegroundColor Yellow

# Limpar teste anterior
if (Test-Path $TestDir) { Remove-Item -Path $TestDir -Recurse -Force }
New-Item -Path $TestDir -ItemType Directory -Force | Out-Null

# Criar estrutura simulada de instalacao
$installDir = Join-Path $TestDir "install"
$dataDir = Join-Path $TestDir "data"
$configDir = Join-Path $TestDir "config"
$backupsDir = Join-Path $TestDir "backups"
$logsDir = Join-Path $TestDir "logs"
$updatesDir = Join-Path $TestDir "updates"

New-Item -Path $installDir -ItemType Directory -Force | Out-Null
New-Item -Path $dataDir -ItemType Directory -Force | Out-Null
New-Item -Path $configDir -ItemType Directory -Force | Out-Null
New-Item -Path $backupsDir -ItemType Directory -Force | Out-Null
New-Item -Path $logsDir -ItemType Directory -Force | Out-Null
New-Item -Path $updatesDir -ItemType Directory -Force | Out-Null

# Criar arquivos simulados da versao 1.0.0
Write-Host ""
Write-Host "Criando arquivos simulados da versao $FromVersion..." -ForegroundColor Yellow

Create-TestFile (Join-Path $installDir "SF Tecnologias BETA.exe") "Desktop v$FromVersion"
Create-TestFile (Join-Path $installDir "version.json") "{`"version`": `"$FromVersion`"}"
Create-TestFile (Join-Path $installDir "resources\api\SFTecnologiasApi.exe") "API v$FromVersion"
Create-TestFile (Join-Path $installDir "resources\api\appsettings.json") "{`"DatabaseProvider`": `"SQLite`"}"
Create-TestFile (Join-Path $installDir "resources\renderer\index.html") "<html>Frontend v$FromVersion</html>"

# Criar banco de dados simulado
Create-TestFile (Join-Path $dataDir "SFTecnologias.db") "SQLite database content"
Create-TestFile (Join-Path $dataDir "SFTecnologias.db-wal") "WAL content"
Create-TestFile (Join-Path $dataDir "SFTecnologias.db-shm") "SHM content"

# Criar dados de empresa simulados
Create-TestFile (Join-Path $dataDir "empresa-1.json") "{`"id`": 1, `"nome`": `"Empresa Teste`"}"
Create-TestFile (Join-Path $dataDir "usuario-1.json") "{`"id`": 1, `"nome`": `"Admin`"}"

Show-Ok "Ambiente de teste criado"

# ============================================
# SIMULAR BUILD DA VERSAO 1.0.1
# ============================================
Write-Host ""
Write-Host "Simulando build da versao $ToVersion..." -ForegroundColor Yellow

$buildDir = Join-Path $TestDir "build"
New-Item -Path $buildDir -ItemType Directory -Force | Out-Null

# Desktop 1.0.1
$desktopBuild = Join-Path $buildDir "desktop"
New-Item -Path $desktopBuild -ItemType Directory -Force | Out-Null
Create-TestFile (Join-Path $desktopBuild "SF Tecnologias BETA.exe") "Desktop v$ToVersion"
Create-TestFile (Join-Path $desktopBuild "version.json") "{`"version`": `"$ToVersion`"}"
Create-TestFile (Join-Path $desktopBuild "resources\api\SFTecnologiasApi.exe") "API v$ToVersion"
Create-TestFile (Join-Path $desktopBuild "resources\api\appsettings.json") "{`"DatabaseProvider`": `"SQLite`"}"
Create-TestFile (Join-Path $desktopBuild "resources\renderer\index.html") "<html>Frontend v$ToVersion</html>"

# API 1.0.1
$apiBuild = Join-Path $buildDir "api"
New-Item -Path $apiBuild -ItemType Directory -Force | Out-Null
Create-TestFile (Join-Path $apiBuild "SFTecnologiasApi.exe") "API v$ToVersion"
Create-TestFile (Join-Path $apiBuild "appsettings.json") "{`"DatabaseProvider`": `"SQLite`"}"

# Criar pacotes ZIP
$packagesDir = Join-Path $TestDir "packages"
New-Item -Path $packagesDir -ItemType Directory -Force | Out-Null

$desktopZip = Join-Path $packagesDir "sf-tecnologias-desktop-$ToVersion.zip"
$apiZip = Join-Path $packagesDir "sf-tecnologias-api-$ToVersion.zip"

Compress-Archive -Path "$desktopBuild\*" -DestinationPath $desktopZip -Force
Compress-Archive -Path "$apiBuild\*" -DestinationPath $apiZip -Force

# Calcular SHA-256
$desktopHash = (Get-FileHash -Path $desktopZip -Algorithm SHA256).Hash.ToLowerInvariant()
$apiHash = (Get-FileHash -Path $apiZip -Algorithm SHA256).Hash.ToLowerInvariant()

Show-Ok "Pacotes criados"
Show-Info "Desktop: $desktopHash"
Show-Info "API: $apiHash"

# Criar manifesto
$manifest = @{
    product = "SF.Tecnologias"
    version = $ToVersion
    minimumUpdaterVersion = "1.0.0"
    components = @{
        desktop = @{
            version = $ToVersion
            packageUrl = "file:///$($desktopZip -replace '\\', '/')"
            sha256 = $desktopHash
        }
        api = @{
            version = $ToVersion
            packageUrl = "file:///$($apiZip -replace '\\', '/')"
            sha256 = $apiHash
        }
    }
    mandatory = $false
    releaseNotes = "Test update from $FromVersion to $ToVersion"
}

$manifestFile = Join-Path $packagesDir "manifest.json"
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestFile -Encoding UTF8

Show-Ok "Manifesto criado: $manifestFile"

# ============================================
# CRIAR BACKUP DA VERSAO ATUAL
# ============================================
Write-Host ""
Write-Host "Criando backup da versao $FromVersion..." -ForegroundColor Yellow

$backupDir = Join-Path $backupsDir "backup-$FromVersion"
New-Item -Path $backupDir -ItemType Directory -Force | Out-Null

# Copiar arquivos para backup
Get-ChildItem -Path $installDir -Recurse -File | ForEach-Object {
    $relativePath = $_.FullName.Substring($installDir.Length)
    $destPath = Join-Path $backupDir $relativePath
    $destDir = Split-Path $destPath -Parent
    if (-not (Test-Path $destDir)) { New-Item -Path $destDir -ItemType Directory -Force | Out-Null }
    Copy-Item -Path $_.FullName -Destination $destPath -Force
}

Show-Ok "Backup criado: $backupDir"

# ============================================
# SIMULAR ATUALIZACAO
# ============================================
Write-Host ""
Write-Host "Simulando atualizacao..." -ForegroundColor Yellow

# 1. Parar Desktop (simulado)
Write-Host "  [1/6] Parando Desktop..." -ForegroundColor Gray
Start-Sleep -Milliseconds 500

# 2. Parar API (simulado)
Write-Host "  [2/6] Parando API..." -ForegroundColor Gray
Start-Sleep -Milliseconds 500

# 3. Extrair Desktop
Write-Host "  [3/6] Atualizando Desktop..." -ForegroundColor Gray
Expand-Archive -Path $desktopZip -DestinationPath $installDir -Force

# 4. Extrair API
Write-Host "  [4/6] Atualizando API..." -ForegroundColor Gray
$apiDir = Join-Path $installDir "resources\api"
New-Item -Path $apiDir -ItemType Directory -Force | Out-Null
Expand-Archive -Path $apiZip -DestinationPath $apiDir -Force

# 5. Iniciar API (simulado)
Write-Host "  [5/6] Iniciando API..." -ForegroundColor Gray
Start-Sleep -Milliseconds 500

# 6. Health check (simulado)
Write-Host "  [6/6] Health check..." -ForegroundColor Gray
Start-Sleep -Milliseconds 500

Show-Ok "Atualizacao concluida"

# ============================================
# VALIDACOES
# ============================================
Write-Host ""
Write-Host "Validando resultado..." -ForegroundColor Yellow

$validations = @()

# Verificar versao do Desktop
$desktopVersion = Get-Content (Join-Path $installDir "version.json") | ConvertFrom-Json
$validations += @{ Name = "Desktop version"; Pass = $desktopVersion.version -eq $ToVersion }

# Verificar conteudo do Desktop
$desktopContent = Get-Content (Join-Path $installDir "SF Tecnologias BETA.exe")
$validations += @{ Name = "Desktop content"; Pass = $desktopContent -like "*v$ToVersion*" }

# Verificar versao da API
$apiContent = Get-Content (Join-Path $installDir "resources\api\SFTecnologiasApi.exe")
$validations += @{ Name = "API version"; Pass = $apiContent -like "*v$ToVersion*" }

# Verificar que o banco NAO foi alterado
$dbContent = Get-Content (Join-Path $dataDir "SFTecnologias.db")
$validations += @{ Name = "SQLite preserved"; Pass = $dbContent -eq "SQLite database content" }

# Verificar que os dados da empresa NAO foram alterados
$empresaContent = Get-Content (Join-Path $dataDir "empresa-1.json")
$validations += @{ Name = "Empresa data preserved"; Pass = $empresaContent -like "*Empresa Teste*" }

# Verificar que o usuario NAO foi alterado
$usuarioContent = Get-Content (Join-Path $dataDir "usuario-1.json")
$validations += @{ Name = "Usuario data preserved"; Pass = $usuarioContent -like "*Admin*" }

# Verificar backup
$backupFiles = Get-ChildItem -Path $backupDir -Recurse -File
$validations += @{ Name = "Backup created"; Pass = $backupFiles.Count -gt 0 }

# Verificar que backup contem versao antiga
$backupDesktop = Get-Content (Join-Path $backupDir "SF Tecnologias BETA.exe")
$validations += @{ Name = "Backup has old version"; Pass = $backupDesktop -like "*v$FromVersion*" }

# Exibir resultados
Write-Host ""
Write-Host "Resultados:" -ForegroundColor Cyan
$passed = 0
$failed = 0
foreach ($v in $validations) {
    if ($v.Pass) {
        Write-Host "  PASS: $($v.Name)" -ForegroundColor Green
        $passed++
    } else {
        Write-Host "  FAIL: $($v.Name)" -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
Write-Host "Total: $passed passed, $failed failed" -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Red' })

# ============================================
# SIMULAR FALHA (OPCIONAL)
# ============================================
if ($WithFailure) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host " SIMULACAO DE FALHA" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    
    # Criar pacote corrompido
    $corruptedZip = Join-Path $packagesDir "corrupted-package.zip"
    Set-Content -Path $corruptedZip -Value "this is not a valid zip file" -Encoding UTF8
    
    $corruptedHash = (Get-FileHash -Path $corruptedZip -Algorithm SHA256).Hash.ToLowerInvariant()
    
    # Manifesto com hash incorreto
    $badManifest = @{
        product = "SF.Tecnologias"
        version = "9.9.9"
        components = @{
            desktop = @{
                version = "9.9.9"
                packageUrl = "file:///$($corruptedZip -replace '\\', '/')"
                sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
            }
        }
        mandatory = $true
        releaseNotes = "Corrupted test"
    }
    
    $badManifestFile = Join-Path $packagesDir "manifest-corrupted.json"
    $badManifest | ConvertTo-Json -Depth 10 | Set-Content -Path $badManifestFile -Encoding UTF8
    
    Write-Host "Teste de falha preparado:" -ForegroundColor Yellow
    Show-Info "Pacote corrompido: $corruptedZip"
    Show-Info "Manifesto com hash incorreto: $badManifestFile"
    Show-Info "Hash real: $corruptedHash"
    Show-Info "Hash esperado (errado): 0000000000000000000000000000000000000000000000000000000000000000"
    
    Write-Host ""
    Write-Host "O updater deve detectar a falha de SHA-256 e rejeitar o pacote." -ForegroundColor Yellow
}

# ============================================
# RESUMO
# ============================================
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Teste de atualizacao (simulado) OK!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Diretorios:" -ForegroundColor Cyan
Write-Host "  Install: $installDir" -ForegroundColor White
Write-Host "  Data:    $dataDir" -ForegroundColor White
Write-Host "  Backup:  $backupDir" -ForegroundColor White
Write-Host "  Packages: $packagesDir" -ForegroundColor White

# ============================================
# EXECUTAR UPDATER REAL (se disponivel)
# ============================================
$updaterExe = Join-Path $RootDir "dist\updater\SF.Updater.exe"
if (Test-Path $updaterExe) {
    Write-Host ""
    Write-Host "Executando SF.Updater.exe real..." -ForegroundColor Yellow

    $manifestUri = "file:///" + ($manifestFile -replace '\\', '/')
    $updaterArgs = @(
        "--manifest", "`"$manifestUri`"",
        "--install-dir", "`"$installDir`"",
        "--data-dir", "`"$dataDir`""
    )

    $proc = Start-Process -FilePath $updaterExe -ArgumentList $updaterArgs -Wait -PassThru -NoNewWindow
    $exitCode = $proc.ExitCode

    $validations2 = @()
    $newVersionFile = Join-Path $installDir "version.json"
    if (Test-Path $newVersionFile) {
        $v = Get-Content $newVersionFile | ConvertFrom-Json
        $validations2 += @{ Name = "Real updater: version.json updated"; Pass = $v.version -eq $ToVersion }
    } else {
        $validations2 += @{ Name = "Real updater: version.json present"; Pass = $false }
    }
    $validations2 += @{ Name = "Real updater: exit code 0"; Pass = $exitCode -eq 0 }

    $stateFile = Join-Path (Split-Path $dataDir -Parent) "config\updater-state.json"
    if (Test-Path $stateFile) {
        $st = Get-Content $stateFile | ConvertFrom-Json
        $validations2 += @{ Name = "Real updater: state Completed"; Pass = $st.Status -eq "Completed" }
    }

    Write-Host ""
    Write-Host "Resultados (updater real):" -ForegroundColor Cyan
    $p2 = 0; $f2 = 0
    foreach ($v in $validations2) {
        if ($v.Pass) { Write-Host "  PASS: $($v.Name)" -ForegroundColor Green; $p2++ }
        else { Write-Host "  FAIL: $($v.Name)" -ForegroundColor Red; $f2++ }
    }
    Write-Host "Total real: $p2 passed, $f2 failed" -ForegroundColor $(if ($f2 -eq 0) { 'Green' } else { 'Red' })
} else {
    Write-Host ""
    Write-Host "SF.Updater.exe nao encontrado em dist\updater - pule build do updater." -ForegroundColor Yellow
    Write-Host "  .\build-release.ps1 -SkipApi -SkipFrontend -SkipDesktop" -ForegroundColor Cyan
}
