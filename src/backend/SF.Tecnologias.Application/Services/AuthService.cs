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

            // Find the active user associated with this company
            var usuarioEmpresa = await _context.UsuarioEmpresas
                .Include(ue => ue.Usuario)
                .Include(ue => ue.Empresa)
                .Include(ue => ue.Perfil)
                    .ThenInclude(p => p.Permissoes)
                .FirstOrDefaultAsync(ue =>
                    ue.EmpresaId == empresa.Id &&
                    ue.Ativo &&
                    ue.Empresa.Ativo &&
                    ue.Usuario.Ativo);

            if (usuarioEmpresa == null)
                return null;

            // Verify password
            if (!BCrypt.Net.BCrypt.Verify(request.Senha, usuarioEmpresa.Usuario.SenhaHash))
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

        private string GenerateToken(Usuario usuario, UsuarioEmpresa usuarioEmpresa)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var secretKey = jwtSettings["Secret"];
            var issuer = jwtSettings["Issuer"];
            var audience = jwtSettings["Audience"];

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
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
