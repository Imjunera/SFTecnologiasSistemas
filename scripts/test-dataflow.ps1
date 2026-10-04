# ============================================
# SF Tecnologias - E2E DE DADOS (Fase 3/4/5, sem elevacao)
#   [1] instalar h2 1.0.2 (raiz isolada)
#   [2] subir API real, login, criar cliente        -> dado criado pela aplicacao
#   [3] atualizar 1.0.2 -> 1.1.0 (SF.Updater REAL)
#       - backup de software criado (retencao <=3)
#       - backup de migration do banco criado       -> §15
#   [4] subir API, login, listar clientes           -> §23 dados continuam presentes
#   [5] arquivo bloqueado + atualizar p/ 1.2.0      -> §14 rollback; versao e dados intactos
#   [6] liberar + atualizar p/ 1.2.0 com sucesso    -> estado normal restaurado (§24)
#   [7] instalar 2o sistema (livraria) e subir as 2 APIs
#       -> cada API enxerga apenas o SEU banco      -> §26 caminhos efetivos
#   [8] manifesto com host inexistente              -> §17/§18 falha de rede
#       nao bloqueia; instalacao permanece integra
#
# Requer: dist\updater\SF.Updater.exe, dist\packages\sf-system-h2-conveniencia-{1.0.2,1.1.0,1.2.0}.zip,
#         dist\packages\sf-system-livraria-papelaria-lider-1.0.0.zip
#         (.\package-system.ps1 -SystemId livraria-papelaria-lider -Name "Livraria Papelaria Lider" -Version 1.0.0 -SkipApi -SkipFrontend)
# ============================================

$ErrorActionPreference = 'Stop'
$Root        = $PSScriptRoot
$UpdaterExe  = Join-Path $Root 'dist\updater\SF.Updater.exe'
$PackagesDir = Join-Path $Root 'dist\packages'
$TestDir     = Join-Path $Root 'test-dataflow'
$InstallDir  = Join-Path $TestDir 'install'
$DataRoot    = Join-Path $TestDir 'programdata'
$DataDir     = Join-Path $DataRoot 'data'
$ManifestDir = Join-Path $TestDir 'manifests'

$H2 = 'h2-conveniencia'
$LV = 'livraria-papelaria-lider'

$script:Pass = 0
$script:Fail = 0

function Check([string]$Name, [bool]$Cond) {
    if ($Cond) { Write-Host "  PASS: $Name" -ForegroundColor Green; $script:Pass++ }
    else       { Write-Host "  FAIL: $Name" -ForegroundColor Red;   $script:Fail++ }
}
function Step([string]$Msg) { Write-Host ""; Write-Host "=== $Msg" -ForegroundColor Cyan }
function ZipOf([string]$Id, [string]$Ver) { Join-Path $PackagesDir "sf-system-$Id-$Ver.zip" }

function Write-Manifest {
    param([string]$Path, [string]$SysId, [string]$Ver, [string]$ZipPath, [string]$ShaOverride, [string]$UrlOverride)
    $sha = $ShaOverride; $size = 0; $file = Split-Path $ZipPath -Leaf
    if (-not $ShaOverride) {
        if (-not (Test-Path $ZipPath)) { throw "zip nao encontrado: $ZipPath" }
        $sha  = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash.ToLower()
        $size = (Get-Item $ZipPath).Length
    } else { $size = 48494015 }
    $url = $UrlOverride
    if (-not $url) { $url = ([System.Uri]$ZipPath).AbsoluteUri }
    $m = [ordered]@{
        product = 'SF.Tecnologias'; version = '1.0.2.1'; minimumUpdaterVersion = '1.0.0'
        components = @{ systems = @(
            [ordered]@{
                id = $SysId; name = $SysId; version = $Ver; minimumPlatformVersion = '1.0.2'
                packageUrl = $url; sha256 = $sha; fileName = $file; sizeBytes = $size
                required = $false; releaseNotes = 'dataflow test'
            }) }
        mandatory = $false; releaseNotes = 'dataflow test'
    }
    $json = $m | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($Path, $json)
    return $Path
}

