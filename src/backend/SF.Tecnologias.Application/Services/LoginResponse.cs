using System;

namespace SF.Tecnologias.Application.Services
{
    public class LoginResponse
    {
        public string AccessToken { get; set; } = default!;
        public DateTime ExpiresIn { get; set; }
        public int UsuarioId { get; set; }
        public int EmpresaId { get; set; }
        public string Nome { get; set; } = default!;
        public string[] Permissoes { get; set; } = default!;
    }
}
