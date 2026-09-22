using System;
using System.Collections.Generic;

namespace SF.Tecnologias.Domain
{
    public class Perfil : BaseEntity
    {
        public string Nome { get; set; } = default!;
        public string? Descricao { get; set; }

        // Navigation properties
        public ICollection<UsuarioEmpresa> UsuarioEmpresas { get; set; } = new List<UsuarioEmpresa>();
        public ICollection<Permissao> Permissoes { get; set; } = new List<Permissao>();
    }
}
