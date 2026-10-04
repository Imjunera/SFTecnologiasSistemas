// ============================================
// SF PLATFORM — SYSTEM CONTRACT & MANAGER
// ============================================
// A plataforma define o contrato; todo sistema implementa o contrato.
// Estrutura esperada de um sistema instalado:
//
//   <system-id>/
//   ├── system.json      (identidade, versao, entry point, dados)
//   ├── version.json     (systemId + versao + build)
//   ├── app/
//   │   ├── executable/  (binario de entrada — ex.: API do sistema)
//   │   └── resources/   (renderer/assets do sistema)
//   └── ...
//
// Dados ficam SEMPRE fora do pacote de software:
//   %ProgramData%\SF Tecnologias\<dataDirectory>  (dataDirectory comeca com "Data/")
//
// A plataforma NAO conhece detalhes internos dos sistemas — apenas o contrato.
'use strict'

const electron = (() => {
  try { return require('electron') } catch { return null }
})()

// Em testes/CLI o electron nao existe (require('electron') devolve o caminho do binario).
// O stub deixa o System Manager utilizavel fora do Electron sem mudar o comportamento real.
const app = (electron && electron.app && typeof electron.app.isPackaged === 'boolean')
  ? electron.app
  : {
      isPackaged: false,
      getPath: () => process.env.SF_USER_DATA || require('os').tmpdir()
    }

const fs = require('fs')
const path = require('path')

// Log proprio (arquivo + console); o main.js repassa o seu log em initLogging se quiser.
let logFn = (msg) => console.log(msg)
function initLogging(fn) { if (typeof fn === 'function') logFn = fn }
function log(msg) { logFn(msg) }

// Versao da plataforma (mesmo version.json do desktop)
function getPlatformVersion() {
  if (process.env.SF_PLATFORM_VERSION) return process.env.SF_PLATFORM_VERSION
  try {
    const versionPath = app.isPackaged
      ? path.join(process.resourcesPath, 'app', 'version.json')
      : path.join(__dirname, '../version.json')
    if (fs.existsSync(versionPath)) {
      return JSON.parse(fs.readFileSync(versionPath, 'utf8')).version || '0.0.0'
    }
  } catch (err) { /* ignore */ }
  return '0.0.0'
}

// Raiz de instalacao da plataforma (onde ficam Systems/ e resources/)
function getPlatformRoot() {
  if (process.env.SF_PLATFORM_ROOT) return process.env.SF_PLATFORM_ROOT
  if (app.isPackaged) {
    return path.dirname(process.execPath) // %ProgramFiles%\SF Tecnologias
  }
  // Desenvolvimento: raiz do repositorio (src/desktop/systems -> repo)
  return path.join(__dirname, '../../..')
}

// Onde procurar sistemas instalados. Ordem = prioridade (primeiro vence, sempre).
//   <install>\Systems\          — sistemas instalados/atualizados pelo gerenciador
//   <install>\resources\systems\ — sistema(s) empacotado(s) junto com a plataforma
//   <repo>\dist\systems\         — apenas em desenvolvimento
// A resolucao e deterministica: nenhuma busca por .exe e a mesma copia nunca e
// escolhida duas vezes (uma pasta por id, na raiz de maior prioridade).
const SYSTEM_ROOT_SOURCES = ['installed', 'bundled', 'bundled']

function getSystemsRoots() {
  const platformRoot = getPlatformRoot()
  const roots = [
    path.join(platformRoot, 'Systems'),
    path.join(platformRoot, 'resources', 'systems')
  ]
  if (!app.isPackaged) {
    roots.push(path.join(platformRoot, 'dist', 'systems'))
  }
  return roots
}

function getSystemsRootSources() {
  return getSystemsRoots().map((_, i) => SYSTEM_ROOT_SOURCES[i] || 'bundled')
}

// Raiz de dados da plataforma (%ProgramData%\SF Tecnologias). Binarios e dados sao separados.
function getPlatformDataRoot() {
  if (process.env.SF_DATA_ROOT) return process.env.SF_DATA_ROOT
  if (app.isPackaged || process.env.SF_FORCE_PROGRAMDATA === '1') {
    const programData = process.env.ProgramData || path.join(process.env.SystemDrive || 'C:', 'ProgramData')
    return path.join(programData, 'SF Tecnologias')
  }
  // Desenvolvimento: userData (nao polui ProgramData da maquina do dev)
  return app.getPath('userData')
}


