using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SF.Tecnologias.Domain;

namespace SF.Tecnologias.Infrastructure.Persistence
{
    /// <summary>
    /// Idempotent development seed. Runs only in the Development environment.
    /// Justification: enables login flow validation (EMPRESA_ID + senha) and
    /// permission-based module access in local development without manual SQL.
    /// Safe to run multiple times: each step checks for existing data first.
    /// </summary>
    public class DevelopmentSeeder
    {
        public const string EmpresaCodigo = "H2CONV";
        public const string EmpresaNome = "H2 Conveniência";
        public const string AdminEmail = "H2CONV";
        public const string AdminSenha = "H22026";

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

        public DevelopmentSeeder(AppDbContext context)
        {
            _context = context;
        }

        public async Task SeedAsync()
        {
            await EnsurePermissoesAsync();
            await EnsureEmpresaAsync();
            await EnsurePerfilAdminAsync();
            await EnsureUsuarioAdminAsync();
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
            var empresa = await _context.Empresas.FirstOrDefaultAsync(e => e.Codigo == EmpresaCodigo);
            if (empresa is null)
            {
                empresa = new Empresa { Codigo = EmpresaCodigo, Nome = EmpresaNome };
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

        private async Task EnsureUsuarioAdminAsync()
        {
            var usuario = await _context.Usuarios
                .Include(u => u.UsuarioEmpresas)
                .FirstOrDefaultAsync(u => u.Email == AdminEmail);
            if (usuario is null)
            {
                usuario = new Usuario
                {
                    Nome = "Administrador",
                    Email = AdminEmail,
                    SenhaHash = BCrypt.Net.BCrypt.HashPassword(AdminSenha)
                };
                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();
            }

            var empresa = await _context.Empresas.SingleAsync(e => e.Codigo == EmpresaCodigo);
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
