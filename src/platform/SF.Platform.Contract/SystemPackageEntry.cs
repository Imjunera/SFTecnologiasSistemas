using System.Text.Json.Serialization;

namespace SF.Platform.Contract;

/// <summary>
/// Entrada de um sistema no manifesto de release (distribuicao via GitHub Releases).
/// Mesmo contrato usado pelo gerador de manifesto, pelo instalador e pelo updater.
/// </summary>
public sealed class SystemPackageEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Versao minima da plataforma exigida pelo pacote.</summary>
    [JsonPropertyName("minimumPlatformVersion")]
    public string? MinimumPlatformVersion { get; set; }

    [JsonPropertyName("packageUrl")]
    public string? PackageUrl { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; set; }

    /// <summary>Sistema obrigatorio: se falhar, a atualizacao nao e concluida.</summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    /// <summary>Campos minimos para instalar/atualizar o sistema com seguranca.</summary>
    public IReadOnlyList<string> ValidateForDistribution()
    {
        var errors = new List<string>();

        if (!SystemValidator.IsValidSystemId(Id))
            errors.Add("systems[]: id invalido (kebab-case obrigatorio)");

        if (!VersionRules.IsValid(Version))
            errors.Add($"systems[{Id}]: version invalida ('{Version}')");

        if (string.IsNullOrWhiteSpace(PackageUrl))
            errors.Add($"systems[{Id}]: packageUrl ausente");

        if (string.IsNullOrWhiteSpace(Sha256) || Sha256.Trim().Length != 64)
            errors.Add($"systems[{Id}]: sha256 ausente/invalido");

        if (!string.IsNullOrWhiteSpace(MinimumPlatformVersion) && !VersionRules.IsValid(MinimumPlatformVersion))
            errors.Add($"systems[{Id}]: minimumPlatformVersion invalida ('{MinimumPlatformVersion}')");

        return errors;
    }

    /// <summary>true quando a plataforma atual atende o minimo exigido pelo pacote.</summary>
    public bool IsCompatibleWith(string? platformVersion) =>
        VersionRules.IsPlatformCompatible(MinimumPlatformVersion, platformVersion);
}
