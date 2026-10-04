using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SF.Tecnologias.Application.Services;
using SF.Tecnologias.Domain;
using SF.Tecnologias.Infrastructure.Persistence;
using Xunit;

namespace SF.Tecnologias.Infrastructure.Tests;

/// <summary>
/// Contrato de login da plataforma: empresa + senha, SEM campo de e-mail.
/// Cobre o requisito "E-mail removido corretamente do login" (contrato, auth e token).
/// </summary>
public class LoginSemEmailTests : DatabaseTestBase
{
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-only-secret-with-at-least-32-characters-000000",
                ["Jwt:Issuer"] = "SF.Tecnologias.Tests",
                ["Jwt:Audience"] = "SF.Tecnologias.Tests",
                ["Jwt:ExpiryMinutes"] = "60"
            })
            .Build();

    private AuthService CreateService() => new(CreateContext(), BuildConfiguration());

    private async Task<Empresa> SeedEmpresaAsync(
        string codigo,
        string permissaoNome,
        bool ativo = true)
    {
        await using var context = CreateContext();

        var empresa = new Empresa { Codigo = codigo, Nome = "Empresa " + codigo, Ativo = ativo };
        var permissao = new Permissao { Nome = permissaoNome, Descricao = "Ver produtos" };
        var perfil = new Perfil { Nome = "Administrador " + codigo, Descricao = "Acesso total" };
        perfil.Permissoes.Add(permissao);

        context.Empresas.Add(empresa);
        context.Perfis.Add(perfil);
        await context.SaveChangesAsync();

        return empresa;
    }

    private async Task<Usuario> SeedUsuarioAsync(
        Empresa empresa,
        string senha,
        string nome = "Administrador",
        string email = "admin@sf.local",
        bool ativo = true,
        bool vinculoAtivo = true)
    {
        await using var context = CreateContext();

        var usuario = new Usuario
        {
            Nome = nome,
            Email = email,
            // Hash real de BCrypt: o login valida a senha de verdade.
            SenhaHash = BCrypt.Net.BCrypt.HashPassword(senha),
            Ativo = ativo
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();

        var perfil = context.Perfis.Single(p => p.Nome == "Administrador " + empresa.Codigo);
        context.UsuarioEmpresas.Add(new UsuarioEmpresa
        {
            UsuarioId = usuario.Id,
            EmpresaId = empresa.Id,
            PerfilId = perfil.Id,
            Ativo = vinculoAtivo
        });
        await context.SaveChangesAsync();

        return usuario;
    }

    [Fact]
    public void LoginRequest_NaoPossuiCampoEmail()
    {
        // O campo saiu do contrato, nao apenas da tela.
        var propriedades = typeof(LoginRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(new[] { "EmpresaCodigo", "Senha" }, propriedades);
    }

    [Fact]
    public async Task Login_ComCredenciaisValidas_RetornaToken()
    {
        var empresa = await SeedEmpresaAsync("H2CONV", "PRODUTO_VER_H2CONV");
        var usuario = await SeedUsuarioAsync(empresa, "H22026");

        var response = await CreateService().LoginAsync(new LoginRequest
        {
            EmpresaCodigo = "H2CONV",
            Senha = "H22026"
        });

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response!.AccessToken));
        Assert.Equal(empresa.Id, response.EmpresaId);
        Assert.Equal(usuario.Id, response.UsuarioId);
        Assert.Equal("Administrador", response.Nome);
        Assert.Contains("PRODUTO_VER_H2CONV", response.Permissoes);
    }

    [Fact]
    public async Task Login_TokenDeclaraIdentidadeSemExigirEmailNoLogin()
    {
        var empresa = await SeedEmpresaAsync("H2CONV", "PRODUTO_VER_H2CONV");
        await SeedUsuarioAsync(empresa, "H22026", email: "admin@sf.local");

        var response = await CreateService().LoginAsync(new LoginRequest
        {
            EmpresaCodigo = "H2CONV",
            Senha = "H22026"
        });

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response!.AccessToken);
        Assert.Contains(token.Claims, c => c.Type == "usuario_login" && c.Value == "admin@sf.local");
        Assert.Contains(token.Claims, c => c.Type == "empresa_id");
        Assert.Contains(token.Claims, c => c.Type == "empresa_nome");
    }

    [Fact]
    public async Task Login_NaoDiferenciaMaiusculasNoCodigoDaEmpresa()
    {
        var empresa = await SeedEmpresaAsync("H2CONV", "PRODUTO_VER_H2CONV");
        await SeedUsuarioAsync(empresa, "H22026");

        var response = await CreateService().LoginAsync(new LoginRequest
        {
            EmpresaCodigo = "  h2conv  ",
            Senha = "H22026"
        });

        Assert.NotNull(response);
    }

    [Fact]
    public async Task Login_ComSenhaErrada_RetornaNull()
    {
        var empresa = await SeedEmpresaAsync("H2CONV", "PRODUTO_VER_H2CONV");
        await SeedUsuarioAsync(empresa, "H22026");

        var response = await CreateService().LoginAsync(new LoginRequest
        {
            EmpresaCodigo = "H2CONV",
            Senha = "senha-errada"
        });

        Assert.Null(response);
    }

    [Fact]
    public async Task Login_ComCamposVazios_RetornaNull()
    {
        var empresa = await SeedEmpresaAsync("H2CONV", "PRODUTO_VER_H2CONV");
        await SeedUsuarioAsync(empresa, "H22026");

        var service = CreateService();
        Assert.Null(await service.LoginAsync(new LoginRequest { EmpresaCodigo = "", Senha = "" }));
        Assert.Null(await service.LoginAsync(new LoginRequest { EmpresaCodigo = "H2CONV", Senha = "" }));
        Assert.Null(await service.LoginAsync(new LoginRequest { EmpresaCodigo = "", Senha = "H22026" }));
    }

    [Fact]
    public async Task Login_ComEmpresaInexistente_RetornaNull()
    {
        var empresa = await SeedEmpresaAsync("H2CONV", "PRODUTO_VER_H2CONV");
        await SeedUsuarioAsync(empresa, "H22026");

        var response = await CreateService().LoginAsync(new LoginRequest
        {
            EmpresaCodigo = "NAOEXISTE",
            Senha = "H22026"
        });

        Assert.Null(response);
    }

    [Fact]
    public async Task Login_ComEmpresaInativa_RetornaNull()
    {
        var empresa = await SeedEmpresaAsync("EMPRESA_OFF", "PRODUTO_VER_EMPRESA_OFF", ativo: false);
        await SeedUsuarioAsync(empresa, "H22026");

        Assert.Null(await CreateService().LoginAsync(new LoginRequest
        {
            EmpresaCodigo = "EMPRESA_OFF",
            Senha = "H22026"
        }));
    }

    [Fact]
    public async Task Login_ComUsuarioInativo_RetornaNull()
    {
        var empresa = await SeedEmpresaAsync("USUARIO_OFF", "PRODUTO_VER_USUARIO_OFF");
        await SeedUsuarioAsync(empresa, "H22026", ativo: false);

        Assert.Null(await CreateService().LoginAsync(new LoginRequest
        {
            EmpresaCodigo = "USUARIO_OFF",
            Senha = "H22026"
        }));
    }

    [Fact]
    public async Task Login_ComVinculoInativo_RetornaNull()
    {
        var empresa = await SeedEmpresaAsync("VINCULO_OFF", "PRODUTO_VER_VINCULO_OFF");
        await SeedUsuarioAsync(empresa, "H22026", vinculoAtivo: false);

        Assert.Null(await CreateService().LoginAsync(new LoginRequest
        {
            EmpresaCodigo = "VINCULO_OFF",
            Senha = "H22026"
        }));
    }

    [Fact]
    public async Task Login_ComVariosUsuariosNaEmpresa_IdentificaPelaSenha()
    {
        var empresa = await SeedEmpresaAsync("H2CONV", "PRODUTO_VER_H2CONV");
        var admin = await SeedUsuarioAsync(empresa, "senha-do-admin", nome: "Administrador", email: "admin@sf.local");
        var caixa = await SeedUsuarioAsync(empresa, "senha-do-caixa", nome: "Operador de Caixa", email: "caixa@sf.local");

        var service = CreateService();

        var loginAdmin = await service.LoginAsync(new LoginRequest { EmpresaCodigo = "H2CONV", Senha = "senha-do-admin" });
        var loginCaixa = await service.LoginAsync(new LoginRequest { EmpresaCodigo = "H2CONV", Senha = "senha-do-caixa" });

        Assert.NotNull(loginAdmin);
        Assert.NotNull(loginCaixa);
        Assert.Equal(admin.Id, loginAdmin!.UsuarioId);
        Assert.Equal("Administrador", loginAdmin.Nome);
        Assert.Equal(caixa.Id, loginCaixa!.UsuarioId);
        Assert.Equal("Operador de Caixa", loginCaixa.Nome);
        Assert.Equal(empresa.Id, loginAdmin.EmpresaId);
        Assert.Equal(empresa.Id, loginCaixa.EmpresaId);
    }

    [Fact]
    public async Task Login_UsuarioDeOutraEmpresa_NaoAutentica()
    {
        var empresaA = await SeedEmpresaAsync("EMP_A", "PRODUTO_VER_EMP_A");
        var empresaB = await SeedEmpresaAsync("EMP_B", "PRODUTO_VER_EMP_B");
        await SeedUsuarioAsync(empresaA, "senha-a", email: "a@sf.local");
        await SeedUsuarioAsync(empresaB, "senha-b", email: "b@sf.local");

        var service = CreateService();

        // Senha valida, empresa errada: o vinculo e o que define o acesso (isolamento).
        Assert.Null(await service.LoginAsync(new LoginRequest { EmpresaCodigo = "EMP_A", Senha = "senha-b" }));
        Assert.Null(await service.LoginAsync(new LoginRequest { EmpresaCodigo = "EMP_B", Senha = "senha-a" }));
    }

    [Fact]
    public async Task Login_ComHashCorrompido_NaoDerrubaOLogin()
    {
        var empresa = await SeedEmpresaAsync("HASH_RUIM", "PRODUTO_VER_HASH_RUIM");
        var usuario = await SeedUsuarioAsync(empresa, "H22026");
        var usuarioValido = await SeedUsuarioAsync(empresa, "senha-valida", nome: "Valido", email: "valido@sf.local");

        await using (var context = CreateContext())
        {
            var quebrado = await context.Usuarios.FindAsync(usuario.Id);
            quebrado!.SenhaHash = "nao-e-um-hash-bcrypt";
            await context.SaveChangesAsync();
        }

        var response = await CreateService().LoginAsync(new LoginRequest
        {
            EmpresaCodigo = "HASH_RUIM",
            Senha = "senha-valida"
        });

        Assert.NotNull(response);
        Assert.Equal(usuarioValido.Id, response!.UsuarioId);
    }
}
