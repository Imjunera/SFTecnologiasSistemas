using System;
using System.Collections.Generic;

namespace SF.Tecnologias.Domain
{
    public class Usuario : BaseEntity
    {
        public string Nome { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string SenhaHash { get; set; } = default!;

        // Navigation properties
        public ICollection<UsuarioEmpresa> UsuarioEmpresas { get; set; } = new List<UsuarioEmpresa>();
    }
}
