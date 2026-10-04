using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SF.Tecnologias.Domain;

namespace SF.Tecnologias.Infrastructure.Persistence
{
    /// <summary>
    /// Bootstrap idempotente de uma INSTALACAO NOVA (banco sem nenhuma empresa).
    ///
    /// Roda em TODOS os ambientes: em Production e a unica forma de existir um primeiro
    /// acesso (nao existe endpoint nem instalador que crie a primeira empresa/usuário).
    /// Se ja existe alguma empresa, este metodo nao faz nada — nunca mexe em dado existente.
    ///
    /// Credenciais configuraveis via secao "Seed" (ou variaveis de ambiente Seed__*).
    /// </summary>
    public class BootstrapSeeder
    {
        public const string EmpresaCodigoPadrao = "H2CONV";
        public const string EmpresaNomePadrao = "H2 Conveniência";
        public const string AdminEmailPadrao = "H2CONV";
        public const string AdminSenhaPadrao = "H22026";

        private static readonly (string Nome, string Descricao)[] Permissoes =
        {
            ("PRODUTO_VER", "Visualizar produtos"),
            ("PRODUTO_CADASTRAR", "Cadastrar produtos"),
            ("PRODUTO_EDITAR", "Editar produtos"),
            ("PRODUTO_INATIVAR", "Inativar produtos"),
            ("CATEGORIA_VER", "Visualizar categorias"),
            ("CATEGORIA_CADASTRAR", "Cadastrar categorias"),
            ("CATEGORIA_EDITAR", "Editar categorias"),
            ("CATEGORIA_INATIVAR", "Inativar categorias"),
            ("CLIENTE_VER", "Visualizar clientes"),
            ("CLIENTE_CADASTRAR", "Cadastrar clientes"),
            ("CLIENTE_EDITAR", "Editar clientes"),
            ("CLIENTE_INATIVAR", "Inativar clientes"),
            ("MESA_VER", "Visualizar mesas"),
            ("MESA_CADASTRAR", "Cadastrar mesas"),
            ("MESA_EDITAR", "Editar mesas"),
            ("MESA_INATIVAR", "Inativar mesas"),
            ("CAIXA_VER", "Visualizar sessão de caixa"),
            ("CAIXA_ABRIR", "Abrir sessão de caixa"),
            ("CAIXA_FECHAR", "Fechar sessão de caixa"),
        };

        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BootstrapSeeder> _logger;

        public BootstrapSeeder(AppDbContext context, IConfiguration configuration, ILogger<BootstrapSeeder> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SeedAsync()
        {
            if (await _context.Empresas.AnyAsync())
            {
                _logger.LogDebug("Bootstrap: banco ja possui empresa, nada a fazer.");
                return;
            }

            _logger.LogInformation("Bootstrap: banco vazio, criando primeira empresa/usuário de acesso.");

            var senhaPadrao = string.IsNullOrWhiteSpace(_configuration["Seed:AdminSenha"])
                ? AdminSenhaPadrao
                : _configuration["Seed:AdminSenha"]!.Trim();

            if (senhaPadrao == AdminSenhaPadrao)
            {
                _logger.LogWarning(
                    "Bootstrap: usando a senha padrao {Senha} para o usuario administrador. " +
                    "Altere a senha em uso ou defina Seed:AdminSenha (variavel de ambiente Seed__AdminSenha) antes da primeira execucao em producao.",
                    AdminSenhaPadrao);
            }

            await EnsurePermissoesAsync();
            await EnsureEmpresaAsync();
            await EnsurePerfilAdminAsync();
            await EnsureUsuarioAdminAsync(senhaPadrao);

            _logger.LogInformation(
                "Bootstrap concluido: empresa {Codigo} e usuario administrador {Email} criados.",
                Config("Seed:EmpresaCodigo", EmpresaCodigoPadrao),
                Config("Seed:AdminEmail", AdminEmailPadrao));
        }

        private string Config(string key, string padrao)
        {
            var valor = _configuration[key];
            return string.IsNullOrWhiteSpace(valor) ? padrao : valor.Trim();
        }

        private async Task EnsurePermissoesAsync()
        {
            var existing = await _context.Permissoes.Select(p => p.Nome).ToListAsync();
            var missing = Permissoes
                .Where(x => !existing.Contains(x.Nome))
                .Select(x => new Permissao { Nome = x.Nome, Descricao = x.Descricao })
                .ToList();
            if (missing.Count > 0)
            {
                _context.Permissoes.AddRange(missing);
                await _context.SaveChangesAsync();
            }
        }

        private async Task<Empresa> EnsureEmpresaAsync()
        {
            var codigo = Config("Seed:EmpresaCodigo", EmpresaCodigoPadrao);
            var empresa = await _context.Empresas.FirstOrDefaultAsync(e => e.Codigo == codigo);
            if (empresa is null)
            {
                empresa = new Empresa { Codigo = codigo, Nome = Config("Seed:EmpresaNome", EmpresaNomePadrao) };
                _context.Empresas.Add(empresa);
                await _context.SaveChangesAsync();
            }
            return empresa;
        }

        private async Task<Perfil> EnsurePerfilAdminAsync()
        {
            var perfil = await _context.Perfis
                .Include(p => p.Permissoes)
                .FirstOrDefaultAsync(p => p.Nome == "Administrador");
            if (perfil is null)
            {
                perfil = new Perfil { Nome = "Administrador", Descricao = "Acesso total" };
                _context.Perfis.Add(perfil);
                await _context.SaveChangesAsync();
            }
            if (perfil.Permissoes.Count == 0)
            {
                var todas = await _context.Permissoes.ToListAsync();
                foreach (var permissao in todas)
                    perfil.Permissoes.Add(permissao);
                await _context.SaveChangesAsync();
            }
            return perfil;
        }

        private async Task EnsureUsuarioAdminAsync(string senha)
        {
            var email = Config("Seed:AdminEmail", AdminEmailPadrao);
            var usuario = await _context.Usuarios
                .Include(u => u.UsuarioEmpresas)
                .FirstOrDefaultAsync(u => u.Email == email);
            if (usuario is null)
            {
                usuario = new Usuario
                {
                    Nome = "Administrador",
                    Email = email,
                    SenhaHash = BCrypt.Net.BCrypt.HashPassword(senha)
                };
                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();
            }

            var empresaCodigo = Config("Seed:EmpresaCodigo", EmpresaCodigoPadrao);
            var empresa = await _context.Empresas.SingleAsync(e => e.Codigo == empresaCodigo);
            var perfil = await _context.Perfis.SingleAsync(p => p.Nome == "Administrador");
            if (!usuario.UsuarioEmpresas.Any(ue => ue.EmpresaId == empresa.Id))
            {
                usuario.UsuarioEmpresas.Add(new UsuarioEmpresa
                {
                    UsuarioId = usuario.Id,
                    EmpresaId = empresa.Id,
                    PerfilId = perfil.Id
                });
                await _context.SaveChangesAsync();
            }
        }
    }
}
