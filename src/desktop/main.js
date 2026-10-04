const { app, BrowserWindow, ipcMain, session } = require('electron')
const path = require('node:path')
const fs = require('node:fs')
const http = require('node:http')
const { URL } = require('node:url')
const { spawn } = require('node:child_process')
const systems = require('./systems/system-manager')
const releaseSource = require('./systems/release-source')
const isDev = !app.isPackaged && process.env.NODE_ENV !== 'production'
const logFile = path.join(app.getPath('userData'), 'debug.log')

function log(msg) {
  const line = `[${new Date().toISOString()}] ${msg}\n`
  try { fs.appendFileSync(logFile, line) } catch {}
  console.log(line.trim())
}

// O System Manager usa o mesmo log (arquivo + console) da plataforma
systems.initLogging(log)

function sanitizeForLog(str) {
  if (!str) return str
  try {
    const obj = JSON.parse(str)
    const walk = (v) => {
      if (v && typeof v === 'object') {
        for (const k of Object.keys(v)) {
          if (/senha|password|token|secret|accessToken/i.test(k) && typeof v[k] === 'string') {
            v[k] = '***'
          } else {
            walk(v[k])
          }
        }
      }
      return v
    }
    return JSON.stringify(walk(obj))
  } catch {
    return str.replace(/("senha"|"password"|"token"|"secret"|"accessToken"\s*:\s*")[^"]+/gi, '$1***')
  }
}

app.setName('SF Tecnologias BETA')

const API_URL = process.env.VITE_API_URL || 'http://localhost:5000'
const DEFAULT_TIMEOUT = parseInt(process.env.REQUEST_TIMEOUT || '10000', 10)
if (isNaN(DEFAULT_TIMEOUT) || DEFAULT_TIMEOUT <= 0) {
  // fallback to 10 seconds if env var is invalid
}

// ============================================
// INSTALL CONTRACT: Data directory resolution
// ============================================
// Binaries: %ProgramFiles%\SF Tecnologias\
// Data:     %ProgramData%\SF Tecnologias\data\
// The data directory is NEVER touched by the updater.
function getDataDir() {
  if (isDev) {
    // Development: use Electron userData
    return app.getPath('userData')
  }
  // Production: use %ProgramData%\SF Tecnologias\data
  const programData = process.env.ProgramData || path.join(process.env.SystemDrive || 'C:', 'ProgramData')
  return path.join(programData, 'SF Tecnologias', 'data')
}

function ensureDataDir() {
  const dataDir = getDataDir()
  if (!fs.existsSync(dataDir)) {
    fs.mkdirSync(dataDir, { recursive: true })
    log('[DATA] Created data directory: ' + dataDir)
  }
  
  // INSTALL CONTRACT: Migrate database from old location on first run
  // Old location: %LOCALAPPDATA%\SF Tecnologias\SFTecnologias.db
  // New location: %ProgramData%\SF Tecnologias\data\SFTecnologias.db
  if (!isDev) {
    const newDb = path.join(dataDir, 'SFTecnologias.db')
    if (!fs.existsSync(newDb)) {
      const oldDataDir = path.join(app.getPath('userData'), '..')
      const oldDbCandidates = [
        path.join(app.getPath('userData'), 'SFTecnologias.db'),
        path.join(oldDataDir, 'SFTecnologias.db')
      ]
      for (const oldDb of oldDbCandidates) {
        if (fs.existsSync(oldDb)) {
          try {
            fs.copyFileSync(oldDb, newDb)
            // Also copy WAL and SHM if they exist
            const oldWal = oldDb + '-wal'
            const oldShm = oldDb + '-shm'
            if (fs.existsSync(oldWal)) fs.copyFileSync(oldWal, newDb + '-wal')
            if (fs.existsSync(oldShm)) fs.copyFileSync(oldShm, newDb + '-shm')
            log('[DATA] Migrated database from: ' + oldDb)
          } catch (err) {
            log('[DATA] WARNING: Failed to migrate database: ' + err.message)
          }
          break
        }
      }
    }
  }
  
  return dataDir
}

