using System;
using System.Collections.Generic;

namespace SF.Tecnologias.Domain
{
    public class Empresa : BaseEntity
    {
        public string Codigo { get; set; } = default!; // Used for login
        public string Nome { get; set; } = default!;

        // Navigation properties
        public ICollection<UsuarioEmpresa> UsuarioEmpresas { get; set; } = new List<UsuarioEmpresa>();
    }
}