function readJsonSafe(file) {
  try {
    // Remove BOM (arquivos gerados pelo PowerShell 5.1 vem com \uFEFF)
    const content = fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '')
    return JSON.parse(content)
  } catch {
    return null
  }
}

// Comparacao de versoes (major.minor.patch.build — cobre os 4 segmentos usados
// pelo projeto, ex.: 1.0.2.1); segmentos ausentes valem 0.
function compareVersions(a, b) {
  const pa = String(a).split('.').map(n => parseInt(n, 10) || 0)
  const pb = String(b).split('.').map(n => parseInt(n, 10) || 0)
  const len = Math.max(pa.length, pb.length, 4)
  for (let i = 0; i < len; i++) {
    if ((pa[i] || 0) > (pb[i] || 0)) return 1
    if ((pa[i] || 0) < (pb[i] || 0)) return -1
  }
  return 0
}

// ============================================
// VALIDACAO DO CONTRATO (se invalido, NAO executa)
// ============================================
function validateSystem(systemDir) {
  const errors = []
  const manifest = readJsonSafe(path.join(systemDir, 'system.json'))
  if (!manifest) {
    return { valid: false, errors: ['system.json ausente ou invalido'], manifest: null, version: null }
  }

  const required = ['id', 'name', 'systemVersion', 'platformVersion', 'dataDirectory']
  for (const field of required) {
    if (!manifest[field] || typeof manifest[field] !== 'string') {
      errors.push(`campo obrigatoria ausente em system.json: ${field}`)
    }
  }
  if (errors.length) {
    return { valid: false, errors, manifest, version: null }
  }

  // id precisa bater com o nome da pasta (evita clone/renome solto)
  const folderId = path.basename(systemDir)
  if (folderId !== manifest.id) {
    errors.push(`system.json id (${manifest.id}) difere do nome da pasta (${folderId})`)
  }

  // estrutura obrigatoria (espelha SystemContract.RequiredDirectories do contrato C#)
  // app/executable e migrations sao obrigatorios; app e opcional se entryPoint omitido
  const hasEntryPoint = manifest.entryPoint && typeof manifest.entryPoint === 'string'
  if (hasEntryPoint) {
    if (!fs.existsSync(path.join(systemDir, 'app'))) {
      errors.push('estrutura obrigatoria ausente: app/')
    }
    if (!fs.existsSync(path.join(systemDir, 'app', 'executable'))) {
      errors.push('estrutura obrigatoria ausente: app/executable/')
    }
  }
  if (!fs.existsSync(path.join(systemDir, 'migrations'))) {
    errors.push('estrutura obrigatoria ausente: migrations/')
  }

  // entry point (opcional) — se informado, deve existir
  if (hasEntryPoint) {
    const entryPoint = path.join(systemDir, manifest.entryPoint)
    if (!fs.existsSync(entryPoint)) {
      errors.push(`entryPoint nao encontrado: ${manifest.entryPoint}`)
    }
  }

  // renderer (se declarado) precisa existir
  if (manifest.renderer && !fs.existsSync(path.join(systemDir, manifest.renderer))) {
    errors.push(`renderer nao encontrado: ${manifest.renderer}`)
  }

  // dataDirectory obrigatoriamente sob data/ (dados separados do software)
  if (!/^data[\\\/]/.test(manifest.dataDirectory)) {
    errors.push('dataDirectory deve comecar com "data/" (dados fora do pacote de software)')
  }

  // version.json coerente
  const version = readJsonSafe(path.join(systemDir, 'version.json'))
  if (!version || !version.version) {
    errors.push('version.json ausente ou sem "version"')
  } else if (version.systemId && version.systemId !== manifest.id) {
    errors.push(`version.json.systemId (${version.systemId}) difere de system.json.id (${manifest.id})`)
  }

  // compatibilidade com a plataforma
  const platformVersion = getPlatformVersion()
  if (compareVersions(platformVersion, manifest.platformVersion) < 0) {
    errors.push(`plataforma ${platformVersion} < platformVersion exigida ${manifest.platformVersion}`)
  }

  return { valid: errors.length === 0, errors, manifest, version }
}