let accessToken = null
let apiProcess = null
let apiReady = false
// Sistema ativo resolvido pelo System Contract (null = layout legado)
let activeSystem = null

// Get the path to the API executable
function getApiPath() {
  if (app.isPackaged) {
    // In packaged app, API is in resources/api/
    return path.join(process.resourcesPath, 'api', 'SFTecnologiasApi.exe')
  }
  // In development, use dotnet run
  return null
}

// Get the path to the Updater executable
function getUpdaterPath() {
  if (app.isPackaged) {
    // In packaged app, Updater is in resources/updater/
    return path.join(process.resourcesPath, 'updater', 'SF.Updater.exe')
  }
  // In development, look in dist/updater/
  return path.join(__dirname, '../../dist/updater/SF.Updater.exe')
}

// Get current version from version.json
function getCurrentVersion() {
  try {
    let versionPath
    if (app.isPackaged) {
      versionPath = path.join(process.resourcesPath, 'app', 'version.json')
    } else {
      versionPath = path.join(__dirname, 'version.json')
    }
    if (fs.existsSync(versionPath)) {
      const data = JSON.parse(fs.readFileSync(versionPath, 'utf8'))
      return data.version || '0.0.0'
    }
  } catch (err) {
    log('[UPDATE] Error reading version: ' + err.message)
  }
  return '0.0.0'
}

// GitHub repository configuration for update checks
const UPDATE_CONFIG = {
  checkIntervalMs: 60 * 60 * 1000 // Check every hour
}

// Check for updates from GitHub Releases (mesma fonte usada pelos sistemas)
async function checkForUpdates() {
  const currentVersion = getCurrentVersion()
  log('[UPDATE] Current version: ' + currentVersion)

  const releaseInfo = await releaseSource.fetchLatestRelease()
  if (!releaseInfo.ok) {
    // Falha ao consultar: registra o problema e NAO bloqueia a execucao local.
    log('[UPDATE] Falha ao consultar release: ' + releaseInfo.error)
    return { available: false, offline: releaseInfo.offline === true, error: releaseInfo.error, currentVersion }
  }

  const latestVersion = releaseInfo.latestVersion
  log('[UPDATE] Latest version: ' + latestVersion)

  if (systems.compareVersions(latestVersion, currentVersion) > 0) {
    return {
      available: true,
      currentVersion,
      latestVersion,
      manifestUrl: releaseInfo.manifestUrl,
      releaseNotes: releaseInfo.releaseNotes,
      releaseUrl: releaseInfo.releaseUrl
    }
  }

  return { available: false, currentVersion, latestVersion }
}

// ============================================
// INSTALACAO / ATUALIZACAO INDIVIDUAL DE SISTEMAS (Fase 3 e 4)
// ============================================
const SYSTEM_ID_RE = /^[a-z0-9]+(-[a-z0-9]+)*$/
const UPDATER_TIMEOUT_MS = 15 * 60 * 1000

// Consulta o release e localiza a entrada do sistema solicitado.
// Sem rede/manifesto: { ok:false, offline:true } — nada e instalado e nada e parado.
async function resolveSystemRelease(systemId) {
  const res = await releaseSource.getReleaseManifest()
  if (!res.ok) {
    return { ok: false, offline: res.offline === true, error: res.error || 'release indisponivel' }
  }

  const entry = releaseSource.findSystemEntry(res.manifest, systemId)
  if (!entry) {
    return {
      ok: false,
      offline: false,
      error: `sistema "${systemId}" nao consta no release ${res.release.latestVersion}`
    }
  }

  return {
    ok: true,
    entry,
    releaseVersion: entry.version,
    manifestUrl: res.release.manifestUrl,
    releaseVersionPlatform: res.manifest.version,
    releaseTag: res.release.latestVersion
  }
}

