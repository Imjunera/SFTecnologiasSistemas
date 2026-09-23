#Requires -RunAsAdministrator
<#
.SYNOPSIS
    SF Tecnologias - Instalador e Gerenciador do Sistema
.DESCRIPTION
    Instala, atualiza e desinstala o SF Tecnologias como aplicacao desktop.
    Verifica e instala dependencias automaticamente (VC++ Redistributable).
    Registra a API como servico Windows opcionalmente.
.EXAMPLE
    .\Setup-SF.ps1 install
    .\Setup-SF.ps1 uninstall
    .\Setup-SF.ps1 status
    .\Setup-SF.ps1 service-install
    .\Setup-SF.ps1 service-uninstall
#>
$ErrorActionPreference = "Stop"

# ============================================
# CONFIGURACAO
# ============================================
$AppName = "SF Tecnologias"
$AppNameShort = "SFTecnologias"
$ServiceName = "SFTecnologiasApi"
$ServiceDisplayName = "SF Tecnologias API"
$ServiceDescription = "API backend do SF Tecnologias - Servico local"

# INSTALL CONTRACT: Binaries vs Data separation
# Binaries: %ProgramFiles%\SF Tecnologias\ (replaceable by updater)
# Data:     %ProgramData%\SF Tecnologias\data\ (NEVER replaced by updater)
$InstallDir = Join-Path ${env:ProgramFiles} $AppName
$DataDir = Join-Path $env:ProgramData "$AppName\data"
$ConfigDir = Join-Path $env:ProgramData "$AppName\config"
$BackupsDir = Join-Path $env:ProgramData "$AppName\backups"
$LogsDir = Join-Path $env:ProgramData "$AppName\logs"
$UpdatesDir = Join-Path $env:ProgramData "$AppName\updates"
$OldDataDir = Join-Path $env:LOCALAPPDATA $AppName
$StartMenuDir = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\$AppName"
$DesktopShortcut = Join-Path $env:USERPROFILE "Desktop\$AppName.lnk"

$SourceDir = $PSScriptRoot
$ApiSource = Join-Path $SourceDir "api"
$DesktopSource = Join-Path $SourceDir "release\win-unpacked"

# URLs para download de dependencias
$VCRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe"
$VCRedistFile = Join-Path $env:TEMP "vc_redist.x64.exe"
# SHA-256 oficial do vc_redist.x64.exe (aka.ms/vs/17/release, medido em 2026-09-23)
# Se o Microsoft atualizar o instalador, atualize este hash junto.
$VCRedistSha256 = "CC0FF0EB1DC3F5188AE6300FAEF32BF5BEEBA4BDD6E8E445A9184072096B713B"

# ============================================
# FUNCOES AUXILIARES
# ============================================
function Show-Header {
    param([string]$Title = "")
    Clear-Host
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "  $AppName - Instalador" -ForegroundColor Cyan
    if ($Title) {
        Write-Host "  $Title" -ForegroundColor Cyan
    }
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host ""
}

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

function Show-Warn {
    param([string]$Message)
    Write-Host "  AVISO: $Message" -ForegroundColor Yellow
}

function Show-Info {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Gray
}

function Show-Done {
    param([string]$Message)
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Green
    Write-Host "  $Message" -ForegroundColor Green
    Write-Host "================================================" -ForegroundColor Green
}

# ============================================
# VERIFICACAO E INSTALACAO DE DEPENDENCIAS
# ============================================
function Test-VCRedist {
    # Verifica se o VC++ Redistributable 2015-2022 esta instalado
    $regPath = "HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"
    if (Test-Path $regPath) {
        $rawVersion = (Get-ItemProperty $regPath).Version
        try {
            $version = [version]$rawVersion
            if ($version -ge [version]"14.38") {
                return $true
            }
        } catch {
            # Fallback: parsing manual se registry retornar valor inesperado
            if ("$rawVersion" -match "^14\.(\d+)") {
                if ([int]$Matches[1] -ge 38) { return $true }
            }
        }
    }
    
    # Verifica pelo DisplayName no Uninstall (x64 + 2015-2022)
    $uninstallPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    $vcRedist = Get-ItemProperty $uninstallPath -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like "*Visual C++*Redistributable*x64*" -and $_.DisplayName -like "*2015-2022*" }
    
    return ($null -ne $vcRedist)
}