// ============================================
// DESCOBERTA
// ============================================
// Retorna a lista de sistemas validos. A resolucao e deterministica: raizes na
// ordem de prioridade (Systems/ > resources/systems/ > dist/systems/) e a
// primeira pasta de um id encontrada vence — instalacao do usuario substitui a
// embutida, e nunca ha duas copias ambiguas do mesmo sistema.
function discoverSystems() {
  const roots = getSystemsRoots()
  const sources = getSystemsRootSources()
  const systemsById = new Map()

  for (let i = 0; i < roots.length; i++) {
    const root = roots[i]
    if (!fs.existsSync(root)) continue
    let entries = []
    try {
      entries = fs.readdirSync(root, { withFileTypes: true })
    } catch { continue }
    for (const entry of entries) {
      if (!entry.isDirectory()) continue
      if (entry.name.startsWith('.') || entry.name.includes('.old-') || entry.name.includes('.staging-')) continue
      const systemDir = path.join(root, entry.name)
      if (!fs.existsSync(path.join(systemDir, 'system.json'))) continue
      const result = validateSystem(systemDir)
      const record = {
        // Id canonico = nome da pasta (o proprio contrato exige id == pasta);
        // manifesto com id divergente ja volta como erro de validacao.
        id: entry.name,
        dir: systemDir,
        root,
        source: sources[i] || 'bundled',
        ...result
      }
      if (!systemsById.has(record.id)) {
        systemsById.set(record.id, record)
      }
    }
  }
  return Array.from(systemsById.values())
}

// ============================================
// STATUS DE UM SISTEMA (descoberta com estados)
// ============================================
// Estados exigidos pela plataforma:
//   INSTALADO | NAO_INSTALADO | INVALIDO | DESATUALIZADO | ATUALIZADO
// A ausencia de um sistema NAO e erro da plataforma.
const SYSTEM_STATUS = {
  INSTALADO: 'INSTALADO',
  NAO_INSTALADO: 'NAO_INSTALADO',
  INVALIDO: 'INVALIDO',
  DESATUALIZADO: 'DESATUALIZADO',
  ATUALIZADO: 'ATUALIZADO'
}

// Inspeciona a pasta de um sistema em qualquer raiz (mesmo invalida).
// Diferencia "nao existe" de "existe mas viola o contrato".
function inspectSystem(systemId) {
  const roots = getSystemsRoots()
  const sources = getSystemsRootSources()

  for (let i = 0; i < roots.length; i++) {
    const dir = path.join(roots[i], systemId)
    if (!fs.existsSync(dir)) continue

    const hasManifest = fs.existsSync(path.join(dir, 'system.json'))
    const result = hasManifest
      ? validateSystem(dir)
      : { valid: false, errors: ['system.json ausente'], manifest: null, version: null }

    return {
      id: systemId,
      dir,
      root: roots[i],
      source: sources[i] || 'bundled',
      present: true,
      valid: result.valid,
      errors: result.errors,
      manifest: result.manifest,
      version: result.version,
      installedVersion: result.version ? result.version.version : null,
      name: result.manifest ? result.manifest.name : systemId
    }
  }

  return {
    id: systemId,
    dir: null,
    root: null,
    source: null,
    present: false,
    valid: false,
    errors: [],
    manifest: null,
    version: null,
    installedVersion: null,
    name: systemId
  }
}

// Resolve o status de um sistema.
//   release: { version } vindo do release (opcional) — sem ele, so e possivel
//   afirmar INSTALADO / NAO_INSTALADO / INVALIDO.
function getSystemStatus(systemId, release) {
  const info = inspectSystem(systemId)

  if (!info.present) {
    return { ...info, status: SYSTEM_STATUS.NAO_INSTALADO }
  }
  if (!info.valid) {
    return { ...info, status: SYSTEM_STATUS.INVALIDO }
  }
  if (!release || !release.version) {
    return { ...info, status: SYSTEM_STATUS.INSTALADO }
  }

  const cmp = compareVersions(info.installedVersion, release.version)
  return {
    ...info,
    status: cmp < 0 ? SYSTEM_STATUS.DESATUALIZADO : SYSTEM_STATUS.ATUALIZADO,
    releaseVersion: release.version
  }
}

function findSystem(systemId) {
  return discoverSystems().find(s => s.id === systemId && s.valid) || null
}


// Sistema padrao para inicializacao: preferredId se valido, senao o primeiro valido.
function getDefaultSystem(preferredId) {
  const systems = discoverSystems().filter(s => s.valid)
  if (preferredId) {
    const preferred = systems.find(s => s.id === preferredId)
    if (preferred) return preferred
  }
  return systems[0] || null
}

