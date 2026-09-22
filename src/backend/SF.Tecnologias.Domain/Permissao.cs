using System;

namespace SF.Tecnologias.Domain
{
    public class Permissao : BaseEntity
    {
        public string Nome { get; set; } = default!; // e.g., "PRODUTO_CADASTRAR"
        public string? Descricao { get; set; }
    }
}
