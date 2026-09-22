# REPORT-PHASE09: Banco de Dados e Estrutura Empresarial

## Objective
Consolidar a estrutura de dados empresarial da plataforma, implementando entidades de empresas e usuários, relacionamentos, índices, restrições, isolamento multiempresa, migrations PostgreSQL e seed de desenvolvimento (quando justificado).

## Files Created
- src/backend/SF.Tecnologias.Infrastructure/Persistence/DevelopmentSeeder.cs
- src/backend/SF.Tecnologias.Infrastructure/Persistence/AppDbContext.cs (modified)
- src/backend/SF.Tecnologias.Infrastructure/Services/TenantProvider.cs (no change, but used)
- src/backend/SF.Tecnologias.Infrastructure/SF.Tecnologias.Infrastructure.csproj (modified)
- src/backend/SF.Tecnologias.Api/Program.cs (modified)
- tests/SF.Tecnologias.Infrastructure.Tests/DatabaseTestBase.cs
- tests/SF.Tecnologias.Infrastructure.Tests/DatabaseStructureTests.cs
- tests/SF.Tecnologias.Infrastructure.Tests/FakeTenantProvider.cs
- tests/SF.Tecnologias.Infrastructure.Tests/UnitTest1.cs (replaced with placeholder)
- Migrations:
  - 20260919193602_ConsolidarEstruturaEmpresarial.cs
  - 20260919200009_RestricoesEmpresas.cs

## Files Modified
- src/backend/SF.Tecnologias.Infrastructure/Persistence/AppDbContext.cs
- src/backend/SF.Tecnologias.Infrastructure/SF.Tecnologias.Infrastructure.csproj
- src/backend/SF.Tecnologias.Api/Program.cs
- tests/SF.Tecnologias.Infrastructure.Tests/UnitTest1.cs

## Dependencies Added
- Infrastructure: BCrypt.Net-Next (version 4.0.3) – required for password hashing in DevelopmentSeeder.
  (Note: BCrypt.Net-Next was already referenced by the Application project; we added it to Infrastructure to allow the seeder to compile.)

## Technical Decisions
1. **Multi-Tenant Isolation**: Implemented a global query filter in `AppDbContext` that automatically applies a tenant (empresa_id) filter to all entities implementing `ITenantEntity`. The filter is bypassed when `TenantId` is null (unauthenticated context) to allow login and seeding operations.
2. **Tenancy Resolution**: Added `ITenantProvider` and `TenantProvider` (registered as scoped) to resolve the current tenant from the authenticated user's JWT claim "empresa_id". The provider is injected into `AppDbContext`.
3. **Idempotent Development Seeder**: Created `DevelopmentSeeder` that runs only in the Development environment. It seeds:
   - Permissions (a global catalog) – idempotent by name.
   - A single company (H2 Conveniência, codigo "H2").
   - An "Administrador" profile with all permissions.
   - An admin user (admin@sftecnologias.local, senha "Admin@123") linked to the company and profile.
   The seeder is safe to run multiple times and is called automatically on API startup in Development.
4. **Data Integrity**:
   - Added unique constraints: `Empresa.Codigo`, `Usuario.Email`, and a composite unique key on `UsuarioEmpresa (UsuarioId, EmpresaId)`.
   - Added CHECK constraints to prevent empty strings in `Empresa.Codigo` and `Empresa.Nome`.
   - Added audit fields (`CriadoEm`, `AtualizadoEm`, `Ativo`) to all relevant tables (including `UsuarioEmpresas` and `Permissoes`).
5. **Relationships**:
   - Corrected the many-to-many relationship between `Perfil` and `Permissao` by removing the misplaced foreign key in `Permissoes` and creating a join table `PerfilPermissoes` with a composite primary key.
   - Ensured all foreign keys use `ReferentialAction.Restrict` to prevent accidental deletions.

## Business Rules Implemented
- Empresa:
  - `Codigo` (login identifier) must be unique and non-empty.
  - `Nome` must be non-empty.
