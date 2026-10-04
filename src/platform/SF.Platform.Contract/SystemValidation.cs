namespace SF.Platform.Contract;

/// <summary>Codigos estaveis de validacao (usados em logs, testes e mensagens ao usuario).</summary>
public static class SystemValidationCodes
{
    public const string SystemDirMissing = "system_dir_missing";
    public const string ManifestMissing = "system_manifest_missing";
    public const string ManifestInvalidJson = "system_manifest_invalid_json";
    public const string ManifestTooLarge = "system_manifest_too_large";
    public const string IdMissing = "system_id_missing";
    public const string IdInvalid = "system_id_invalid";
    public const string IdMismatch = "system_id_mismatch";
    public const string NameMissing = "system_name_missing";
    public const string SystemVersionInvalid = "system_version_invalid";
    public const string VersionFileMissing = "version_file_missing";
    public const string VersionFileInvalidJson = "version_file_invalid_json";
    public const string VersionFileIdMismatch = "version_file_id_mismatch";
    public const string VersionFileVersionInvalid = "version_file_version_invalid";
    public const string RequiredDirectoryMissing = "required_directory_missing";
    public const string RecommendedDirectoryMissing = "recommended_directory_missing";
    public const string EntryPointMissing = "entry_point_missing";
    public const string EntryPointInvalid = "entry_point_invalid";
    public const string EntryPointNotFound = "entry_point_not_found";
    public const string DataDirectoryInvalid = "data_directory_invalid";
    public const string DataDirectoryNotIsolated = "data_directory_not_isolated";
    public const string PlatformIncompatible = "platform_incompatible";
}

public sealed class SystemValidationIssue
{
    public SystemValidationIssue(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public string Code { get; }

    public string Message { get; }

    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>Resultado da validacao de um sistema. Sistema invalido nunca deve ser executado.</summary>
public sealed class SystemValidationResult
{
    private readonly List<SystemValidationIssue> _errors = new();
    private readonly List<SystemValidationIssue> _warnings = new();

    public bool IsValid => _errors.Count == 0;

    public SystemManifest? Manifest { get; internal set; }

    public SystemVersionInfo? Version { get; internal set; }

    public string? SystemDirectory { get; internal set; }

    /// <summary>Caminho absoluto do entry point (apenas quando <see cref="IsValid"/>).</summary>
    public string? EntryPointPath { get; internal set; }

    /// <summary>Pasta de dados canonica do sistema (apenas quando <see cref="IsValid"/>).</summary>
    public string? DataDirectory { get; internal set; }

    public IReadOnlyList<SystemValidationIssue> Errors => _errors;

    public IReadOnlyList<SystemValidationIssue> Warnings => _warnings;

    public string? InstalledVersion => Version?.Version;

    internal void AddError(string code, string message) => _errors.Add(new SystemValidationIssue(code, message));

    internal void AddWarning(string code, string message) => _warnings.Add(new SystemValidationIssue(code, message));

    public string Describe()
    {
        if (IsValid && _warnings.Count == 0)
            return "sistema valido";

        var parts = new List<string>();
        parts.AddRange(_errors.Select(e => "erro " + e));
        parts.AddRange(_warnings.Select(w => "aviso " + w));
        return string.Join("; ", parts);
    }
}