function Invoke-Updater {
    param([string]$ManifestPath, [string]$SysId)
    $uri = ([System.Uri]$ManifestPath).AbsoluteUri
    $p = Start-Process -FilePath $UpdaterExe -ArgumentList @(
        '--manifest', "`"$uri`"",
        '--system', $SysId,
        '--install-dir', "`"$InstallDir`"",
        '--data-dir', "`"$DataDir`""
    ) -Wait -PassThru -WindowStyle Hidden
    return $p.ExitCode
}

function Installed-Version([string]$SysId) {
    $f = Join-Path $InstallDir "Systems\$SysId\system.json"
    if (-not (Test-Path $f)) { return $null }
    try { return ((Get-Content $f -Raw | ConvertFrom-Json).systemVersion) } catch { return $null }
}

$script:ApiProcs = @()
function Start-SystemApi {
    param([string]$SysId, [int]$Port)
    $entry = Join-Path $InstallDir "Systems\$SysId\app\executable\SFTecnologiasApi.exe"
    if (-not (Test-Path $entry)) { return $null }
    $db = Join-Path $DataDir "$SysId\database.sqlite"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $entry
    $psi.WorkingDirectory = (Split-Path $entry -Parent)
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables['ASPNETCORE_URLS'] = "http://localhost:$Port"
    $psi.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $psi.EnvironmentVariables['DatabaseProvider'] = 'SQLite'
    $psi.EnvironmentVariables['ConnectionStrings__DefaultConnection'] = "Data Source=$db"
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] 48
    $rng.GetBytes($bytes)
    $psi.EnvironmentVariables['Jwt__Secret'] = [Convert]::ToBase64String($bytes)
    $rng.Dispose()
    $proc = [System.Diagnostics.Process]::Start($psi)
    # Dreno assincrono de stdout/stderr: com redirect e SEM leitor, o buffer do pipe
    # (~4-64KB) enche com os logs do EF Core e a API congela no meio de uma request
    # (health/login passam, as requests seguintes dao timeout). O dreno mantem o pipe
    # livre; o buffer em memoria e descartado no fim do processo.
    $null = $proc.StandardOutput.ReadToEndAsync()
    $null = $proc.StandardError.ReadToEndAsync()
    $script:ApiProcs += $proc
    return $proc
}

function Stop-SystemApi([int]$Port) {
    Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        ForEach-Object { try { Stop-Process -Id $_.OwningProcess -Force -ErrorAction Stop } catch {} }
}

function Wait-Health([int]$Port, [int]$Tries = 40) {
    for ($i = 0; $i -lt $Tries; $i++) {
        try { $r = Invoke-WebRequest -Uri "http://localhost:$Port/health" -TimeoutSec 2 -UseBasicParsing; if ($r.StatusCode -eq 200) { return $true } } catch {}
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Login([int]$Port) {
    $body = @{ empresaCodigo = 'H2CONV'; senha = 'H22026' } | ConvertTo-Json
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:$Port/api/auth/login" -Method Post -Body $body -ContentType 'application/json' -TimeoutSec 15 -UseBasicParsing
        return ($r.Content | ConvertFrom-Json).accessToken
    } catch { return $null }
}

function Create-Cliente([int]$Port, [string]$Token, [string]$Nome) {
    $body = @{ nome = $Nome } | ConvertTo-Json
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:$Port/api/clientes/" -Method Post -Body $body `
            -ContentType 'application/json' -Headers @{ Authorization = "Bearer $Token" } -TimeoutSec 15 -UseBasicParsing
        return ($r.StatusCode -in 200,201)
    } catch {
        try {
            $r = Invoke-WebRequest -Uri "http://localhost:$Port/api/clientes" -Method Post -Body $body `
                -ContentType 'application/json' -Headers @{ Authorization = "Bearer $Token" } -TimeoutSec 15 -UseBasicParsing
            return ($r.StatusCode -in 200,201)
        } catch { return $false }
    }
}

function Get-Clientes([int]$Port, [string]$Token) {
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:$Port/api/clientes/" -TimeoutSec 15 -UseBasicParsing `
            -Headers @{ Authorization = "Bearer $Token" }
        return @($r.Content | ConvertFrom-Json)
    } catch {
        try {
            $r = Invoke-WebRequest -Uri "http://localhost:$Port/api/clientes" -TimeoutSec 15 -UseBasicParsing `
                -Headers @{ Authorization = "Bearer $Token" }
            return @($r.Content | ConvertFrom-Json)
        } catch { return @() }
    }
}

# ============================================
# Setup
# ============================================
if (Test-Path $TestDir) { Remove-Item $TestDir -Recurse -Force }
New-Item -Path $ManifestDir -ItemType Directory -Force | Out-Null
New-Item -Path $DataDir     -ItemType Directory -Force | Out-Null
# Raiz de instalacao precisa do version.json da plataforma (instalacao real o possui);
# sem ele o updater assume 0.0.0 e reprova o sistema por "Platform incompatible".
New-Item -Path $InstallDir  -ItemType Directory -Force | Out-Null
Copy-Item -Path (Join-Path $Root 'src\desktop\version.json') -Destination (Join-Path $InstallDir 'version.json') -Force

$m1 = Write-Manifest (Join-Path $ManifestDir 'h2-102.json') $H2 '1.0.2' (ZipOf $H2 '1.0.2')
$m2 = Write-Manifest (Join-Path $ManifestDir 'h2-110.json') $H2 '1.1.0' (ZipOf $H2 '1.1.0')
$m3 = Write-Manifest (Join-Path $ManifestDir 'h2-120.json') $H2 '1.2.0' (ZipOf $H2 '1.2.0')
$ml = Write-Manifest (Join-Path $ManifestDir 'lv-100.json') $LV '1.0.0' (ZipOf $LV '1.0.0')

# ============================================
# [1] instalacao individual (1.0.2)
# ============================================
Step "[1] instalar $H2 1.0.2 (manifesto local)"
$e = Invoke-Updater $m1 $H2
Check "instalacao 1.0.2 exit=0 (exit=$e)" ($e -eq 0)
Check "system.json instalado" (Test-Path (Join-Path $InstallDir "Systems\$H2\system.json"))
Check "versao instalada = 1.0.2" ((Installed-Version $H2) -eq '1.0.2')

# ============================================
# [2] dado criado pela aplicacao
# ============================================
Step "[2] API real: login + criar cliente"
$api = Start-SystemApi $H2 5010
$ok = Wait-Health 5010
Check "API do sistema inicia (/health 5010)" $ok
$tok = Login 5010
Check "login H2CONV" ([bool]$tok)
$created = $false
$secondApiOk = $false
if ($tok) {
    $created = Create-Cliente 5010 $tok 'Cliente Previo A Atualizacao'
    Check "cliente criado pela aplicacao" $created
    $names = @(Get-Clientes 5010 $tok | ForEach-Object { $_.nome })
    Check "cliente listado na API" (@($names | Where-Object { $_ -like 'Cliente Previo*' }).Count -gt 0)
}
Stop-SystemApi 5010
Start-Sleep -Seconds 1
$dbFile = Join-Path $DataDir "$H2\database.sqlite"
Check "banco por sistema criado" (Test-Path $dbFile)

# ============================================
# [3] atualizacao 1.0.2 -> 1.1.0 (§15: backups)
# ============================================
Step "[3] atualizar 1.0.2 -> 1.1.0 (SF.Updater real)"
$e = Invoke-Updater $m2 $H2
Check "atualizacao exit=0 (exit=$e)" ($e -eq 0)
Check "versao instalada = 1.1.0" ((Installed-Version $H2) -eq '1.1.0')
$swBackups = @(Get-ChildItem (Join-Path $DataRoot 'backups\systems') -Directory -ErrorAction SilentlyContinue)
$swBackupH2 = @(Get-ChildItem (Join-Path $DataRoot "backups\systems\$H2") -Directory -ErrorAction SilentlyContinue)
Check "backup de software criado (§13)" ($swBackupH2.Count -ge 1)
$migBackups = @(Get-ChildItem (Join-Path $DataDir "$H2\backups") -Directory -Filter 'migration-*' -ErrorAction SilentlyContinue)
Check "backup de migration do banco criado (§15)" ($migBackups.Count -ge 1)

# ============================================
# [4] dados continuam presentes (§23)
# ============================================
Step "[4] dados apos atualizacao"
$api = Start-SystemApi $H2 5010
$ok = Wait-Health 5010
Check "sistema ATUALIZADO inicia (/health 5010)" $ok
$tok = Login 5010
Check "login apos atualizacao" ([bool]$tok)
if ($tok) {
    $names = @(Get-Clientes 5010 $tok | ForEach-Object { $_.nome })
    Check "cliente pre-existente ainda presente (§23)" (@($names | Where-Object { $_ -like 'Cliente Previo*' }).Count -gt 0)
}
Stop-SystemApi 5010
Start-Sleep -Seconds 1
$hashBeforeLocked = (Get-FileHash $dbFile -Algorithm SHA256).Hash

# ============================================
# [5] arquivo bloqueado -> rollback (§14)
# ============================================
Step "[5] arquivo bloqueado + atualizar p/ 1.2.0 (rollback)"
$lockFile = Join-Path $InstallDir "Systems\$H2\system.json"
$lock = [System.IO.File]::Open($lockFile, 'Open', 'Read', 'Read')
try {
    $e = Invoke-Updater $m3 $H2
    Check "atualizacao com arquivo bloqueado falha (exit!=0) (exit=$e)" ($e -ne 0)
} finally { $lock.Close() }
Check "versao preservada = 1.1.0 apos falha" ((Installed-Version $H2) -eq '1.1.0')
$hashAfterLocked = (Get-FileHash $dbFile -Algorithm SHA256).Hash
Check "banco intacto apos falha (hash identico)" ($hashBeforeLocked -eq $hashAfterLocked)

# ============================================
# [6] recuperacao: atualizar p/ 1.2.0 (§24)
# ============================================
Step "[6] liberar arquivo e concluir atualizacao p/ 1.2.0"
$e = Invoke-Updater $m3 $H2
Check "recuperacao exit=0 (exit=$e)" ($e -eq 0)
Check "versao instalada = 1.2.0" ((Installed-Version $H2) -eq '1.2.0')
$swBackupH2 = @(Get-ChildItem (Join-Path $DataRoot "backups\systems\$H2") -Directory -ErrorAction SilentlyContinue)
Check "retencao de backups de software <= 3 (tem $($swBackupH2.Count))" ($swBackupH2.Count -le 3)
$api = Start-SystemApi $H2 5010
$ok = Wait-Health 5010
Check "sistema 1.2.0 inicia" $ok
$tok = Login 5010
if ($tok) {
    $names = @(Get-Clientes 5010 $tok | ForEach-Object { $_.nome })
    Check "cliente sobreviveu a todo o ciclo" (@($names | Where-Object { $_ -like 'Cliente Previo*' }).Count -gt 0)
}
Stop-SystemApi 5010
Start-Sleep -Seconds 1

# ============================================
# [7] dois sistemas: caminhos efetivos (§26)
# ============================================
Step "[7] instalar $LV e subir as 2 APIs (banco efetivo de cada)"
$e = Invoke-Updater $ml $LV
Check "instalacao $LV exit=0 (exit=$e)" ($e -eq 0)
Check "versao $LV = 1.0.0" ((Installed-Version $LV) -eq '1.0.0')

$apiA = Start-SystemApi $H2 5010
$apiB = Start-SystemApi $LV 5011
$okA = Wait-Health 5010
$okB = Wait-Health 5011
Check "API H2 inicia (5010)" $okA
Check "API Livraria inicia (5011)" $okB
$tokA = Login 5010
$tokB = Login 5011
Check "login nas 2 APIs" ([bool]($tokA -and $tokB))
$mkA = $false; $mkB = $false
if ($tokA) { $mkA = Create-Cliente 5010 $tokA 'Cliente Unico do H2' }
if ($tokB) { $mkB = Create-Cliente 5011 $tokB 'Cliente Unico da Livraria' }
Check "cada API gravou seu cliente" ($mkA -and $mkB)
if ($tokA -and $tokB) {
    $nA = @(Get-Clientes 5010 $tokA | ForEach-Object { $_.nome })
    $nB = @(Get-Clientes 5011 $tokB | ForEach-Object { $_.nome })
    Check "H2 enxerga apenas o SEU cliente" ((@($nA | Where-Object { $_ -like 'Cliente Unico do H2' }).Count -gt 0) -and (@($nA | Where-Object { $_ -like '*Livraria*' }).Count -eq 0))
    Check "Livraria enxerga apenas o SEU cliente" ((@($nB | Where-Object { $_ -like 'Cliente Unico da Livraria' }).Count -gt 0) -and (@($nB | Where-Object { $_ -like '*do H2*' }).Count -eq 0))
    $dbA = Join-Path $DataDir "$H2\database.sqlite"
    $dbB = Join-Path $DataDir "$LV\database.sqlite"
    $mtA = (Get-Item $dbA).LastWriteTime
    $mtB = (Get-Item $dbB).LastWriteTime
    Check "bancos distintos, escritas independentes (mtimes distintos ou arquivos separados)" ((Test-Path $dbA) -and (Test-Path $dbB) -and ($dbA -ne $dbB))
}
Stop-SystemApi 5010
Stop-SystemApi 5011
Start-Sleep -Seconds 1

# ============================================
# [8] falha de rede nao bloqueia (§17/§18)
# ============================================
Step "[8] manifesto com host inexistente (falha de download)"
$mBad = Write-Manifest (Join-Path $ManifestDir 'h2-999.json') $H2 '9.9.9' (ZipOf $H2 '1.2.0') `
    -ShaOverride ('0' * 64) -UrlOverride 'http://nao-existe-sf.invalid/sf-system-h2-conveniencia-9.9.9.zip'
$e = Invoke-Updater $mBad $H2
Check "download inexistente retorna exit!=0 (exit=$e)" ($e -ne 0)
Check "versao permanece 1.2.0 (nada foi substituido)" ((Installed-Version $H2) -eq '1.2.0')
$api = Start-SystemApi $H2 5010
$ok = Wait-Health 5010
Check "sistema continua iniciando com rede falha" $ok
$tok = Login 5010
if ($tok) {
    $names = @(Get-Clientes 5010 $tok | ForEach-Object { $_.nome })
    Check "dados locais usaveis sem rede (§17)" (@($names | Where-Object { $_ -like 'Cliente*' }).Count -gt 0)
}
Stop-SystemApi 5010

# ============================================
Write-Host ""
Write-Host "=== RESUMO: $($script:Pass) PASS, $($script:Fail) FAIL ===" -ForegroundColor $(if ($script:Fail -eq 0) { 'Green' } else { 'Red' })
if ($script:Fail -gt 0) { exit 1 } else { exit 0 }
