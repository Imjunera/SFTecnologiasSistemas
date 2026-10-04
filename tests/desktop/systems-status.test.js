// ============================================
// SF Tecnologias - testes do System Manager (Fase 3/4)
// ============================================
// Cobrem o que a plataforma precisa garantir sem Electron:
//   - estados: INSTALADO / NAO_INSTALADO / INVALIDO / DESATUALIZADO / ATUALIZADO;
//   - precedencia deterministica entre raizes (Systems/ > resources/systems/);
//   - ausencia de sistema nao e erro;
//   - registro (configuration/systems.json) sem apagar catalogo.
//
// Rodar: node --test tests/desktop
// ============================================
'use strict'

const test = require('node:test')
const assert = require('node:assert')
const fs = require('fs')
const os = require('os')
const path = require('path')

const platformRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'sf-platform-'))
const dataRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'sf-data-'))

process.env.SF_PLATFORM_ROOT = platformRoot
process.env.SF_DATA_ROOT = dataRoot
process.env.SF_PLATFORM_VERSION = '1.0.2.1'

const systems = require('../../src/desktop/systems/system-manager')

function writeJson(file, value) {
  fs.mkdirSync(path.dirname(file), { recursive: true })
  fs.writeFileSync(file, JSON.stringify(value, null, 2), 'utf8')
}

function createSystem(root, id, { version = '1.0.0', platformVersion = '1.0.0', valid = true } = {}) {
  const dir = path.join(root, id)
  fs.mkdirSync(path.join(dir, 'app', 'executable'), { recursive: true })
  fs.mkdirSync(path.join(dir, 'migrations'), { recursive: true })

  writeJson(path.join(dir, 'system.json'), {
    id,
    name: `Sistema ${id}`,
    systemVersion: version,
    platformVersion,
    entryPoint: 'app/executable/run.exe',
    dataDirectory: `data/${id}`
  })
  writeJson(path.join(dir, 'version.json'), { systemId: id, version })
  fs.writeFileSync(path.join(dir, 'app', 'executable', 'run.exe'), `binario de ${id}`, 'utf8')

  if (!valid) {
    fs.rmSync(path.join(dir, 'system.json'))
  }
  return dir
}

test('sistema ausente nao e erro: status NAO_INSTALADO', () => {
  const status = systems.getSystemStatus('livraria-papelaria-lider')
  assert.strictEqual(status.present, false)
  assert.strictEqual(status.status, systems.SYSTEM_STATUS.NAO_INSTALADO)
  assert.deepStrictEqual(status.errors, [])
})

test('sistema valido em Systems/ e INSTALADO', () => {
  const id = 'h2-conveniencia'
  createSystem(path.join(platformRoot, 'Systems'), id)

  const status = systems.getSystemStatus(id)
  assert.strictEqual(status.present, true)
  assert.strictEqual(status.valid, true)
  assert.strictEqual(status.status, systems.SYSTEM_STATUS.INSTALADO)
  assert.strictEqual(status.installedVersion, '1.0.0')
  assert.strictEqual(status.source, 'installed')
  assert.ok(status.dir.startsWith(path.join(platformRoot, 'Systems')))
})

test('pasta sem contrato valida o estado INVALIDO (diferente de ausente)', () => {
  const id = 'sistema-quebrado'
  const dir = path.join(platformRoot, 'Systems', id)
  fs.mkdirSync(dir, { recursive: true })
  fs.writeFileSync(path.join(dir, 'leia-me.txt'), 'sem contrato', 'utf8')

  const status = systems.getSystemStatus(id)
  assert.strictEqual(status.present, true)
  assert.strictEqual(status.valid, false)
  assert.strictEqual(status.status, systems.SYSTEM_STATUS.INVALIDO)
  assert.ok(status.errors.length > 0)

  const found = systems.findSystem(id)
  assert.strictEqual(found, null, 'sistema invalido nao pode ser escolhido para executar')
})

test('release mais novo => DESATUALIZADO; release igual/antigo => ATUALIZADO', () => {
  const id = 'versoes-sistema'
  createSystem(path.join(platformRoot, 'Systems'), id, { version: '1.0.2.1' })

  const old = systems.getSystemStatus(id, { version: '1.0.2.5' })
  assert.strictEqual(old.status, systems.SYSTEM_STATUS.DESATUALIZADO)

  const same = systems.getSystemStatus(id, { version: '1.0.2.1' })
  assert.strictEqual(same.status, systems.SYSTEM_STATUS.ATUALIZADO)

  const older = systems.getSystemStatus(id, { version: '1.0.2.0' })
  assert.strictEqual(older.status, systems.SYSTEM_STATUS.ATUALIZADO)
})

test('comparacao de versoes cobre os 4 segmentos', () => {
  assert.strictEqual(systems.compareVersions('1.0.2.1', '1.0.2.2'), -1)
  assert.strictEqual(systems.compareVersions('1.0.2.2', '1.0.2.1'), 1)
  assert.strictEqual(systems.compareVersions('1.0.2', '1.0.2.0'), 0)
  assert.strictEqual(systems.compareVersions('2.0.0.0', '1.9.9.9'), 1)
})

