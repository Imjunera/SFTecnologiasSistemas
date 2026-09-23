using SF.Tecnologias.Domain;
using Microsoft.AspNetCore.Http;
using System;

namespace SF.Tecnologias.Infrastructure.Services
{
    public class TenantProvider : ITenantProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TenantProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public int? GetTenantId()
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return null;

            var tenantClaim = context.User.FindFirst("empresa_id");
            if (tenantClaim != null && int.TryParse(tenantClaim.Value, out var tenantId))
            {
                return tenantId;
            }

            return null;
        }

        public int? GetUsuarioId()
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return null;

            var subClaim = context.User.FindFirst("sub");
            if (subClaim != null && int.TryParse(subClaim.Value, out var usuarioId))
            {
                return usuarioId;
            }

            return null;
        }
    }
}