// Program Files exige administracao. Quando o processo nao consegue gravar no
// installDir (instalacao real), o updater roda elevado (UAC); com gravacao
// liberada roda direto (testes E2E e instalacao em pasta do usuario).
function spawnUpdaterProcess(updaterPath, updaterArgs, installDir) {
  if (systems.canWriteInstallDir(installDir)) {
    return { child: spawn(updaterPath, updaterArgs, { stdio: 'ignore' }), elevated: false }
  }

  const q = (s) => "'" + String(s).replace(/'/g, "''") + "'"
  const argList = updaterArgs.map(q).join(', ')
  const script = 'try { $p = Start-Process -FilePath ' + q(updaterPath) +
    ' -ArgumentList @(' + argList + ')' +
    ' -Verb RunAs -Wait -PassThru -WindowStyle Hidden; exit $p.ExitCode }' +
    ' catch { Write-Output $_.Exception.Message; exit 1 }'
  log('[UPDATER] Sem permissao de escrita em ' + installDir + ' - executando elevado (UAC)')
  return {
    child: spawn('powershell.exe', [
      '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', script
    ], { stdio: 'ignore' }),
    elevated: true
  }
}

// Executa o SF.Updater com ALVO de sistema. Nao mexe em Desktop/API/Updater da plataforma.
function runSystemUpdater(systemId, manifestUrl) {
  const updaterPath = getUpdaterPath()
  if (!updaterPath || !fs.existsSync(updaterPath)) {
    log('[UPDATER] SF.Updater.exe nao encontrado em: ' + updaterPath)
    return Promise.resolve({ success: false, error: 'Updater not found' })
  }

  const installDir = app.isPackaged ? path.dirname(process.execPath) : systems.getPlatformRoot()

  log('[UPDATER] Instalando/atualizando sistema: ' + systemId)
  log('[UPDATER] Manifesto: ' + manifestUrl)

  return new Promise((resolve) => {
    let finished = false
    const finish = (result) => { if (!finished) { finished = true; resolve(result) } }

    let child
    try {
      child = spawnUpdaterProcess(updaterPath, [
        '--manifest', manifestUrl || '',
        '--system', systemId,
        '--install-dir', installDir,
        '--data-dir', getDataDir()
      ], installDir).child
    } catch (err) {
      log('[UPDATER] Falha ao iniciar: ' + err.message)
      return finish({ success: false, error: err.message })
    }

    const timer = setTimeout(() => {
      log('[UPDATER] TIMEOUT: atualizador excedeu ' + UPDATER_TIMEOUT_MS + 'ms')
      try { child.kill() } catch { /* ja finalizado */ }
      finish({ success: false, error: 'timeout' })
    }, UPDATER_TIMEOUT_MS)

    child.on('error', (err) => {
      clearTimeout(timer)
      log('[UPDATER] Erro de processo: ' + err.message)
      finish({ success: false, error: err.message })
    })

    child.on('exit', (code) => {
      clearTimeout(timer)
      log('[UPDATER] Finalizado com codigo ' + code)
      finish({ success: code === 0, exitCode: code })
    })
  })
}

// Instala (se ausente) ou atualiza (se desatualizado) UM sistema.
// Regras: nao baixa quando a versao local ja e a disponivel; download so acontece
// depois de um manifesto valido; falha de release nunca bloqueia o uso local.
async function installSystem(systemId) {
  if (!SYSTEM_ID_RE.test(systemId || '')) {
    return { success: false, error: 'id de sistema invalido' }
  }

  const local = systems.getSystemStatus(systemId)
  const release = await resolveSystemRelease(systemId)

  if (!release.ok) {
    systems.recordSystemUpdateCheck(systemId, release.offline ? 'offline' : 'unavailable', release.error)
    log('[SYSTEM] Release indisponivel para ' + systemId + ': ' + release.error +
        ' (execucao local nao bloqueada)')
    return {
      success: false,
      offline: !!release.offline,
      status: local.status,
      installedVersion: local.installedVersion,
      error: release.error
    }
  }

  const status = systems.getSystemStatus(systemId, { version: release.releaseVersion })
  systems.recordSystemState(systemId, status.status, { releaseVersion: release.releaseVersion })
  systems.recordSystemUpdateCheck(systemId, 'checked', { releaseVersion: release.releaseVersion })

  if (status.status === systems.SYSTEM_STATUS.ATUALIZADO) {
    log('[SYSTEM] ' + systemId + ' ja esta na versao ' + status.installedVersion + ' — nenhum download')
    return {
      success: true,
      downloaded: false,
      status: status.status,
      installedVersion: status.installedVersion,
      releaseVersion: release.releaseVersion
    }
  }

  // Para apenas o sistema afetado (se for o ativo); o restante da plataforma segue.
  const wasActive = activeSystem && activeSystem.id === systemId
  if (wasActive) {
    log('[SYSTEM] Parando API do sistema ativo antes da troca: ' + systemId)
    stopApi()
  }

  const run = await runSystemUpdater(systemId, release.manifestUrl)

  if (!run.success) {
    log('[SYSTEM] Instalacao/atualizacao de ' + systemId + ' falhou: ' + (run.error || ('exit ' + run.exitCode)))
    if (wasActive) await startApi()
    const afterFailure = systems.getSystemStatus(systemId, { version: release.releaseVersion })
    return {
      success: false,
      status: afterFailure.status,
      installedVersion: afterFailure.installedVersion,
      releaseVersion: release.releaseVersion,
      error: run.error || ('updater exit ' + run.exitCode)
    }
  }

  const after = systems.getSystemStatus(systemId, { version: release.releaseVersion })
  if (after.present) {
    if (after.valid) {
      if (after.manifest) systems.ensureSystemData({ id: systemId, manifest: after.manifest })
      systems.recordSystemInstalled(systemId, after.installedVersion, after.dir)
    }
    systems.recordSystemState(systemId, after.status, {
      releaseVersion: release.releaseVersion,
      location: after.dir
    })
  }

  if (wasActive) {
    log('[SYSTEM] Reiniciando API do sistema ' + systemId)
    await startApi()
  }

  log('[SYSTEM] ' + systemId + ' instalado/atualizado para ' + after.installedVersion)
  return {
    success: true,
    downloaded: true,
    status: after.status,
    valid: after.valid,
    installedVersion: after.installedVersion,
    releaseVersion: release.releaseVersion
  }
}

// Status de todos os sistemas conhecidos (locais + catalogo do release, se houver).
async function listSystems(includeRelease) {
  const ids = new Set(systems.listPresentSystemIds())
  for (const id of Object.keys((systems.loadSystemsRegistry().systems) || {})) ids.add(id)

  let releaseInfo = { ok: false, offline: true }
  if (includeRelease) {
    releaseInfo = await releaseSource.getReleaseManifest()
    if (releaseInfo.ok) {
      for (const entry of releaseSource.listSystemEntries(releaseInfo.manifest)) ids.add(entry.id)
    }
  }

  const result = []
  for (const id of Array.from(ids).sort()) {
    const releaseEntry = releaseInfo.ok ? releaseSource.findSystemEntry(releaseInfo.manifest, id) : null
    const status = systems.getSystemStatus(id, releaseEntry ? { version: releaseEntry.version } : null)
    result.push({
      id,
      name: status.name,
      status: status.status,
      installedVersion: status.installedVersion,
      releaseVersion: releaseEntry ? releaseEntry.version : null,
      location: status.dir,
      source: status.source,
      valid: status.valid,
      errors: status.errors
    })
  }

  return {
    systems: result,
    offline: !releaseInfo.ok,
    releaseError: releaseInfo.ok ? null : releaseInfo.error
  }
}

// Garante o segredo JWT da API em Production.
// A API recusa iniciar em Production sem Jwt__Secret (>= 32 chars). O instalador grava o
// mesmo arquivo quando registra o servico; aqui lemos/criamos no mesmo caminho para que a
// instalacao portatil (API como processo filho) tambem consiga iniciar.
function ensureJwtSecret(dataDir) {
  const secretFile = path.join(dataDir, 'jwt.secret')
  try {
    if (fs.existsSync(secretFile)) {
      const secret = fs.readFileSync(secretFile, 'utf8').trim()
      if (secret && secret.length >= 32) return secret
    }

    const secret = require('node:crypto').randomBytes(48).toString('base64')
    fs.writeFileSync(secretFile, secret, { encoding: 'utf8', mode: 0o600 })
    log('[API] Jwt__Secret criado em ' + secretFile)
    return secret
  } catch (err) {
    log('[API] WARNING: nao foi possivel criar o jwt.secret: ' + err.message)
    return null
  }
}

// Start the API server
async function startApi() {
  // Ja ha uma API saudavel em API_URL (ex.: servico Windows registrado pelo instalador,
  // que aponta para o banco do sistema padrao). Sobir outra instancia falharia com
  // "address already in use" na porta 5000 e o renderer usaria a instancia existente.
  try {
    const probe = await fetch(API_URL + '/health', { signal: AbortSignal.timeout(1500) })
    if (probe.ok) {
      log('[API] API ja em execucao em ' + API_URL + ' - usando instancia existente')
      apiReady = true
      return true
    }
  } catch (err) {
    // nenhuma API respondendo - segue para a instancia local
  }

  // FASE 2 (System Contract): resolver o sistema padrao pelo contrato.
  // Sem sistema valido, cai no layout legado (resources/api + data/SFTecnologias.db).
  let apiExe = getApiPath()
  let dataDir = ensureDataDir()
  let dbPath = path.join(dataDir, 'SFTecnologias.db')

  const system = systems.getDefaultSystem(process.env.SF_SYSTEM_ID || 'h2-conveniencia')
  if (system) {
    const launch = systems.getLaunchInfo(system)
    activeSystem = system
    apiExe = launch.apiExe
    dataDir = launch.dataDir
    dbPath = launch.dbPath
    log('[SYSTEM] Sistema ativo: ' + system.id + ' v' + (system.version ? system.version.version : '?') + ' (' + system.source + ') em ' + system.dir)
  } else {
    activeSystem = null
    log('[SYSTEM] Nenhum sistema valido via contrato — usando layout legado')
  }

  if (!apiExe) {
    // Development mode - assume API is already running or use dotnet run
    log('[API] Development mode - assuming API is running at ' + API_URL)
    return true
  }

  if (!fs.existsSync(apiExe)) {
    log('[API] WARNING: API executable not found at ' + apiExe)
    log('[API] Application will try to connect to external API at ' + API_URL)
    return false
  }

  log('[API] Starting API from: ' + apiExe)
  
  // Set working directory to the API directory
  const apiDir = path.dirname(apiExe)

  // JWT é da PLATAFORMA (login acontece antes do sistema): permanece no data dir
  // da plataforma; os DADOS do sistema ficam no data dir por sistema (contrato).
  const jwtSecret = process.env.Jwt__Secret || ensureJwtSecret(getDataDir())
  
  // Set environment variables for SQLite
  // INSTALL CONTRACT: Database is always in %ProgramData%\SF Tecnologias\data\
  const env = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Production',
    ASPNETCORE_URLS: API_URL,
    DatabaseProvider: 'SQLite',
    ConnectionStrings__DefaultConnection: `Data Source=${dbPath}`
  }
  if (jwtSecret) {
    env.Jwt__Secret = jwtSecret
  } else {
    log('[API] WARNING: Jwt__Secret indisponivel; a API pode nao iniciar em Production')
  }

  try {
    apiProcess = spawn(apiExe, [], {
      cwd: apiDir,
      env: env,
      stdio: ['ignore', 'pipe', 'pipe']
    })

    apiProcess.stdout.on('data', (data) => {
      const msg = data.toString().trim()
      if (msg) log('[API] ' + msg)
    })

    apiProcess.stderr.on('data', (data) => {
      const msg = data.toString().trim()
      if (msg) log('[API] ' + msg)
    })

    apiProcess.on('error', (err) => {
      log('[API] ERROR: ' + err.message)
      apiProcess = null
    })

    apiProcess.on('exit', (code, signal) => {
      log('[API] Process exited with code ' + code + ', signal ' + signal)
      apiProcess = null
      apiReady = false
    })

    log('[API] API process started (PID: ' + apiProcess.pid + ')')
    
    // Wait for API to be ready
    return await waitForApi()
  } catch (err) {
    log('[API] Failed to start API: ' + err.message)
    return false
  }
}