// ============================================
// DADOS DO SISTEMA (separados do software; nunca tocados pelo updater)
// ============================================
// Cria a estrutura de dados do sistema e migra o banco legado (data/SFTecnologias.db)
// na primeira execucao. Copy, nunca move: o arquivo legado permanece como fallback.
function ensureSystemData(system) {
  const platformDataRoot = getPlatformDataRoot()
  const dataDir = path.join(platformDataRoot, system.manifest.dataDirectory)
  const legacyDataDir = path.join(platformDataRoot, 'data')
  const legacyDb = path.join(legacyDataDir, 'SFTecnologias.db')
  const newDb = path.join(dataDir, 'database.sqlite')

  for (const sub of ['', path.join('configuration'), path.join('logs'), path.join('backups'), path.join('user-data')]) {
    const dir = path.join(dataDir, sub)
    if (!fs.existsSync(dir)) {
      fs.mkdirSync(dir, { recursive: true })
      log('[DATA] Diretorio de dados criado: ' + dir)
    }
  }

  if (!fs.existsSync(newDb) && fs.existsSync(legacyDb)) {
    try {
      fs.copyFileSync(legacyDb, newDb)
      for (const suffix of ['-wal', '-shm']) {
        if (fs.existsSync(legacyDb + suffix)) fs.copyFileSync(legacyDb + suffix, newDb + suffix)
      }
      log('[DATA] Banco legado migrado: ' + legacyDb + ' -> ' + newDb)
    } catch (err) {
      log('[DATA] WARNING: falha ao migrar banco legado: ' + err.message)
    }
  }

  return { dataDir, dbPath: newDb }
}

// ============================================
// INFORMACOES DE EXECUCAO (a partir do contrato)
// ============================================
function getLaunchInfo(system) {
  const { dataDir, dbPath } = ensureSystemData(system)

  // API compartilhada da plataforma (quando o sistema nao tem entryPoint proprio)
  let apiExe = null
  if (system.manifest.entryPoint && typeof system.manifest.entryPoint === 'string') {
    apiExe = path.join(system.dir, system.manifest.entryPoint)
  } else {
    // Plataforma fornece a API em resources/api/
    const platformRoot = getPlatformRoot()
    apiExe = path.join(platformRoot, 'resources', 'api', 'SFTecnologiasApi.exe')
  }

  return {
    id: system.id,
    name: system.manifest.name,
    apiExe,
    rendererIndex: system.manifest.renderer ? path.join(system.dir, system.manifest.renderer) : null,
    dataDir,
    dbPath,
    healthPath: (system.manifest.backend && system.manifest.backend.healthPath) || '/health'
  }
}

// ============================================
// SISTEMA DE REGISTRO DE SISTEMAS INSTALADOS
// ============================================
// Armazena informacoes sobre sistemas instalados para acesso rapido
// Sem duplicar informacoes essenciais que ja estao em system.json/version.json
function getSystemsRegistryPath() {
  const platformDataRoot = getPlatformDataRoot()
  const configDir = path.join(platformDataRoot, 'configuration')
  return path.join(configDir, 'systems.json')
}

function loadSystemsRegistry() {
  try {
    const registryPath = getSystemsRegistryPath()
    if (fs.existsSync(registryPath)) {
      const content = fs.readFileSync(registryPath, 'utf8')
      return JSON.parse(content)
    }
  } catch (err) {
    log('[REGISTRO] WARNING: nao foi possivel carregar registro de sistemas: ' + err.message)
  }
  // Retorna estrutura padrao se nao existir ou erro
  return { lastUpdated: 0, systems: {} }
}

function saveSystemsRegistry(registry) {
  try {
    const registryPath = getSystemsRegistryPath()
    const configDir = path.dirname(registryPath)
    if (!fs.existsSync(configDir)) {
      fs.mkdirSync(configDir, { recursive: true })
    }
    const content = JSON.stringify(registry, null, 2)
    fs.writeFileSync(registryPath, content, 'utf8')
    return true
  } catch (err) {
    log('[REGISTRO] ERROR: nao foi possivel salvar registro de sistemas: ' + err.message)
    return false
  }
}

// Ids de sistemas realmente presentes em disco (validos ou nao), em qualquer raiz.
function listPresentSystemIds() {
  const ids = new Set()
  for (const root of getSystemsRoots()) {
    if (!fs.existsSync(root)) continue
    let entries = []
    try { entries = fs.readdirSync(root, { withFileTypes: true }) } catch { continue }
    for (const entry of entries) {
      if (!entry.isDirectory()) continue
      if (entry.name.startsWith('.') || entry.name.includes('.old-') || entry.name.includes('.staging-')) continue
      ids.add(entry.name)
    }
  }
  return ids
}