function Install-VCRedist {
    Write-Host "  Verificando VC++ Redistributable..." -ForegroundColor Gray
    
    if (Test-VCRedist) {
        Show-Ok "VC++ Redistributable ja instalado"
        return $true
    }
    
    Show-Warn "VC++ Redistributable nao encontrado"
    Write-Host "  O Electron (framework do app) precisa deste componente." -ForegroundColor Gray
    Write-Host "  Baixando de: $VCRedistUrl" -ForegroundColor Gray
    
    try {
        # Tentar baixar
        $wc = New-Object System.Net.WebClient
        $wc.DownloadFile($VCRedistUrl, $VCRedistFile)

        # Verificar integridade (SHA-256) se hash esperado estiver configurado
        if ($VCRedistSha256) {
            $actualHash = (Get-FileHash -Path $VCRedistFile -Algorithm SHA256).Hash
            if ($actualHash -ne $VCRedistSha256) {
                Show-Warn "SHA-256 do vc_redist nao confere (possivel download corrompido ou hash desatualizado)"
                Show-Info "Esperado: $VCRedistSha256"
                Show-Info "Obtido:   $actualHash"
                # Continua mesmo assim: hash pode estar desatualizado apos update do Microsoft
            } else {
                Show-Ok "SHA-256 do vc_redist validado"
            }
        }
        
        Write-Host "  Instalando VC++ Redistributable (silencioso)..." -ForegroundColor Gray
        $process = Start-Process -FilePath $VCRedistFile -ArgumentList "/install", "/quiet", "/norestart" -Wait -PassThru
        
        if ($process.ExitCode -eq 0 -or $process.ExitCode -eq 3010) {
            Show-Ok "VC++ Redistributable instalado com sucesso"
            # Limpar
            Remove-Item -Path $VCRedistFile -Force -ErrorAction SilentlyContinue
            return $true
        } else {
            Show-Fail "Falha na instalacao do VC++ Redistributable (codigo: $($process.ExitCode))"
            Show-Info "Baixe manualmente de: https://aka.ms/vs/17/release/vc_redist.x64.exe"
            return $false
        }
    } catch {
        Show-Fail "Nao foi possivel baixar: $($_.Exception.Message)"
        Show-Info "Baixe manualmente de: https://aka.ms/vs/17/release/vc_redist.x64.exe"
        Show-Info "Execute: vc_redist.x64.exe /install /quiet /norestart"
        return $false
    }
}