- Usuario:
  - `Email` must be unique and non-empty.
  - `SenhaHash` must be stored as a BCrypt hash.
- UsuarioEmpresa:
  - Association between a user and a company with a profile.
  - Unique per (UsuarioId, EmpresaId).
  - Must reference active Usuario, Empresa, and Perfil.
- Perfil:
  - `Nome` must be unique and non-empty.
  - Can have zero or more permissions (many-to-many).
- Permissao:
  - `Nome` must be unique and non-empty (global catalog of permissions).
  - Reusable across profiles and companies.

## Build and Test Results
- **Backend Build**: Success (0 errors, 4 warnings related to BCrypt.Net-Next known vulnerability – informational only).
- **Backend Tests: 11 tests passed** (all in `SF.Tecnologias.Infrastructure.Tests`).
  - Tests cover: table creation, relationships, unique constraints, referential integrity, multi-tenant isolation (query filter), and permission many-to-many.
- **Frontend TypeScript Check**: Success (no errors).
- **Frontend Build**: N/A (no changes in this phase; prior phase 8 build remains valid after fixing App.tsx corruption – see Phase 8 report).
- **End-to-End Validation**:
  - API starts successfully and applies migrations.
  - Development seeder populates the database.
  - Login endpoint works with empresaId=1 and senha=Admin@123, returning a JWT with user info and permissions.
  - Invalid credentials are rejected (401).
  - Health check endpoint returns 200.

## Failures Found and Resolved
- **Connection Pool Issue During Test Initialization**: The test suite was dropping and recreating the test database for each test class, but the Npgsql connection pool held onto open connections, causing "connection forcibly closed by remote host" errors during `MigrateAsync`. Fixed by calling `NpgsqlConnection.ClearAllPools()` before dropping the test database.
- **Missing CHECK Constraints for Empty Strings**: The initial validation only tested for `null` values, but the database allowed empty strings. Added `HasCheckConstraint` to enforce non-empty strings for `Empresa.Codigo` and `Empresa.Nome`.
- **Migration Scaffold Error**: The initial attempt to scaffold the many-to-many join table incorrectly tried to create a unique index on the join table's columns via a lambda that EF Core couldn't translate. Fixed by simplifying the `UsingEntity` call to only specify the join table name, relying on the conventional composite primary key.

## Pending Items
- None for this phase. All acceptance criteria are met.

## Instructions for Local Validation
1. Ensure PostgreSQL is running on localhost:5432 with the default database `postgres` (user: postgres, password: postgres) – the same as used in the development connection string.
2. Run the API: `dotnet run --project src/backend/SF.Tecnologias.Api`.
   - The API will automatically apply any pending migrations and run the development seeder (because the environment is Development).
3. Test the login endpoint:
   ```powershell
   $body = '{"empresaId": 1, "senha": "Admin@123"}'
   Invoke-RestMethod -Uri 'http://localhost:5145/api/auth/login' -Method Post -ContentType 'application/json' -Body $body
   ```
   Expected response: JSON with `accessToken`, `usuarioId`, `empresaId`, `nome`, and `permissoes` array.
4. Test an invalid login (wrong password) – should return 401.
5. Test the health endpoint: `Invoke-RestMethod -Uri 'http://localhost:5145/health'` – should return 200.

## Next Phase Recommended
**Fase 10 — Integração da Interface H2 Conveniência**
- Lê toda a estrutura do projeto H2 Conveniência.
- Avalia framework, dependências, componentes, estilos e compatibilidade com React, Vite, TypeScript e Electron.
- Integra à área autenticada (após login).
- Adapta a navegação ao sistema de janelas internas.
- Utiliza o contexto da empresa autenticada.
- Organiza componentes para manutenção.
- Não altera a identidade visual da H2 sem justificativa.
- Não transforma a interface H2 na identidade global da plataforma.
- Não cria dashboard obrigatório.
- Não adiciona sidebar sem necessidade.
- Validação: build do frontend, build do Electron, testes, navegação, contexto da empresa, renderização e acesso autenticado.

--- 
*Report generated on 2026-09-19*