// Atualiza informacoes basicas dos sistemas no registro a partir da descoberta atual
// Esta funcao deve ser chamada periodicamente (nao a cada descoberta) para evitar writes excessivos
function updateRegistryBasicInfo() {
  try {
    const registry = loadSystemsRegistry()
    const now = Date.now()

    // Se o registro foi atualizado recentemente (nos ultimos 5 minutos), nao fazer nada
    if (registry.lastUpdated && (now - registry.lastUpdated) < 5 * 60 * 1000) {
      return true
    }

    if (!registry.systems) registry.systems = {}
    const presentIds = listPresentSystemIds()

    // Sistemas presentes: guarda versao, localizacao e estado (INSTALADO/INVALIDO).
    for (const systemId of presentIds) {
      const info = inspectSystem(systemId)
      const existing = registry.systems[systemId] || {}
      registry.systems[systemId] = {
        ...existing,
        location: info.dir,
        source: info.source,
        installedVersion: info.installedVersion,
        state: info.valid ? SYSTEM_STATUS.INSTALADO : SYSTEM_STATUS.INVALIDO,
        installDate: existing.installDate || new Date().toISOString(),
        autoUpdateEnabled: existing.autoUpdateEnabled === undefined ? true : existing.autoUpdateEnabled
      }
    }

    // Ausentes: nao e erro — apenas deixa de estar instalado (o registro mantem a
    // entrada como catalogo, com a versao de release ja consultada, se houver).
    for (const systemId of Object.keys(registry.systems)) {
      if (presentIds.has(systemId)) continue
      const entry = registry.systems[systemId]
      entry.state = SYSTEM_STATUS.NAO_INSTALADO
      entry.location = null
      entry.installedVersion = null
      entry.source = null
    }

    registry.lastUpdated = now
    return saveSystemsRegistry(registry)
  } catch (err) {
    log('[REGISTRO] ERROR: nao foi possivel atualizar informacoes basicas: ' + err.message)
    return false
  }
}

// Registro informacoes de um sistema como instalado (chamado apos instalacao bem-sucedida)
function recordSystemInstalled(systemId, version, location) {
  try {
    const registry = loadSystemsRegistry()
    if (!registry.systems) {
      registry.systems = {}
    }

    const existing = registry.systems[systemId] || {}
    registry.systems[systemId] = {
      ...existing,
      installedVersion: version || '0.0.0',
      state: SYSTEM_STATUS.INSTALADO,
      location: location || (existing.location || null),
      installDate: existing.installDate || new Date().toISOString(),
      lastUpdated: new Date().toISOString(),
      autoUpdateEnabled: existing.autoUpdateEnabled === undefined ? true : existing.autoUpdateEnabled
    }

    return saveSystemsRegistry(registry)
  } catch (err) {
    log('[REGISTRO] ERROR: nao foi possivel registrar instalacao do sistema: ' + err.message)
    return false
  }
}

// Atualiza o estado/versao disponivel de um sistema no registro (descoberta x release)
function recordSystemState(systemId, state, details) {
  try {
    const registry = loadSystemsRegistry()
    if (!registry.systems) registry.systems = {}
    const existing = registry.systems[systemId] || {}
    registry.systems[systemId] = { ...existing, state }
    if (details && typeof details === 'object') {
      Object.assign(registry.systems[systemId], details)
    }
    return saveSystemsRegistry(registry)
  } catch (err) {
    log('[REGISTRO] ERROR: nao foi possivel registrar estado do sistema: ' + err.message)
    return false
  }
}

// Atualizar status de verificacao de atualizacao
function recordSystemUpdateCheck(systemId, status, details = null) {
  try {
    const registry = loadSystemsRegistry()
    if (!registry.systems) {
      registry.systems = {}
    }
    
    if (!registry.systems[systemId]) {
      registry.systems[systemId] = {}
    }
    
    registry.systems[systemId].lastUpdateCheck = new Date().toISOString()
    registry.systems[systemId].updateStatus = status
    if (details !== null) {
      registry.systems[systemId].updateDetails = details
    }
    
    return saveSystemsRegistry(registry)
  } catch (err) {
    log('[REGISTRO] ERROR: nao foi possivel registrar verificacao de atualizacao: ' + err.message)
    return false
  }
}

