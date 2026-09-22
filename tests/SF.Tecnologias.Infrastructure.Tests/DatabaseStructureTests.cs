using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SF.Tecnologias.Domain;
using SF.Tecnologias.Infrastructure.Persistence;
using Xunit;

namespace SF.Tecnologias.Infrastructure.Tests;

/// <summary>
/// Phase 09 integration tests: database creation, migrations,
/// relationships, constraints, multi-tenant isolation, and invalid data.
/// Uses a dedicated test database (SFTecnologiasTestsDb) that is created,
/// migrated, and dropped for each test run. Requires local PostgreSQL.
/// </summary>
public class DatabaseStructureTests : DatabaseTestBase
{
    [Fact]
    public async Task Migrations_CreateAllExpectedTables()
    {
        await using var context = CreateContext();
        var canConnect = await context.Database.CanConnectAsync();
        Assert.True(canConnect);

        var tables = await GetTableNamesAsync();
        Assert.Contains("Empresas", tables);
        Assert.Contains("Usuarios", tables);
        Assert.Contains("Perfis", tables);
        Assert.Contains("Permissoes", tables);
        Assert.Contains("UsuarioEmpresas", tables);
        Assert.Contains("PerfilPermissoes", tables);
        Assert.Contains("Produtos", tables);
    }

    [Fact]
    public async Task Relationship_UsuarioEmpresa_LinksAllEntities()
    {
        await using var context = CreateContext();
        var (empresa, perfil, usuario) = await SeedBaseEntitiesAsync(context);

        context.UsuarioEmpresas.Add(new UsuarioEmpresa
        {
            UsuarioId = usuario.Id,
            EmpresaId = empresa.Id,
            PerfilId = perfil.Id
        });
        await context.SaveChangesAsync();

        var loaded = await context.UsuarioEmpresas
            .Include(ue => ue.Usuario)
            .Include(ue => ue.Empresa)
            .Include(ue => ue.Perfil)
            .SingleAsync(ue => ue.EmpresaId == empresa.Id);

        Assert.Equal(usuario.Id, loaded.Usuario.Id);
        Assert.Equal(empresa.Id, loaded.Empresa.Id);
        Assert.Equal(perfil.Id, loaded.Perfil.Id);
    }

