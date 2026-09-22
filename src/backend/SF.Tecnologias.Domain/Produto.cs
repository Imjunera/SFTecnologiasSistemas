using System;

namespace SF.Tecnologias.Domain
{
    public class Produto : BaseEntity, ITenantEntity
    {
        public int EmpresaId { get; set; }
        public virtual Empresa Empresa { get; set; } = default!;

        public int? CategoriaId { get; set; }
        public virtual Categoria? Categoria { get; set; }

        public string Nome { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public decimal PrecoVenda { get; set; }
        public decimal? PrecoCusto { get; set; }
    }
}

