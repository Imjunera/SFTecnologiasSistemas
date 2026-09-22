using System;

namespace SF.Tecnologias.Domain
{
    public abstract class BaseEntity
    {
        public int Id { get; set; }
        public bool Ativo { get; set; } = true;
        public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
        public DateTime? AtualizadoEm { get; set; }
    }
}