// Wait for API to be ready
async function waitForApi(maxAttempts = 30, intervalMs = 1000) {
  log('[API] Waiting for API to be ready...')
  
  for (let i = 0; i < maxAttempts; i++) {
    try {
      const response = await fetch(API_URL + '/health')
      if (response.ok) {
        log('[API] API is ready!')
        apiReady = true
        return true
      }
    } catch (err) {
      // API not ready yet
    }
    await new Promise(resolve => setTimeout(resolve, intervalMs))
  }
  
  log('[API] WARNING: API did not become ready within timeout')
  return false
}

// Stop the API server
function stopApi() {
  if (apiProcess) {
    log('[API] Stopping API process...')
    try {
      apiProcess.kill()
    } catch (err) {
      log('[API] Error stopping API: ' + err.message)
    }
    apiProcess = null
    apiReady = false
  }
}

function getRendererIndex() {
  // FASE 2: renderer vem do contrato do sistema ativo; fallback = layout legado
  if (activeSystem && activeSystem.manifest.renderer) {
    return path.join(activeSystem.dir, activeSystem.manifest.renderer)
  }
  if (app.isPackaged) {
    return path.join(process.resourcesPath, 'renderer', 'index.html')
  }
  return path.join(__dirname, '../frontend/sf-tecnologias-web/dist/index.html')
}

