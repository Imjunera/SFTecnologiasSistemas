namespace SF.Platform.Contract;

/// <summary>
/// Regras de versao do contrato (compatibilidade plataforma x sistema).
/// Mantem a mesma semantica ja usada pelo updater (System.Version).
/// </summary>
public static class VersionRules
{
    /// <summary>Converte "1.2.3" / "1.2.3.4" em Version. Retorna false quando invalido.</summary>
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];

        // Normaliza para 4 partes: comparacoes entre 1.0 e 1.0.0.0 sao equivalentes.
        var partes = normalized.Split('.');
        if (partes.Length is < 1 or > 4)
            return false;

        foreach (var parte in partes)
        {
            if (parte.Length == 0 || !parte.All(char.IsDigit))
                return false;
        }

        var preenchido = string.Join('.', partes.Concat(Enumerable.Repeat("0", 4 - partes.Length)));
        return Version.TryParse(preenchido, out version!);
    }

    public static bool IsValid(string? value) => TryParse(value, out _);

    /// <summary>
    /// true quando <paramref name="requiredPlatformVersion"/> (minimo exigido pelo sistema)
    /// e suportado pela versao atual da plataforma.
    /// </summary>
    public static bool IsPlatformCompatible(string? requiredPlatformVersion, string? platformVersion)
    {
        // Sem exigencia declarada ⇒ compativel (sistema legado/simples).
        if (string.IsNullOrWhiteSpace(requiredPlatformVersion))
            return true;

        if (!TryParse(requiredPlatformVersion, out var required))
            return false;

        // Plataforma sem versao legivel nao pode atestar compatibilidade.
        if (!TryParse(platformVersion, out var current))
            return false;

        return required <= current;
    }

    /// <summary>true quando <paramref name="candidate"/> e mais recente que <paramref name="current"/>.</summary>
    public static bool IsNewer(string candidate, string current)
    {
        if (!TryParse(candidate, out var c) || !TryParse(current, out var b))
            return false;

        return c > b;
    }

    /// <summary>true quando as duas versoes sao equivalentes (1.0 == 1.0.0.0).</summary>
    public static bool AreEquivalent(string? left, string? right)
    {
        if (!TryParse(left, out var a) || !TryParse(right, out var b))
            return false;

        return a == b;
    }
}