# ============================================
# INSTALACAO
# ============================================
function Install-App {
    Show-Header "INSTALACAO"
    
    $totalSteps = 7
    $step = 0
    
    # Passo 1: Verificar origem
    $step++
    Show-Step $step $totalSteps "Verificando arquivos de instalacao..."
    
    if (-not (Test-Path $DesktopSource)) {
        Show-Fail "Pasta do app nao encontrada: $DesktopSource"
        Show-Info "Execute primeiro: npm run build:beta (na pasta src/desktop)"
        exit 1
    }
    Show-Ok "Arquivos do app encontrados"
    
    # Passo 2: Verificar/instalar dependencias
    $step++
    Show-Step $step $totalSteps "Verificando dependencias..."
    
    Install-VCRedist | Out-Null
    
    # Passo 3: Parar app existente
    $step++
    Show-Step $step $totalSteps "Verificando instalacao existente..."
    
    $runningProc = Get-Process -Name "SF Tecnologias BETA" -ErrorAction SilentlyContinue
    if ($runningProc) {
        Show-Warn "App esta em execucao. Fechando..."
        $runningProc | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
    
    # Parar servico se existir
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        Show-Warn "Servico encontrado. Parando..."
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    }
    
    Show-Ok "Verificacao concluida"
    
    # Passo 4: Copiar arquivos
    $step++
    Show-Step $step $totalSteps "Instalando arquivos em $InstallDir..."
    
    # Criar diretorio de instalacao
    if (Test-Path $InstallDir) {
        Remove-Item -Path $InstallDir -Recurse -Force
    }
    New-Item -Path $InstallDir -ItemType Directory -Force | Out-Null
    
    # Copiar app Electron
    Write-Host "  Copiando aplicacao..." -ForegroundColor Gray
    Copy-Item -Path "$DesktopSource\*" -Destination $InstallDir -Recurse -Force
    
    # Criar diretorio de dados (INSTALL CONTRACT: %ProgramData%\SF Tecnologias\data\)
    New-Item -Path $DataDir -ItemType Directory -Force | Out-Null
    
    # Criar subdiretorios de dados
    $backupsDir = Join-Path $DataDir "backups"
    $logsDir = Join-Path $DataDir "logs"
    New-Item -Path $backupsDir -ItemType Directory -Force | Out-Null
    New-Item -Path $logsDir -ItemType Directory -Force | Out-Null
    
    # Criar diretorios do updater
    New-Item -Path $ConfigDir -ItemType Directory -Force | Out-Null
    New-Item -Path $BackupsDir -ItemType Directory -Force | Out-Null
    New-Item -Path $LogsDir -ItemType Directory -Force | Out-Null
    New-Item -Path $UpdatesDir -ItemType Directory -Force | Out-Null
    New-Item -Path (Join-Path $UpdatesDir "temp") -ItemType Directory -Force | Out-Null
    
    # Migrar banco de local antigo se existir (compatibilidade)
    $oldDb = Join-Path $OldDataDir "SFTecnologias.db"
    $newDb = Join-Path $DataDir "SFTecnologias.db"
    if ((Test-Path $oldDb) -and -not (Test-Path $newDb)) {
        Write-Host "  Migrando banco de dados do local antigo..." -ForegroundColor Gray
        Copy-Item -Path $oldDb -Destination $newDb -Force
        # Copiar WAL/SHM se existirem
        $oldWal = Join-Path $OldDataDir "SFTecnologias.db-wal"
        $oldShm = Join-Path $OldDataDir "SFTecnologias.db-shm"
        if (Test-Path $oldWal) { Copy-Item -Path $oldWal -Destination "$newDb-wal" -Force }
        if (Test-Path $oldShm) { Copy-Item -Path $oldShm -Destination "$newDb-shm" -Force }
        Show-Ok "Banco migrado para $DataDir"
    }
    
    Show-Ok "Arquivos instalados"
    
    # Passo 5: Verificar/instalar SF.Updater.exe
    $step++
    Show-Step $step $totalSteps "Verificando SF.Updater.exe..."
    
    $updaterExe = Join-Path $InstallDir "SF.Updater.exe"
    if (-not (Test-Path $updaterExe)) {
        # Try to find updater in dist directory
        $updaterSource = Join-Path $SourceDir "updater\SF.Updater.exe"
        if (Test-Path $updaterSource) {
            Copy-Item -Path $updaterSource -Destination $InstallDir -Force
            Show-Ok "SF.Updater.exe instalado"
        } else {
            Show-Warn "SF.Updater.exe nao encontrado. O updater deve ser compilado primeiro."
            Show-Info "Execute: dotnet publish src\updater\SF.Updater.csproj -c Release -r win-x64 --self-contained -o dist\updater"
        }
    } else {
        Show-Ok "SF.Updater.exe ja instalado"
    }
    
    # Passo 6: Criar atalhos
    $step++
    Show-Step $step $totalSteps "Criando atalhos..."
    
    # Atalho da Area de Trabalho
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($DesktopShortcut)
    $shortcut.TargetPath = Join-Path $InstallDir "SF Tecnologias BETA.exe"
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = "SF Tecnologias - Sistema Empresarial"
    $shortcut.Save()
    Show-Ok "Atalho criado na Area de Trabalho"
    
    # Atalho no Menu Iniciar
    New-Item -Path $StartMenuDir -ItemType Directory -Force | Out-Null
    $startMenuShortcut = Join-Path $StartMenuDir "$AppName.lnk"
    $shortcut2 = $shell.CreateShortcut($startMenuShortcut)
    $shortcut2.TargetPath = Join-Path $InstallDir "SF Tecnologias BETA.exe"
    $shortcut2.WorkingDirectory = $InstallDir
    $shortcut2.Description = "SF Tecnologias - Sistema Empresarial"
    $shortcut2.Save()
    Show-Ok "Atalho criado no Menu Iniciar"
    
    # Passo 7: Criar desinstalador
    $step++
    Show-Step $step $totalSteps "Criando desinstalador..."
    
    $uninstallScript = @"
#Requires -RunAsAdministrator
`$ErrorActionPreference = "Stop"
Write-Host "Removendo $AppName..." -ForegroundColor Yellow

# Parar app
`$proc = Get-Process -Name "SF Tecnologias BETA" -ErrorAction SilentlyContinue
if (`$proc) { `$proc | Stop-Process -Force }

# Parar e remover servico
if (Get-Service -Name "$ServiceName" -ErrorAction SilentlyContinue) {
    Stop-Service -Name "$ServiceName" -Force -ErrorAction SilentlyContinue
    & cmd /c "sc.exe delete $ServiceName 2>&1" | Out-Null
}

# Remover atalhos
Remove-Item -Path "$DesktopShortcut" -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$StartMenuDir" -Recurse -Force -ErrorAction SilentlyContinue

# Remover dados (opcional) - ANTES de remover os arquivos
`$response = Read-Host "Deseja remover dados de configuracao? (S/N)"
if (`$response -eq "S" -or `$response -eq "s") {
    Remove-Item -Path "$DataDir" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Dados removidos" -ForegroundColor Green
} else {
    Write-Host "Dados preservados em $DataDir" -ForegroundColor Gray
}

Write-Host "$AppName removido. Arquivos em $InstallDir serao limpos apos esta janela fechar." -ForegroundColor Green
Write-Host "Pressione Enter para sair e concluir a limpeza..."
Read-Host | Out-Null

# Self-delete: este script roda DENTRO de $InstallDir, entao agenda a limpeza
# via cmd que espera o processo atual terminar antes de remover o diretorio
`$self = `$MyInvocation.MyCommand.Path
`$parentDir = Split-Path -Parent `$self
`$tempCmd = [System.IO.Path]::GetTempFileName() + ".cmd"
`$cmdContent = @(
    'ping -n 3 127.0.0.1 >nul'
    "rmdir /s /q `"`$parentDir`""
    "del `"`$tempCmd`""
) -join "`r`n"
Set-Content -Path `$tempCmd -Value `$cmdContent -Encoding ASCII
Start-Process -FilePath "cmd.exe" -ArgumentList "/c", "`"`$tempCmd`"" -WindowStyle Hidden
"@

    $uninstallPath = Join-Path $InstallDir "Desinstalar.bat"
    Set-Content -Path $uninstallPath -Value $uninstallScript -Encoding ASCII
    Show-Ok "Desinstalador criado"
    
    # Resultado final
    Show-Done "INSTALACAO CONCLUIDA!"
    Write-Host ""
    Write-Host "  Local:    $InstallDir" -ForegroundColor White
    Write-Host "  Dados:    $DataDir" -ForegroundColor White
    Write-Host "  Atalho:   Area de Trabalho" -ForegroundColor White
    Write-Host ""
    Write-Host "  Para iniciar: clique no atalho ou execute:" -ForegroundColor White
    Write-Host "    `"$InstallDir\SF Tecnologias BETA.exe`"" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Para configurar como servico Windows:" -ForegroundColor White
    Write-Host "    .\Setup-SF.ps1 service-install" -ForegroundColor Cyan
    Write-Host ""
}

# ============================================
# DESINSTALACAO
# ============================================
function Uninstall-App {
    Show-Header "DESINSTALACAO"
    
    Write-Host "ATENCAO: Isso ira remover o $AppName deste computador." -ForegroundColor Yellow
    Write-Host ""
    $confirm = Read-Host "Digite 'SIM' para confirmar"
    
    if ($confirm -ne "SIM") {
        Write-Host "Operacao cancelada." -ForegroundColor Gray
        return
    }
    
    Write-Host ""
    Write-Host "[1/5] Parando aplicacao..." -ForegroundColor Yellow
    $proc = Get-Process -Name "SF Tecnologias BETA" -ErrorAction SilentlyContinue
    if ($proc) {
        $proc | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
    Show-Ok "Aplicacao parada"
    
    Write-Host "[2/5] Parando updater..." -ForegroundColor Yellow
    $updaterProc = Get-Process -Name "SF.Updater" -ErrorAction SilentlyContinue
    if ($updaterProc) {
        $updaterProc | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
    Show-Ok "Updater parado"
    
    Write-Host "[3/5] Removendo servico..." -ForegroundColor Yellow
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        & cmd /c "sc.exe delete $ServiceName 2>&1" | Out-Null
        Show-Ok "Servico removido"
    } else {
        Show-Ok "Nenhum servico encontrado"
    }
    
    Write-Host "[4/5] Removendo atalhos..." -ForegroundColor Yellow
    Remove-Item -Path "$DesktopShortcut" -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "$StartMenuDir" -Recurse -Force -ErrorAction SilentlyContinue
    Show-Ok "Atalhes removidos"
    
    Write-Host "[5/5] Removendo arquivos..." -ForegroundColor Yellow
    if (Test-Path $InstallDir) {
        # Se este script esta rodando de DENTRO do InstallDir, agendar remocao
        $scriptPath = $MyInvocation.MyCommand.Path
        $scriptDir = if ($scriptPath) { Split-Path -Parent $scriptPath } else { "" }
        $runningInsideInstall = $scriptDir -and $scriptDir.StartsWith($InstallDir, [System.StringComparison]::OrdinalIgnoreCase)

        if ($runningInsideInstall) {
            $tempCmd = [System.IO.Path]::GetTempFileName() + ".cmd"
            $cmdContent = @(
                'ping -n 3 127.0.0.1 >nul'
                "rmdir /s /q `"$InstallDir`""
                "del `"$tempCmd`""
            ) -join "`r`n"
            Set-Content -Path $tempCmd -Value $cmdContent -Encoding ASCII
            Start-Process -FilePath "cmd.exe" -ArgumentList "/c", "`"$tempCmd`"" -WindowStyle Hidden
            Show-Ok "Arquivos serao removidos apos esta janela fechar"
        } else {
            Remove-Item -Path $InstallDir -Recurse -Force
            Show-Ok "Arquivos removidos"
        }
    } else {
        Show-Ok "Nenhum arquivo encontrado"
    }
    
    Show-Done "DESINSTALACAO CONCLUIDA!"
    Write-Host ""
    Write-Host "  Dados preservados em: $DataDir" -ForegroundColor White
    Write-Host "  O banco de dados NAO foi removido." -ForegroundColor White
    Write-Host "  Para remover dados, delete manualmente: $DataDir" -ForegroundColor Gray
    Write-Host ""
}

