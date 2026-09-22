using System;

namespace SF.Tecnologias.Domain
{
    public enum StatusMesa
    {
        Livre = 0,
        Ocupada = 1,
        Reservada = 2
    }

    public class Mesa : BaseEntity, ITenantEntity
    {
        public int EmpresaId { get; set; }
        public virtual Empresa Empresa { get; set; } = default!;

        public int Numero { get; set; }
        public string? Descricao { get; set; }
        public int Capacidade { get; set; } = 4;
        public StatusMesa Status { get; set; } = StatusMesa.Livre;
    }
}