const gotTheLock = app.requestSingleInstanceLock()

if (!gotTheLock) {
  app.quit()
} else {
  app.on('second-instance', (event, commandLine, workingDirectory) => {
    // Someone tried to run a second instance, we should focus our window.
    if (mainWindow) {
      if (mainWindow.isMinimized()) mainWindow.restore()
      mainWindow.focus()
    }
  })

  function createWindow() {
    const win = new BrowserWindow({
      width: 420,
      height: 580,
      title: 'SF Tecnologias - Login',
      resizable: false,
      maximizable: false,
      webPreferences: {
        preload: path.join(__dirname, 'preload.js'),
        contextIsolation: true,
        nodeIntegration: false,
        webSecurity: true,
        sandbox: true
      }
    })

    // Set CSP headers
    session.defaultSession.webRequest.onHeadersReceived((details, callback) => {
      callback({
        responseHeaders: {
          ...details.responseHeaders,
          'Content-Security-Policy': ["default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self' http://localhost:5000 https://api.github.com; img-src 'self' data: file:; font-src 'self';"]
        }
      })
    })

    // Block navigation to untrusted origins
    win.webContents.on('will-navigate', (event, url) => {
      const parsedUrl = new URL(url)
      const allowedHosts = ['localhost', '127.0.0.1']
      if (!allowedHosts.includes(parsedUrl.hostname)) {
        event.preventDefault()
      }
    })

    // Block new windows
    win.webContents.setWindowOpenHandler(() => {
      return { action: 'deny' }
    })

    if (isDev) {
      win.loadURL('http://localhost:5173')
      win.webContents.openDevTools()
    } else {
      win.loadFile(getRendererIndex())
    }

    return win
  }

  let mainWindow = null

  app.whenReady().then(async () => {
    // Start API server first
    log('[APP] Starting application...')
    const apiStarted = await startApi()
    if (apiStarted) {
      log('[APP] API started successfully')
    } else {
      log('[APP] WARNING: API may not be available')
    }

    mainWindow = createWindow()

    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) {
        mainWindow = createWindow()
      }
    })
  })

  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') {
      stopApi()
      app.quit()
    }
  })

  // Clear token and stop API on app quit
  app.on('before-quit', () => {
    accessToken = null
    stopApi()
  })

  // Ensure API is stopped on exit
  app.on('will-quit', () => {
    stopApi()
  })

  // IPC Handlers

  // Store token
  ipcMain.handle('set-token', (event, token) => {
    accessToken = token
    return true
  })

  // Remove token
  ipcMain.handle('remove-token', (event) => {
    accessToken = null
    return true
  })

  // Get token
  ipcMain.handle('get-token', (event) => {
    return accessToken
  })

  // Login success - resize window to main application
  ipcMain.handle('login-success', (event) => {
    if (mainWindow) {
      mainWindow.setSize(1280, 800)
      mainWindow.center()
      mainWindow.setTitle('SF Tecnologias')
      mainWindow.setResizable(true)
      mainWindow.setMaximizable(true)
    }
    return true
  })

  // Start updater
  ipcMain.handle('start-updater', async (event, manifestUrl) => {
    const updaterPath = getUpdaterPath()
    if (!updaterPath || !fs.existsSync(updaterPath)) {
      log('[UPDATER] SF.Updater.exe not found at: ' + updaterPath)
      return { success: false, error: 'Updater not found' }
    }

    log('[UPDATER] Starting updater: ' + updaterPath)
    log('[UPDATER] Manifest URL: ' + manifestUrl)

    try {
      // Stop API before starting updater
      stopApi()

      // Start the updater
      const updaterProcess = spawn(updaterPath, [
        '--manifest', manifestUrl || '',
        '--install-dir', app.isPackaged ? path.dirname(process.execPath) : process.cwd(),
        '--data-dir', getDataDir()
      ], {
        detached: true,
        stdio: 'ignore'
      })

      updaterProcess.unref()

      log('[UPDATER] Updater started (PID: ' + updaterProcess.pid + ')')

      // Quit the app
      app.quit()

      return { success: true }
    } catch (err) {
      log('[UPDATER] Failed to start updater: ' + err.message)
      return { success: false, error: err.message }
    }
  })

  // Check for updates
  ipcMain.handle('check-for-updates', async () => {
    log('[UPDATE] Manual update check requested')
    return await checkForUpdates()
  })

  // ============================================
  // SISTEMAS INDIVIDUAIS (Fase 3 e 4)
  // ============================================

  // Descoberta: INSTALADO / NAO_INSTALADO / INVALIDO / DESATUALIZADO / ATUALIZADO
  ipcMain.handle('list-systems', async (event, includeRelease) => {
    log('[SYSTEM] Descoberta de sistemas solicitada')
    return await listSystems(includeRelease !== false)
  })

  // Verifica versao local x release SEM baixar nada (e sem parar nada).
  ipcMain.handle('check-system-update', async (event, systemId) => {
    if (!SYSTEM_ID_RE.test(systemId || '')) {
      return { success: false, error: 'id de sistema invalido' }
    }

    const local = systems.getSystemStatus(systemId)
    const release = await resolveSystemRelease(systemId)
    if (!release.ok) {
      systems.recordSystemUpdateCheck(systemId, release.offline ? 'offline' : 'unavailable', release.error)
      return {
        success: false,
        offline: !!release.offline,
        status: local.status,
        installedVersion: local.installedVersion,
        error: release.error
      }
    }

    const status = systems.getSystemStatus(systemId, { version: release.releaseVersion })
    systems.recordSystemState(systemId, status.status, { releaseVersion: release.releaseVersion })
    systems.recordSystemUpdateCheck(systemId, 'checked', { releaseVersion: release.releaseVersion })

    return {
      success: true,
      status: status.status,
      installedVersion: status.installedVersion,
      releaseVersion: release.releaseVersion,
      updateAvailable: status.status === systems.SYSTEM_STATUS.DESATUALIZADO ||
        status.status === systems.SYSTEM_STATUS.NAO_INSTALADO
    }
  })

  // Instala ou atualiza UM sistema (baixa somente quando necessario).
  ipcMain.handle('install-system', async (event, systemId) => {
    log('[SYSTEM] Instalacao/atualizacao solicitada: ' + systemId)
    return await installSystem(systemId)
  })

  // Get current version
  ipcMain.handle('get-version', () => {
    return getCurrentVersion()
  })

  // Auto-check for updates on startup (after 5 seconds)
  let updateCheckTimer = null
  app.whenReady().then(() => {
    // Initial check after 5 seconds
    setTimeout(async () => {
      const result = await checkForUpdates()
      if (result.available && mainWindow) {
        log('[UPDATE] Update available: ' + result.latestVersion)
        // Send update available event to renderer
        mainWindow.webContents.send('update-available', result)
      }
    }, 5000)

    // Periodic check
    updateCheckTimer = setInterval(async () => {
      const result = await checkForUpdates()
      if (result.available && mainWindow) {
        log('[UPDATE] Update available: ' + result.latestVersion)
        mainWindow.webContents.send('update-available', result)
      }
    }, UPDATE_CONFIG.checkIntervalMs)
  })

  // Perform HTTP request with timeout and standardized error handling
  ipcMain.handle('http-request', async (event, options) => {
    if (!options || typeof options !== 'object') {
      return { status: 400, data: null, success: false, error: 'Invalid request options' }
    }

    const { method, url, data, headers = {} } = options

    if (!method || !url || typeof url !== 'string') {
      return { status: 400, data: null, success: false, error: 'Missing method or url' }
    }

    // Validate URL: must be a relative path, no protocol, no path traversal
    if (url.startsWith('http://') || url.startsWith('https://') || url.includes('..')) {
      return { status: 400, data: null, success: false, error: 'Invalid URL: must be a relative path' }
    }

    const validMethods = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH']
    if (!validMethods.includes(method.toUpperCase())) {
      return { status: 400, data: null, success: false, error: 'Invalid HTTP method' }
    }

    const fullUrl = API_URL + url
    const parsedUrl = new URL(fullUrl)
    const bodyStr = data !== undefined ? JSON.stringify(data) : null

    const reqHeaders = {
      'Content-Type': 'application/json',
      ...headers
    }

    if (accessToken) {
      reqHeaders['Authorization'] = 'Bearer ' + accessToken
    }

    log(`[HTTP] ${method.toUpperCase()} ${fullUrl} | hasToken: ${!!accessToken} | tokenLen: ${accessToken ? accessToken.length : 0}`)
    if (data) log(`[HTTP] Body: ${sanitizeForLog(bodyStr)}`)

    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        req.destroy()
        resolve({ status: 408, data: null, success: false, error: 'Request timeout', message: 'The request timed out after ' + DEFAULT_TIMEOUT + 'ms' })
      }, DEFAULT_TIMEOUT)

      const req = http.request({
        hostname: parsedUrl.hostname,
        port: parsedUrl.port,
        path: parsedUrl.pathname + parsedUrl.search,
        method: method.toUpperCase(),
        headers: reqHeaders,
        timeout: DEFAULT_TIMEOUT
      }, (res) => {
        clearTimeout(timer)
        let chunks = []
        res.on('data', (chunk) => chunks.push(chunk))
        res.on('end', () => {
          const rawBody = Buffer.concat(chunks).toString()
          log(`[HTTP] Response: ${res.statusCode}`)
          let responseData = null
          try {
            responseData = JSON.parse(rawBody)
            log(`[HTTP] Parsed JSON: ${sanitizeForLog(JSON.stringify(responseData).substring(0, 500))}`)
          } catch (e) {
            log(`[HTTP] JSON parse failed: ${e.message}`)
            responseData = {}
          }
          resolve({
            status: res.statusCode,
            data: responseData,
            success: res.statusCode >= 200 && res.statusCode < 300,
            headers: res.headers
          })
        })
      })

      req.on('error', (error) => {
        clearTimeout(timer)
        log(`[HTTP] Error: ${error.name}: ${error.message}`)
        if (error.code === 'ECONNREFUSED' || error.code === 'ENOTFOUND' || error.code === 'ECONNRESET') {
          resolve({ status: 503, data: null, success: false, error: 'Network error', message: 'Unable to connect to the server', details: error.message })
        } else {
          resolve({ status: 500, data: null, success: false, error: 'Internal error', message: 'An unexpected error occurred', details: error.message })
        }
      })

      req.on('timeout', () => {
        clearTimeout(timer)
        req.destroy()
        resolve({ status: 408, data: null, success: false, error: 'Request timeout', message: 'The request timed out after ' + DEFAULT_TIMEOUT + 'ms' })
      })

      if (bodyStr) {
        req.write(bodyStr)
      }
      req.end()
    })
  })
}