// Configurar preferencia de atualizacao automática
function setSystemAutoUpdate(systemId, enabled) {
  try {
    const registry = loadSystemsRegistry()
    if (!registry.systems) {
      registry.systems = {}
    }
    
    if (!registry.systems[systemId]) {
      registry.systems[systemId] = {}
    }
    
    registry.systems[systemId].autoUpdateEnabled = enabled
    
    return saveSystemsRegistry(registry)
  } catch (err) {
    log('[REGISTRO] ERROR: nao foi possivel configurar auto-update: ' + err.message)
    return false
  }
}

// Obter informacoes completas de um sistema do registro
// Se o registro estiver desatualizado (mais de 5 minutos), atualiza primeiro
function getSystemInfo(systemId) {
  try {
    const registry = loadSystemsRegistry()
    const now = Date.now()
    
    // Se nao temos registro ou esta desatualizado, atualizar informacoes basicas
    if (!registry.lastUpdated || (now - registry.lastUpdated) > 5 * 60 * 1000) {
      updateRegistryBasicInfo()
      // Recarregar apos atualizacao
      return loadSystemsRegistry().systems[systemId] || null
    }
    
    return registry.systems && registry.systems[systemId] ? registry.systems[systemId] : null
  } catch (err) {
    log('[REGISTRO] ERROR: nao foi possivel obter informacoes do sistema: ' + err.message)
    return null
  }
}

// Sincroniza o registro com o disco: sistema que sumiu deixa de estar instalado,
// mas a entrada e mantida (catalogo) — ausencia nao e erro da plataforma.
function cleanupRegistry() {
  try {
    const registry = loadSystemsRegistry()
    if (!registry.systems) {
      return true
    }

    let changed = false
    const presentIds = listPresentSystemIds()

    for (const systemId of Object.keys(registry.systems)) {
      const entry = registry.systems[systemId]
      if (presentIds.has(systemId)) {
        const info = inspectSystem(systemId)
        const state = info.valid ? SYSTEM_STATUS.INSTALADO : SYSTEM_STATUS.INVALIDO
        if (entry.state !== state || entry.location !== info.dir) {
          entry.state = state
          entry.location = info.dir
          entry.installedVersion = info.installedVersion
          changed = true
        }
        continue
      }

      if (entry.state !== SYSTEM_STATUS.NAO_INSTALADO || entry.location !== null) {
        entry.state = SYSTEM_STATUS.NAO_INSTALADO
        entry.location = null
        entry.installedVersion = null
        changed = true
        log('[REGISTRO] Sistema deixou de estar instalado: ' + systemId)
      }
    }

    if (changed) {
      registry.lastUpdated = Date.now()
      return saveSystemsRegistry(registry)
    }
    return true
  } catch (err) {
    log('[REGISTRO] ERROR: nao foi possivel limpar registro: ' + err.message)
    return false
  }
}

// true quando o processo consegue gravar em `dir` (sobe ate a pasta existente).
// Prova real de escrita: fs.accessSync(W_OK) e enganoso no Windows e devolve
// true em pastas onde a escrita e negada (ex.: C:\Windows).
function canWriteInstallDir(dir) {
  let target = dir
  while (target && !fs.existsSync(target)) {
    const parent = path.dirname(target)
    if (parent === target) break
    target = parent
  }
  const probe = path.join(target, `.sf-write-probe-${process.pid}-${Date.now()}`)
  try {
    fs.writeFileSync(probe, '')
    fs.unlinkSync(probe)
    return true
  } catch {
    return false
  }
}

module.exports = {
  initLogging,
  getPlatformVersion,
  getPlatformRoot,
  getSystemsRoots,
  getSystemsRootSources,
  getPlatformDataRoot,
  validateSystem,
  discoverSystems,
  inspectSystem,
  listPresentSystemIds,
  getSystemStatus,
  SYSTEM_STATUS,
  findSystem,
  getDefaultSystem,
  ensureSystemData,
  getLaunchInfo,
  compareVersions,
  // Funcoes do registro de sistemas
  getSystemsRegistryPath,
  loadSystemsRegistry,
  saveSystemsRegistry,
  updateRegistryBasicInfo,
  recordSystemInstalled,
  recordSystemState,
  recordSystemUpdateCheck,
  setSystemAutoUpdate,
  getSystemInfo,
  cleanupRegistry,
  canWriteInstallDir
}
