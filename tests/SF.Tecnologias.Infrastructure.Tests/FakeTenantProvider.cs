using System;
using SF.Tecnologias.Domain;

namespace SF.Tecnologias.Infrastructure.Tests;

/// <summary>
/// Deterministic tenant provider for multi-tenant isolation tests.
/// </summary>
public class FakeTenantProvider : ITenantProvider
{
    private readonly int? _tenantId;
    private readonly int? _usuarioId;
    public FakeTenantProvider(int? tenantId, int? usuarioId = null) => (_tenantId, _usuarioId) = (tenantId, usuarioId);
    public int? GetTenantId() => _tenantId;
    public int? GetUsuarioId() => _usuarioId;
}