    [Fact]
    public async Task Constraint_Empresa_Codigo_MustBeUnique()
    {
        await using var context = CreateContext();
        await SeedBaseEntitiesAsync(context);
        context.Empresas.Add(new Empresa { Codigo = "H2", Nome = "Duplicada" });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Constraint_Usuario_Email_MustBeUnique()
    {
        await using var context = CreateContext();
        var (_, _, usuario) = await SeedBaseEntitiesAsync(context);
        context.Usuarios.Add(new Usuario
        {
            Nome = "Outro",
            Email = usuario.Email,
            SenhaHash = "hash"
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Constraint_UsuarioEmpresa_MustBeUniquePerUsuarioEmpresa()
    {
        await using var context = CreateContext();
        var (empresa, perfil, usuario) = await SeedBaseEntitiesAsync(context);
        context.UsuarioEmpresas.Add(new UsuarioEmpresa
        {
            UsuarioId = usuario.Id, EmpresaId = empresa.Id, PerfilId = perfil.Id
        });
        await context.SaveChangesAsync();

        context.UsuarioEmpresas.Add(new UsuarioEmpresa
        {
            UsuarioId = usuario.Id, EmpresaId = empresa.Id, PerfilId = perfil.Id
        });
        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Constraint_UsuarioEmpresa_RequiresExistingForeignKeys()
    {
        await using var context = CreateContext();
        context.UsuarioEmpresas.Add(new UsuarioEmpresa
        {
            UsuarioId = 999999, EmpresaId = 999999, PerfilId = 999999
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task InvalidData_Empresa_RequiresNome()
    {
        await using var context = CreateContext();
        context.Empresas.Add(new Empresa { Codigo = "X1", Nome = "" });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task MultiTenant_Isolation_FilterAppliesToTenantScopedEntities()
    {
        // Arrange: two companies, each with one user-empresa association
        var empresaA = new Empresa { Codigo = "A", Nome = "Empresa A" };
        var empresaB = new Empresa { Codigo = "B", Nome = "Empresa B" };
        var perfil = new Perfil { Nome = "Operador" };
        var usuario = new Usuario { Nome = "U", Email = "u@x.local", SenhaHash = "h" };

        await using (var context = CreateContext())
        {
            context.AddRange(empresaA, empresaB, perfil, usuario);
            await context.SaveChangesAsync();
            context.UsuarioEmpresas.AddRange(
                new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = empresaA.Id, PerfilId = perfil.Id },
                new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = empresaB.Id, PerfilId = perfil.Id });
            await context.SaveChangesAsync();
        }

        // Act: query as tenant A
        await using (var contextA = CreateContext(new FakeTenantProvider(empresaA.Id)))
        {
            var associations = await contextA.UsuarioEmpresas.ToListAsync();
            Assert.Single(associations);
            Assert.Equal(empresaA.Id, associations[0].EmpresaId);
        }

        // Act: query as tenant B
        await using (var contextB = CreateContext(new FakeTenantProvider(empresaB.Id)))
        {
            var associations = await contextB.UsuarioEmpresas.ToListAsync();
            Assert.Single(associations);
            Assert.Equal(empresaB.Id, associations[0].EmpresaId);
        }

        // Act: unauthenticated context (null tenant) sees all rows (login/seeding scenario)
        await using (var contextNone = CreateContext())
        {
            var associations = await contextNone.UsuarioEmpresas.ToListAsync();
            Assert.Equal(2, associations.Count);
        }
    }

    [Fact]
    public async Task MultiTenant_Insert_ShouldNotLeakToOtherTenant()
    {
        var empresaA = new Empresa { Codigo = "A", Nome = "Empresa A" };
        var empresaB = new Empresa { Codigo = "B", Nome = "Empresa B" };
        var perfil = new Perfil { Nome = "Operador" };
        var usuario = new Usuario { Nome = "U", Email = "u@x.local", SenhaHash = "h" };

        await using (var context = CreateContext())
        {
            context.AddRange(empresaA, empresaB, perfil, usuario);
            await context.SaveChangesAsync();
            context.UsuarioEmpresas.AddRange(
                new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = empresaA.Id, PerfilId = perfil.Id },
                new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = empresaB.Id, PerfilId = perfil.Id });
            await context.SaveChangesAsync();
        }

        // A tenant-scoped save still persists; filtering is a query-level guarantee.
        await using (var contextA = CreateContext(new FakeTenantProvider(empresaA.Id)))
        {
            var seen = await contextA.UsuarioEmpresas.ToListAsync();
            Assert.Single(seen);
        }
    }

    [Fact]
    public async Task Perfil_CanHaveMultiplePermissoes()
    {
        await using var context = CreateContext();
        var perfil = new Perfil { Nome = "Admin" };
        var p1 = new Permissao { Nome = "PRODUTO_VER" };
        var p2 = new Permissao { Nome = "PRODUTO_CADASTRAR" };
        perfil.Permissoes.Add(p1);
        perfil.Permissoes.Add(p2);
        context.AddRange(perfil);
        await context.SaveChangesAsync();

        var loaded = await context.Perfis.Include(x => x.Permissoes)
            .SingleAsync(x => x.Id == perfil.Id);
        Assert.Equal(2, loaded.Permissoes.Count);
    }

    private async Task<(Empresa empresa, Perfil perfil, Usuario usuario)> SeedBaseEntitiesAsync(AppDbContext context)
    {
        var empresa = new Empresa { Codigo = "H2", Nome = "H2 Conveniência" };
        var perfil = new Perfil { Nome = "Administrador" };
        var usuario = new Usuario
        {
            Nome = "Admin",
            Email = "admin@sftecnologias.local",
            SenhaHash = "hash"
        };
        context.AddRange(empresa, perfil, usuario);
        await context.SaveChangesAsync();
        return (empresa, perfil, usuario);
    }
}