# ============================================
# SERVICO WINDOWS
# ============================================
function Install-Service {
    Show-Header "REGISTRAR COMO SERVICO"
    
    Write-Host "Isso registra a API como servico Windows que inicia automaticamente." -ForegroundColor Cyan
    Write-Host "O app desktop continuara funcionando normalmente." -ForegroundColor Cyan
    Write-Host ""
    
    # Verificar se a API esta nas pastas do app
    $apiExe = Join-Path $InstallDir "resources\api\SFTecnologiasApi.exe"
    $apiExeAlt = Join-Path $ApiSource "SFTecnologiasApi.exe"
    
    if (Test-Path $apiExe) {
        $apiPath = $apiExe
    } elseif (Test-Path $apiExeAlt) {
        $apiPath = $apiExeAlt
    } else {
        Show-Fail "API (SFTecnologiasApi.exe) nao encontrada"
        Show-Info "Verifique se a API foi publicada em dist/api/"
        return
    }
    
    Write-Host "API encontrada: $apiPath" -ForegroundColor Gray
    
    # Remover servico existente
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Show-Warn "Servico existente encontrado. Removendo..."
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        & cmd /c "sc.exe delete $ServiceName 2>&1" | Out-Null
        Start-Sleep -Seconds 2
    }
    
    # Criar servico
    Write-Host "Registrando servico..." -ForegroundColor Yellow
    try {
        $apiDir = Split-Path $apiPath -Parent
        
        # INSTALL CONTRACT: Database path via environment variable
        $dbPath = Join-Path $DataDir "SFTecnologias.db"
        
        # Ensure data directory exists for the service
        New-Item -Path $DataDir -ItemType Directory -Force | Out-Null
        
        New-Service -Name $ServiceName `
            -BinaryPathName "`"$apiPath`"" `
            -DisplayName $ServiceDisplayName `
            -StartupType Automatic `
            -ErrorAction Stop | Out-Null
        
        # Configurar variaveis de ambiente do servico via Registro
        # IMPORTANTE: O servico precisa saber onde esta o banco
        $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
        $envVars = "ASPNETCORE_ENVIRONMENT=Production;ASPNETCORE_URLS=http://localhost:5000;DatabaseProvider=SQLite;ConnectionStrings__DefaultConnection=Data Source=$dbPath"
        
        # Definir ambiente via ImagePath (flag --environment ou via registro)
        # Usa sc.exe para configurar ambiente do servico
        & cmd /c "sc.exe description $ServiceName `"$ServiceDescription`" 2>&1" | Out-Null
        & cmd /c "sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 2>&1" | Out-Null
        
        # Configurar variaveis de ambiente via registro do servico
        # Isso e necessario porque New-Service nao suporta variaveis de ambiente
        try {
            $currentEnv = (Get-ItemProperty -Path $regPath -Name "Environment" -ErrorAction SilentlyContinue).Environment

            # JWT secret: preserva existente ou gera um novo (min 32 chars) em %ProgramData%
            $jwtSecretFile = Join-Path $DataDir "jwt.secret"
            $jwtSecret = $null
            if ($currentEnv) {
                foreach ($line in @($currentEnv)) {
                    if ($line -like "Jwt__Secret=*") {
                        $jwtSecret = $line.Substring("Jwt__Secret=".Length)
                        break
                    }
                }
            }
            if (-not $jwtSecret -and (Test-Path $jwtSecretFile)) {
                $jwtSecret = (Get-Content -Path $jwtSecretFile -Raw -ErrorAction SilentlyContinue)
                if ($jwtSecret) { $jwtSecret = $jwtSecret.Trim() }
            }
            if (-not $jwtSecret -or $jwtSecret.Length -lt 32) {
                $rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
                try {
                    $bytes = New-Object byte[] 48
                    $rng.GetBytes($bytes)
                    $jwtSecret = [Convert]::ToBase64String($bytes)
                } finally {
                    $rng.Dispose()
                }
                Set-Content -Path $jwtSecretFile -Value $jwtSecret -Encoding ASCII -NoNewline
                try {
                    icacls $jwtSecretFile /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" "Administrators:(OI)(CI)F" | Out-Null
                } catch {}
            }

            $newEnv = @(
                "ASPNETCORE_ENVIRONMENT=Production",
                "ASPNETCORE_URLS=http://localhost:5000",
                "DatabaseProvider=SQLite",
                "ConnectionStrings__DefaultConnection=Data Source=$dbPath",
                "Jwt__Secret=$jwtSecret"
            )
            Set-ItemProperty -Path $regPath -Name "Environment" -Value $newEnv -ErrorAction Stop
            Show-Ok "Variaveis de ambiente configuradas (inclui Jwt__Secret)"
        } catch {
            Show-Warn "Nao foi possivel configurar variaveis de ambiente via registro: $($_.Exception.Message)"
            Show-Info "O servico precisa de Jwt__Secret para iniciar em Production"
        }
        
        Show-Ok "Servico registrado"
        
        # Iniciar servico
        Write-Host "Iniciando servico..." -ForegroundColor Yellow
        Start-Service -Name $ServiceName -ErrorAction Stop
        
        # Aguardar API
        $ready = $false
        for ($i = 1; $i -le 15; $i++) {
            Start-Sleep -Seconds 1
            try {
                $r = Invoke-WebRequest -Uri "http://localhost:5000/health" -TimeoutSec 2 -ErrorAction Stop
                if ($r.StatusCode -eq 200) { $ready = $true; break }
            } catch {}
        }
        
        if ($ready) {
            Show-Ok "API iniciada e respondendo"
        } else {
            Show-Warn "Servico iniciado, mas API ainda nao respondeu"
        }
        
    } catch {
        Show-Fail "Falha ao registrar servico: $($_.Exception.Message)"
        return
    }
    
    Show-Done "SERVICO REGISTRADO!"
    Write-Host ""
    Write-Host "  Servico: $ServiceName" -ForegroundColor White
    Write-Host "  Status:  Iniciado" -ForegroundColor White
    Write-Host "  Modo:    Inicio automatico com o Windows" -ForegroundColor White
    Write-Host ""
    Write-Host "  Para remover: .\Setup-SF.ps1 service-uninstall" -ForegroundColor Cyan
    Write-Host ""
}

function Uninstall-Service {
    Show-Header "REMOVER SERVICO"
    
    if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
        Show-Info "Servico nao esta registrado"
        return
    }
    
    Write-Host "Parando servico..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    
    Write-Host "Removendo servico..." -ForegroundColor Yellow
    & cmd /c "sc.exe delete $ServiceName 2>&1" | Out-Null
    Start-Sleep -Seconds 2
    
    Show-Done "SERVICO REMOVIDO!"
    Write-Host ""
}

# ============================================
# STATUS
# ============================================
function Show-Status {
    Show-Header "STATUS DO SISTEMA"
    
    # Verificar instalacao
    Write-Host "INSTALACAO" -ForegroundColor Cyan
    if (Test-Path $InstallDir) {
        Show-Ok "App instalado em: $InstallDir"
        $size = (Get-ChildItem -Path $InstallDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
        $sizeMB = [math]::Round($size / 1MB, 1)
        Show-Info "Tamanho: $sizeMB MB"
    } else {
        Show-Warn "App nao instalado"
    }
    
    if (Test-Path $DataDir) {
        Show-Ok "Dados em: $DataDir"
        if (Test-Path "$DataDir\SFTecnologias.db") {
            $dbSize = (Get-Item "$DataDir\SFTecnologias.db").Length
            $dbSizeKB = [math]::Round($dbSize / 1KB, 1)
            Show-Info "Banco de dados: $dbSizeKB KB"
        } else {
            Show-Warn "Banco de dados nao encontrado em $DataDir"
        }
        # Verificar subdiretorios
        if (Test-Path "$DataDir\backups") {
            $backupCount = (Get-ChildItem "$DataDir\backups" -File -ErrorAction SilentlyContinue).Count
            Show-Info "Backups: $backupCount arquivo(s)"
        }
    } else {
        Show-Warn "Diretorio de dados nao encontrado: $DataDir"
    }
    
    Write-Host ""
    Write-Host "APLICACAO" -ForegroundColor Cyan
    $proc = Get-Process -Name "SF Tecnologias BETA" -ErrorAction SilentlyContinue
    if ($proc) {
        Show-Ok "App em execucao (PID: $($proc.Id))"
    } else {
        Show-Warn "App nao esta em execucao"
    }
    
    Write-Host ""
    Write-Host "UPDATER" -ForegroundColor Cyan
    $updaterExe = Join-Path $InstallDir "SF.Updater.exe"
    if (Test-Path $updaterExe) {
        Show-Ok "SF.Updater.exe instalado"
        $updaterProc = Get-Process -Name "SF.Updater" -ErrorAction SilentlyContinue
        if ($updaterProc) {
            Show-Info "Updater em execucao (PID: $($updaterProc.Id))"
        }
    } else {
        Show-Warn "SF.Updater.exe nao encontrado"
    }
    
    # Verificar estado da ultima atualizacao
    $updaterState = Join-Path $ConfigDir "updater-state.json"
    if (Test-Path $updaterState) {
        $state = Get-Content $updaterState -Raw | ConvertFrom-Json
        Show-Info "Ultima atualizacao: $($state.version) em $($state.timestamp)"
        Show-Info "Estado: $($state.status)"
    }
    
    Write-Host ""
    Write-Host "SERVICO API" -ForegroundColor Cyan
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Host "  Servico: $ServiceName" -ForegroundColor White
        Write-Host "  Status:  $($svc.Status)" -ForegroundColor $(if ($svc.Status -eq 'Running') { 'Green' } else { 'Yellow' })
        Write-Host "  Tipo:    $($svc.StartType)" -ForegroundColor White
        
        if ($svc.Status -eq 'Running') {
            try {
                $r = Invoke-WebRequest -Uri "http://localhost:5000/health" -TimeoutSec 5 -ErrorAction Stop
                Show-Ok "API respondendo (HTTP $($r.StatusCode))"
            } catch {
                Show-Warn "API nao respondendo"
            }
        }
    } else {
        Show-Info "Servico nao registrado (app gerencia API internamente)"
    }
    
    Write-Host ""
    Write-Host "DEPENDENCIAS" -ForegroundColor Cyan
    if (Test-VCRedist) {
        Show-Ok "VC++ Redistributable"
    } else {
        Show-Warn "VC++ Redistributable NAO encontrado"
        Show-Info "Execute: .\Setup-SF.ps1 install"
    }
    
    Write-Host ""
}

# ============================================
# MENU PRINCIPAL
# ============================================
function Show-Menu {
    Show-Header
    Write-Host "  Selecione uma opcao:" -ForegroundColor White
    Write-Host ""
    Write-Host "  1. Instalar $AppName" -ForegroundColor Cyan
    Write-Host "  2. Desinstalar $AppName" -ForegroundColor Cyan
    Write-Host "  3. Ver Status" -ForegroundColor Cyan
    Write-Host "  4. Registrar API como Servico Windows" -ForegroundColor DarkGray
    Write-Host "  5. Remover Servico Windows" -ForegroundColor DarkGray
    Write-Host "  0. Sair" -ForegroundColor Gray
    Write-Host ""
    
    $choice = Read-Host "Opcao"
    
    switch ($choice) {
        "1" { Install-App }
        "2" { Uninstall-App }
        "3" { Show-Status }
        "4" { Install-Service }
        "5" { Uninstall-Service }
        "0" { exit }
        default { Show-Menu }
    }
}

# ============================================
# PONTO DE ENTRADA
# ============================================
$Action = $args[0]

switch ($Action) {
    "install"          { Install-App }
    "uninstall"        { Uninstall-App }
    "status"           { Show-Status }
    "service-install"  { Install-Service }
    "service-uninstall" { Uninstall-Service }
    default            { Show-Menu }
}
