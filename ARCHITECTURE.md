# Arquitetura do SF Tecnologias

## Organização da Solução

```
/
├── installer/                  ← Fontes versionadas do instalador (copiadas p/ dist/ no build)
├── docs/reports/               ← Relatórios de fases (histórico)
├── src/
│   ├── backend/                ← ASP.NET Core 8 (API, Application, Domain, Infrastructure)
│   ├── frontend/sf-tecnologias-web/  ← React 19 + Vite 5 + TypeScript
│   ├── desktop/                ← Electron 44 (main, preload, renderer)
│   └── updater/SF.Updater/     ← Atualizador .NET self-contained (win-x64)
├── tests/                      ← xUnit + Coverlet
├── build-release.ps1           ← Build completo + pacotes zip (flatten desktop)
├── generate-manifest.ps1       ← Manifesto JSON com URLs absolutas GitHub Releases
├── test-update-flow.ps1        ← E2E do updater (simulado + SF.Updater.exe real)
├── INSTALL-CONTRACT.md         ← Contrato de instalação/atualização (checklist)
└── ARCHITECTURE.md             ← Este arquivo
```

## Separação de Responsabilidades

- **Domínio**: regras de negócio puras, entidades e interfaces de repositório. Sem dependências externas.
- **Aplicação**: casos de uso, orquestra domínio e infraestrutura. Depende apenas do domínio.
- **Infraestrutura**: EF Core, SQLite (prod) / PostgreSQL (dev+testes), TenantProvider (claims JWT), repositórios.
- **API**: Minimal APIs + Swagger, JWT auth, health check, CORS, seeder apenas em Development.
- **Frontend**: SPA React; http central com tenant/user via claims JWT (sem headers X-* custom).
- **Desktop**: Electron carrega renderer; backend API self-contained em `resources/api/`; updater em `resources/updater/`.
- **Updater**: independente, baixa manifesto → valida SHA-256 → para serviços → backup → aplica → health-check → rollback em falha.

## Tecnologias

| Camada   | Stack                                                          |
| -------- | -------------------------------------------------------------- |
| Backend  | ASP.NET Core 8, C# 12, EF Core 8, JwtBearer 8                  |
| Banco    | SQLite (Production), PostgreSQL (Development/Tests)            |
| Frontend | React 19, Vite 5, TypeScript ~6, Tailwind, Radix UI, oxlint    |
| Desktop  | Electron 44, Node                                              |
| Updater  | .NET 8, self-contained win-x64                                 |
| Testes   | xUnit 2.9, Coverlet, Microsoft.NET.Test.Sdk 17.14              |
| CI       | GitHub Actions (`release.yml`) → pacotes + SHA-256 + manifesto |

## Contrato de Instalação/Atualização

Ver **[INSTALL-CONTRACT.md](./INSTALL-CONTRACT.md)** para:

- Layout `InstallDir` (binários) vs `%ProgramData%` (dados — nunca substituídos pelo updater)
- `Jwt__Secret` via variável de ambiente do serviço
- Estados do updater e rollback
- Pacotes e manifesto

## Decisões Arquiteturais (ativas)

- **JWT strict**: `ValidateIssuerSigningKey`, `RequireSignedTokens`; secret obrigatório em Production (≥32 chars), via `Jwt__Secret`.
- **Tenant só via JWT**: sem headers `X-Tenant-Id`/`X-User-Id`.
- **Seeder**: `DevelopmentSeeder` roda apenas em `IsDevelopment()`.
- **Login multiusuário**: e-mail opcional no login; se >1 usuário ativo e e-mail omitido → 400.
- **Pacote desktop flatten**: zip root = layout do `InstallDir` (compatível com extração do updater).
- **Estado do updater**: serializado com `JsonStringEnumConverter` (string legível).
- **Instalador**: fontes em `installer/` (versionadas); `build-release.ps1` copia para `dist/`.

## Estratégia de Testes

- Unitários/integração: xUnit (Infrastructure — requer PostgreSQL local; `DatabaseTestBase`).
- E2E updater: `test-update-flow.ps1` (simulação + execução real de `SF.Updater.exe`).
- Frontend: `tsc -b && vite build` + `oxlint`.
- Pre-commit: husky + lint-staged (Prettier) na raiz.
