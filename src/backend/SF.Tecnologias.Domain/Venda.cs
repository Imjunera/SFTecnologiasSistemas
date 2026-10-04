using System;
using System.Collections.Generic;

namespace SF.Tecnologias.Domain
{
    public enum StatusVenda
    {
        Concluida = 0,
        Cancelada = 1
    }

    /// <summary>
    /// Venda finalizada no caixa. Fica vinculada a sessao de caixa aberta no momento
    /// (quando existir) para que o fechamento de expediente considere o total vendido.
    /// </summary>
    public class Venda : BaseEntity, ITenantEntity
    {
        public int EmpresaId { get; set; }
        public virtual Empresa Empresa { get; set; } = default!;

        public int? UsuarioId { get; set; }
        public virtual Usuario? Usuario { get; set; }

        public int? SessaoCaixaId { get; set; }
        public virtual SessaoCaixa? SessaoCaixa { get; set; }

        public int? ClienteId { get; set; }
        public virtual Cliente? Cliente { get; set; }

        public DateTime DataVenda { get; set; } = DateTime.UtcNow;
        public string FormaPagamento { get; set; } = "dinheiro";
        public decimal ValorTotal { get; set; }
        public StatusVenda Status { get; set; } = StatusVenda.Concluida;

        public virtual ICollection<VendaItem> Itens { get; set; } = new List<VendaItem>();
    }

    /// <summary>
    /// Item da venda. Guarda uma copia do nome e do preco praticados na venda, para que
    /// o historico continue integro mesmo que o produto seja editado ou excluido depois.
    /// </summary>
    public class VendaItem : BaseEntity, ITenantEntity
    {
        public int EmpresaId { get; set; }
        public virtual Empresa Empresa { get; set; } = default!;

        public int VendaId { get; set; }
        public virtual Venda Venda { get; set; } = default!;

        public int? ProdutoId { get; set; }
        public virtual Produto? Produto { get; set; }

        public string ProdutoNome { get; set; } = string.Empty;
        public int Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        public decimal Subtotal { get; set; }
    }
}