test('precedencia deterministica: Systems/ vence resources/systems/', () => {
  const id = 'sistema-duplicado'
  createSystem(path.join(platformRoot, 'Systems'), id, { version: '2.0.0' })
  createSystem(path.join(platformRoot, 'resources', 'systems'), id, { version: '1.0.0' })

  const status = systems.getSystemStatus(id)
  assert.strictEqual(status.source, 'installed')
  assert.strictEqual(status.installedVersion, '2.0.0')
  assert.strictEqual(
    path.dirname(status.dir),
    path.join(platformRoot, 'Systems'),
    'a copia instalada vence a embutida'
  )

  const discovered = systems.discoverSystems().filter(s => s.id === id)
  assert.strictEqual(discovered.length, 1, 'nunca ha duas copias ambiguas do mesmo id')
})

test('pastas .staging-/.old- e arquivos soltos nao entram na descoberta', () => {
  const roots = [
    path.join(platformRoot, 'Systems'),
    path.join(platformRoot, 'resources', 'systems')
  ]
  for (const root of roots) {
    createSystem(root, '.staging-x-1234', {})
    createSystem(root, 'sistema.old-20260101', {})
    fs.mkdirSync(path.join(root, '.hidden'), { recursive: true })
  }
  fs.writeFileSync(path.join(platformRoot, 'Systems', 'arquivo.txt'), 'x', 'utf8')

  const ids = systems.listPresentSystemIds()
  assert.ok(!ids.has('.staging-x-1234'))
  assert.ok(!ids.has('.hidden'))
  assert.ok(!ids.has('arquivo.txt'))
  assert.ok(!Array.from(ids).some(id => id.includes('.old-')))
})

test('registro guarda versao/localizacao e ausencia vira NAO_INSTALADO sem apagar', () => {
  const id = 'registro-sistema'
  const dir = createSystem(path.join(platformRoot, 'Systems'), id, { version: '1.4.0' })

  assert.strictEqual(systems.recordSystemInstalled(id, '1.4.0', dir), true)
  systems.recordSystemState(id, systems.SYSTEM_STATUS.ATUALIZADO, { releaseVersion: '1.4.0' })

  let registry = systems.loadSystemsRegistry()
  assert.strictEqual(registry.systems[id].installedVersion, '1.4.0')
  assert.strictEqual(registry.systems[id].state, systems.SYSTEM_STATUS.ATUALIZADO)
  assert.strictEqual(registry.systems[id].location, dir)
  assert.strictEqual(registry.systems[id].releaseVersion, '1.4.0')

  // Sistema some do disco: deixa de estar instalado, mas o catalogo permanece.
  fs.rmSync(dir, { recursive: true, force: true })
  registry = systems.loadSystemsRegistry()
  registry.lastUpdated = 0
  require('fs').writeFileSync(systems.getSystemsRegistryPath(), JSON.stringify(registry, null, 2))
  assert.strictEqual(systems.updateRegistryBasicInfo(), true)

  registry = systems.loadSystemsRegistry()
  assert.strictEqual(registry.systems[id].state, systems.SYSTEM_STATUS.NAO_INSTALADO)
  assert.strictEqual(registry.systems[id].location, null)
  assert.strictEqual(registry.systems[id].releaseVersion, '1.4.0', 'release ja consultada nao se perde')
})

test('limpeza de registro nao apaga sistemas ainda presentes', () => {
  const id = 'limpeza-sistema'
  const dir = createSystem(path.join(platformRoot, 'Systems'), id)
  systems.recordSystemInstalled(id, '1.0.0', dir)

  assert.strictEqual(systems.cleanupRegistry(), true)
  const registry = systems.loadSystemsRegistry()
  assert.ok(registry.systems[id], 'sistema presente continua no registro')
  assert.strictEqual(registry.systems[id].state, systems.SYSTEM_STATUS.INSTALADO)
})

test('plataforma incompativel invalida o sistema', () => {
  const id = 'futuro-sistema'
  createSystem(path.join(platformRoot, 'Systems'), id, { platformVersion: '9.9.9' })

  const status = systems.getSystemStatus(id)
  assert.strictEqual(status.valid, false)
  assert.strictEqual(status.status, systems.SYSTEM_STATUS.INVALIDO)
  assert.ok(status.errors.some(e => e.includes('platformVersion')))
})

test('canWriteInstallDir: pasta gravavel e True, caminho inexistente sobe para ancestral', () => {
  const writable = path.join(platformRoot, 'Systems')
  assert.strictEqual(systems.canWriteInstallDir(writable), true)

  const deepNew = path.join(writable, 'nao-existe', 'ainda-nao')
  assert.strictEqual(systems.canWriteInstallDir(deepNew), true, 'sobe ate a pasta existente')

  // C:\Windows: fs.accessSync(W_OK) diria True (falso positivo no Windows);
  // a deteccao precisa espelhar a escrita real (negada para usuario comum).
  const windows = 'C:\\Windows'
  let trulyWritable = false
  try {
    const probe = path.join(windows, `.sf-probe-${process.pid}`)
    fs.writeFileSync(probe, '')
    fs.unlinkSync(probe)
    trulyWritable = true
  } catch { /* negado */ }
  assert.strictEqual(systems.canWriteInstallDir(windows), trulyWritable)
})
