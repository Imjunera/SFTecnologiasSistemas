# SYSTEM CONTRACT — SF Tecnologias

> **A plataforma define o contrato. Todo sistema implementa o contrato.**
> Referência de código: `src/platform/SF.Platform.Contract/` (C#, testes em `tests/SF.Platform.Tests`).

## Layout físico

```text
<InstallDir>\                      ← binários (ex.: C:\Program Files\SF Tecnologias) — substituíveis
  SF Tecnologias BETA.exe          ← plataforma (shell)
  SF.Updater.exe                   ← atualizador
  version.json                     ← versão da PLATAFORMA
  Systems\<system-id>\             ← software de cada sistema (um por pasta)

<DataRoot>\                        ← dados (ex.: %ProgramData%\SF Tecnologias) — NUNCA substituídos
  data\SFTecnologias.db            ← (legado, preservado como fallback)
  data\<system-id>\
    database.sqlite                ← banco operacional do sistema (SQLite)
    configuration\  logs\  backups\  user-data\
```

- **Software e dados separados por obrigação.** `dataDirectory` do sistema fica sempre fora do `InstallDir`.
- **Isolamento:** um sistema nunca acessa os dados de outro (cada um tem seu próprio `database.sqlite`).
- Migração legado→novo layout: cópia one-time (nunca move), feita pela plataforma na primeira execução e por `SqliteLegacyMigrator` na API.

## Estrutura mínima de um sistema distribuído

```text
<system-id>/
├── system.json          ← OBRIGATÓRIO
├── version.json         ← OBRIGATÓRIO
├── app/
│   └── executable/      ← OBRIGATÓRIO (entry point mora aqui)
├── modules/             ← recomendado (core/features/integrations)
├── assets/              ← recomendado
├── configuration/       ← recomendado (defaults/schemas)
└── migrations/          ← OBRIGATÓRIO
```

Obrigatório: `app/`, `app/executable/`, `migrations/`. Ausência de pasta recomendada ⇒ **aviso**, não bloqueio (nada de pastas artificiais).

## system.json

```json
{
  "id": "h2-conveniencia",
  "name": "H2 Conveniência",
  "publisher": "SF Tecnologias",
  "systemVersion": "1.0.2.1",
  "platformVersion": "1.0.2.1",
  "entryPoint": "app/executable/SFTecnologiasApi.exe",
  "renderer": "app/resources/renderer/index.html",
  "dataDirectory": "data/h2-conveniencia"
}
```

| Campo | Regra |
|---|---|
| `id` | kebab-case; **deve ser igual ao nome da pasta** |
| `systemVersion` | versão do sistema (semver) |
| `platformVersion` | versão **mínima** da plataforma compatível |
| `entryPoint` | relativo à raiz do sistema, dentro de `app/executable/` — nunca busca automática |
| `renderer` | opcional; UI carregada pelo shell da plataforma |
| `dataDirectory` | sempre `data/<…>`, relativo ao DataRoot |

## version.json

```json
{ "systemId": "h2-conveniencia", "version": "1.0.2.1", "build": "20261002" }
```

A plataforma lê a versão instalada **sem executar o sistema**.

## Validação (antes de executar/instalar — sistema inválido NÃO executa)

1. Pasta existe; 2. `system.json` válido; 3. `version.json` válido; 4. `id` == pasta == `systemId`; 5. versões válidas; 6. estrutura obrigatória; 7. `entryPoint` existe; 8. `dataDirectory` sob `data/`; 9. compatibilidade de plataforma; 10. (distribuição) id do pacote == id do manifest.

## Distribuição (GitHub Releases)

Pacote = zip da estrutura acima + `system-manifest-<systemId>-<versão>.json` (`packageUrl`, `sha256`, `sizeBytes`). Nunca incluir SQLite de produção, dados reais, segredos, tokens, `.env`. Empacotamento: `package-system.ps1`. Atualização de cada sistema é **independente**; sistema já instalado e atualizado **não é baixado novamente**; offline ⇒ sistema instalado continua funcionando.

## Login (plataforma)

Login é da **plataforma** (empresa + senha; campo E-mail não existe no fluxo de autenticação). O JWT/`jwt.secret` é infraestrutura da plataforma e fica no DataRoot (`data/jwt.secret`), não dentro do pacote de software.
