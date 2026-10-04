// ============================================
// SF Tecnologias - testes da fonte de release (offline/manifesto)
// ============================================
// Garante que a plataforma nunca quebra por causa do GitHub:
//   - falha de rede  => { ok:false, offline:true } (execucao local segue);
//   - HTTP 404/401   => { ok:false, offline:false } (release indisponivel);
//   - manifesto local file:// e aceito;
//   - components.systems[] e lido do manifesto.
// Rodar: node --test "tests/desktop/*.test.js"
// ============================================
'use strict'

const { test, after } = require('node:test')
const assert = require('node:assert')
const fs = require('fs')
const os = require('os')
const path = require('path')

const releaseSource = require('../../src/desktop/systems/release-source')

const realFetch = global.fetch

after(() => { global.fetch = realFetch })

test('falha de rede nao lanca: ok=false e offline=true', async (t) => {
  t.after(() => { global.fetch = realFetch })
  global.fetch = async () => { throw new Error('getaddrinfo ENOTFOUND api.github.com') }

  const res = await releaseSource.fetchLatestRelease()
  assert.strictEqual(res.ok, false)
  assert.strictEqual(res.offline, true)
  assert.ok(res.error)
})

test('HTTP 404 e release indisponivel (offline=false), nao erro de rede', async (t) => {
  t.after(() => { global.fetch = realFetch })
  global.fetch = async () => ({ ok: false, status: 404, json: async () => ({}) })

  const res = await releaseSource.fetchLatestRelease()
  assert.strictEqual(res.ok, false)
  assert.strictEqual(res.offline, false)
  assert.strictEqual(res.status, 404)
})

test('manifesto remoto com rede caida -> offline=true e nada lanca', async (t) => {
  t.after(() => { global.fetch = realFetch })
  global.fetch = async () => { throw new Error('fetch failed') }

  const res = await releaseSource.fetchManifest('https://example.invalid/manifest.json')
  assert.strictEqual(res.ok, false)
  assert.strictEqual(res.offline, true)
})

test('manifesto local file:// e aceito e expoe systems[]', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sf-manifest-'))
  const file = path.join(dir, 'manifest.json')
  fs.writeFileSync(file, JSON.stringify({
    product: 'SF.Tecnologias',
    version: '1.0.2',
    components: {
      desktop: { version: '1.0.2' },
      systems: [
        { id: 'h2-conveniencia', version: '1.0.2' },
        { id: 'livraria-papelaria-lider', version: '2.0.0' },
        { semId: true },
        null
      ]
    }
  }), 'utf8')

  const res = await releaseSource.fetchManifest('file:///' + file.replace(/\\/g, '/'))
  assert.strictEqual(res.ok, true)
  assert.strictEqual(res.manifest.components.systems.length, 4)

  const ids = releaseSource.listSystemEntries(res.manifest).map(e => e.id)
  assert.deepStrictEqual(ids, ['h2-conveniencia', 'livraria-papelaria-lider'])

  const h2 = releaseSource.findSystemEntry(res.manifest, 'h2-conveniencia')
  assert.strictEqual(h2.version, '1.0.2')
  assert.strictEqual(releaseSource.findSystemEntry(res.manifest, 'nao-existe'), null)
})

test('manifesto sem components.systems devolve lista vazia (nao erro)', () => {
  assert.deepStrictEqual(releaseSource.listSystemEntries(null), [])
  assert.deepStrictEqual(releaseSource.listSystemEntries({ components: {} }), [])
  assert.deepStrictEqual(releaseSource.listSystemEntries({ components: { systems: 'x' } }), [])
})

test('token vem de SF_RELEASE_TOKEN e sem token a consulta e anonima', (t) => {
  t.after(() => { delete process.env.SF_RELEASE_TOKEN })

  process.env.SF_RELEASE_TOKEN = '  abc123  '
  assert.strictEqual(releaseSource.readAuthToken(), 'abc123')

  delete process.env.SF_RELEASE_TOKEN
  assert.strictEqual(releaseSource.readAuthToken(), null, 'sem token configurado: anonimo')
})
