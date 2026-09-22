using System;

namespace SF.Tecnologias.Domain
{
    public interface ITenantProvider
    {
        int? GetTenantId();
        int? GetUsuarioId();
    }
}
