// ============================================
// SF PLATFORM — FONTE DE RELEASE (GitHub Releases)
// ============================================
// Unica implementacao da consulta de release usada por:
//   - atualizacao da propria plataforma;
//   - instalacao individual de sistemas (Fase 3);
//   - atualizacao individual de sistemas (Fase 4).
// Git e ferramenta de desenvolvimento; release e o mecanismo de distribuicao.
//
// Falha de rede/autenticacao NUNCA lancada para o chamador: o resultado vem com
// { ok:false, error } para que a execucao local continue (offline).
'use strict'

const fs = require('fs')
const path = require('path')
const systemManager = require('./system-manager')

const DEFAULT_OWNER = 'Imjunera'
const DEFAULT_REPO = 'SFTecnologiasSistemas'
const REQUEST_TIMEOUT_MS = 15000

function getRepo() {
  const owner = process.env.SF_RELEASE_OWNER || DEFAULT_OWNER
  const repo = process.env.SF_RELEASE_REPO || DEFAULT_REPO
  return { owner, repo }
}

function configurationDir() {
  return path.join(systemManager.getPlatformDataRoot(), 'configuration')
}

// ============================================
// AUTENTICACAO PARA RELEASES PRIVADOS
// ============================================
// Nenhum segredo e gravado no executavel. O token vem, nesta ordem:
//   1. SF_RELEASE_TOKEN (variavel de ambiente do processo);
//   2. <configuration>/release-auth.json -> { "token": "..." }
// Recomendacao: token de curta duracao e menor privilegio (fine-grained PAT com
// acesso de leitura somente a este repositorio), gravado fora do programa com
// permissoes restritas. Se nao houver token, a consulta anonima e usada (publico).
function readAuthToken() {
  if (process.env.SF_RELEASE_TOKEN) return process.env.SF_RELEASE_TOKEN.trim()

  try {
    const file = path.join(configurationDir(), 'release-auth.json')
    if (!fs.existsSync(file)) return null
    const raw = fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '')
    const parsed = JSON.parse(raw)
    const token = parsed && (parsed.token || parsed.accessToken)
    return token ? String(token).trim() : null
  } catch {
    return null
  }
}

function buildHeaders() {
  const headers = {
    'Accept': 'application/vnd.github.v3+json',
    'User-Agent': 'SF-Tecnologias-Desktop'
  }
  const token = readAuthToken()
  if (token) headers['Authorization'] = 'Bearer ' + token
  return headers
}

// ============================================
// CONSULTA
// ============================================
async function fetchLatestRelease() {
  const { owner, repo } = getRepo()
  const url = `https://api.github.com/repos/${owner}/${repo}/releases/latest`

  try {
    const response = await fetch(url, {
      headers: buildHeaders(),
      signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS)
    })

    if (!response.ok) {
      // HTTP respondido (404/401): release indisponivel, nao e falha de rede.
      return { ok: false, offline: false, status: response.status, error: `GitHub API error (${response.status})` }
    }

    const release = await response.json()
    const latestVersion = (release.tag_name || '').replace(/^v/, '') || '0.0.0'

    const manifestAsset = (release.assets || []).find(a =>
      a.name === 'manifest.json' ||
      (a.name && a.name.startsWith('manifest-') && a.name.endsWith('.json'))
    )

    return {
      ok: true,
      release,
      latestVersion,
      manifestUrl: manifestAsset ? manifestAsset.browser_download_url : null,
      releaseNotes: release.body || '',
      releaseUrl: release.html_url
    }
  } catch (err) {
    return { ok: false, offline: true, error: err.message }
  }
}

async function fetchManifest(manifestUrl) {
  if (!manifestUrl) return { ok: false, offline: false, error: 'manifest URL ausente' }

  try {
    if (manifestUrl.startsWith('file://')) {
      const local = decodeURIComponent(manifestUrl.replace(/^file:\/\//, ''))
      const normalized = /^\/[a-zA-Z]:/.test(local) ? local.slice(1) : local
      const parsed = JSON.parse(fs.readFileSync(normalized, 'utf8').replace(/^\uFEFF/, ''))
      return { ok: true, manifest: parsed }
    }

    const response = await fetch(manifestUrl, {
      headers: buildHeaders(),
      signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS)
    })
    if (!response.ok) {
      return { ok: false, offline: false, status: response.status, error: `manifest download failed (${response.status})` }
    }

    const manifest = await response.json()
    return { ok: true, manifest }
  } catch (err) {
    return { ok: false, offline: true, error: err.message }
  }
}

// Release + manifesto em uma chamada. Em offline: { ok:false, error } (nao lanca).
async function getReleaseManifest() {
  const releaseInfo = await fetchLatestRelease()
  if (!releaseInfo.ok) return releaseInfo

  const manifestInfo = await fetchManifest(releaseInfo.manifestUrl)
  if (!manifestInfo.ok) return { ...manifestInfo, release: releaseInfo }

  return { ...manifestInfo, release: releaseInfo }
}

// ============================================
// SISTEMAS DECLARADOS NO MANIFESTO
// ============================================
function listSystemEntries(manifest) {
  if (!manifest || !manifest.components || !Array.isArray(manifest.components.systems)) return []
  return manifest.components.systems.filter(e => e && typeof e.id === 'string')
}

function findSystemEntry(manifest, systemId) {
  return listSystemEntries(manifest).find(e => e.id === systemId) || null
}

module.exports = {
  getRepo,
  readAuthToken,
  fetchLatestRelease,
  fetchManifest,
  getReleaseManifest,
  listSystemEntries,
  findSystemEntry
}
