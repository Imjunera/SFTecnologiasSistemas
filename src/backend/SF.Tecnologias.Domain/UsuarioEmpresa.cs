using System;

namespace SF.Tecnologias.Domain
{
    public class UsuarioEmpresa : BaseEntity, ITenantEntity
    {
        public int UsuarioId { get; set; }
        public Usuario Usuario { get; set; } = default!;
        public int EmpresaId { get; set; }
        public Empresa Empresa { get; set; } = default!;
        public int PerfilId { get; set; }
        public Perfil Perfil { get; set; } = default!;
    }
}
