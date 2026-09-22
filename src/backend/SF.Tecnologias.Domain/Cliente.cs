using System;

namespace SF.Tecnologias.Domain
{
    public class Cliente : BaseEntity, ITenantEntity
    {
        public int EmpresaId { get; set; }
        public virtual Empresa Empresa { get; set; } = default!;

        public string Nome { get; set; } = string.Empty;
        public string? Telefone { get; set; }
        public string? Cpf { get; set; }
        public string? Email { get; set; }
        public string? Endereco { get; set; }
        public string? Observacoes { get; set; }
    }
}
