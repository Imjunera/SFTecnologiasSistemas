using System;

namespace SF.Tecnologias.Domain
{
    public enum StatusSessaoCaixa
    {
        Aberta = 0,
        Fechada = 1
    }

    public class SessaoCaixa : BaseEntity, ITenantEntity
    {
        public int EmpresaId { get; set; }
        public virtual Empresa Empresa { get; set; } = default!;

        public int UsuarioId { get; set; }
        public virtual Usuario Usuario { get; set; } = default!;

        public int? MesaId { get; set; }
        public virtual Mesa? Mesa { get; set; }

        public DateTime DataAbertura { get; set; } = DateTime.UtcNow;
        public decimal ValorInicial { get; set; }
        public DateTime? DataFechamento { get; set; }
        public decimal? ValorFechamento { get; set; }
        public StatusSessaoCaixa Status { get; set; } = StatusSessaoCaixa.Aberta;
    }
}
