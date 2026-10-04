using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SF.Tecnologias.Domain;
using SF.Tecnologias.Infrastructure.Persistence;

namespace SF.Tecnologias.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<LoginResponse?> LoginAsync(LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.EmpresaCodigo) || string.IsNullOrWhiteSpace(request.Senha))
                return null;

            // Find the active company by Codigo
            var empresa = await _context.Empresas
                .FirstOrDefaultAsync(e => e.Ativo && e.Codigo.ToLower() == request.EmpresaCodigo.Trim().ToLower());

            if (empresa == null)
                return null;

            // Vinculos ativos da empresa: quem tem acesso ao sistema.
            // Ordem deterministica (UsuarioId) para o resultado nao depender do banco.
            var candidatos = await _context.UsuarioEmpresas
                .Include(ue => ue.Usuario)
                .Include(ue => ue.Empresa)
                .Include(ue => ue.Perfil)
                    .ThenInclude(p => p.Permissoes)
                .Where(ue =>
                    ue.EmpresaId == empresa.Id &&
                    ue.Ativo &&
                    ue.Empresa.Ativo &&
                    ue.Usuario.Ativo)
                .OrderBy(ue => ue.UsuarioId)
                .ToListAsync();

            if (candidatos.Count == 0)
                return null;

            // Login sem e-mail: a identidade do usuario e o vinculo cuja senha confere.
            // A senha continua sendo verificada por BCrypt contra o hash armazenado.
            UsuarioEmpresa? usuarioEmpresa = null;
            foreach (var candidato in candidatos)
            {
                if (SenhaConfere(request.Senha, candidato.Usuario.SenhaHash))
                {
                    usuarioEmpresa = candidato;
                    break;
                }
            }

            if (usuarioEmpresa == null)
                return null;

            // Generate JWT token
            var token = GenerateToken(usuarioEmpresa.Usuario, usuarioEmpresa);

            return new LoginResponse
            {
                AccessToken = token,
                ExpiresIn = DateTime.UtcNow.AddMinutes(
                    double.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "60")),
                UsuarioId = usuarioEmpresa.Usuario.Id,
                EmpresaId = empresa.Id,
                Nome = usuarioEmpresa.Usuario.Nome,
                Permissoes = usuarioEmpresa.Perfil.Permissoes.Select(p => p.Nome).ToArray()
            };
        }

        /// <summary>
        /// Verifica a senha contra um hash BCrypt. Hash corrompido/ilegivel nao derruba o login
        /// (apenas nao confere), para que um registro invalido nao resulte em 500 para todos.
        /// </summary>
        private static bool SenhaConfere(string senha, string? senhaHash)
        {
            if (string.IsNullOrWhiteSpace(senhaHash))
                return false;

            try
            {
                return BCrypt.Net.BCrypt.Verify(senha, senhaHash);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string GenerateToken(Usuario usuario, UsuarioEmpresa usuarioEmpresa)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var secretKey = jwtSettings["Secret"];
            if (string.IsNullOrWhiteSpace(secretKey) || secretKey.Length < 32)
                throw new InvalidOperationException("Jwt:Secret ausente ou invalido (min 32 caracteres).");
            var issuer = jwtSettings["Issuer"];
            var audience = jwtSettings["Audience"];

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                // Identificador da conta (coluna Usuario.Email). Nao e pedido no login.
                new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
                new Claim("usuario_login", usuario.Email),
                new Claim("empresa_id", usuarioEmpresa.EmpresaId.ToString()),
                new Claim("usuario_nome", usuario.Nome),
                new Claim("empresa_nome", usuarioEmpresa.Empresa.Nome ?? string.Empty)
            };

            // Add permission claims
            foreach (var permissao in usuarioEmpresa.Perfil.Permissoes)
            {
                claims = claims.Append(new Claim("permissao", permissao.Nome)).ToArray();
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(
                    double.Parse(jwtSettings["ExpiryMinutes"] ?? "60")),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
