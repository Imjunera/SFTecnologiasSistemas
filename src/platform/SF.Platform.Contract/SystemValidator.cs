using System.Text.Json;

namespace SF.Platform.Contract;

/// <summary>
/// Valida um sistema instalado/pacote ANTES de executa-lo ou instala-lo.
/// Sistema invalido nao executa: a plataforma nao adivinha estrutura.
/// </summary>
public static class SystemValidator
{
    /// <summary>
    /// Valida a pasta de um sistema. O id esperado e o nome da pasta
    /// (o id do system.json precisa bater com a pasta — evita clone/renome solto).
    /// </summary>
    /// <param name="systemDirectory">Pasta do sistema (ex.: ...\Systems\h2-conveniencia).</param>
    /// <param name="platformVersion">Versao atual da plataforma; null pula a checagem de compatibilidade.</param>
    public static SystemValidationResult ValidateDirectory(string systemDirectory, string? platformVersion = null)
        => ValidateDirectory(systemDirectory, platformVersion, expectedDataDirectory: null);

    public static SystemValidationResult ValidateDirectory(
        string systemDirectory,
        string? platformVersion,
        string? expectedDataDirectory)
    {
        var result = new SystemValidationResult();

        if (string.IsNullOrWhiteSpace(systemDirectory))
        {
            result.AddError(SystemValidationCodes.SystemDirMissing, "pasta do sistema nao informada");
            return result;
        }

        systemDirectory = Path.GetFullPath(systemDirectory);
        result.SystemDirectory = systemDirectory;

        if (!Directory.Exists(systemDirectory))
        {
            result.AddError(SystemValidationCodes.SystemDirMissing, $"pasta nao encontrada: {systemDirectory}");
            return result;
        }

        var folderId = Path.GetFileName(systemDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (!ValidateStructure(systemDirectory, result))
            return result;

        var manifest = ReadManifest(systemDirectory, result);
        if (manifest is null)
            return result;

        result.Manifest = manifest;

        ValidateId(manifest, folderId, result);
        ValidateEntryPoint(systemDirectory, manifest, result);
        ValidateDataDirectory(manifest, folderId, result, expectedDataDirectory);

        var version = ReadVersion(systemDirectory, result);
        if (version is null)
            return result;

        result.Version = version;

        if (!VersionRules.IsValid(manifest.SystemVersion))
            result.AddError(SystemValidationCodes.SystemVersionInvalid,
                $"system.json: systemVersion invalida ('{manifest.SystemVersion}')");

        if (!VersionRules.IsValid(version.Version))
            result.AddError(SystemValidationCodes.VersionFileVersionInvalid,
                $"version.json: version invalida ('{version.Version}')");

        if (!string.IsNullOrWhiteSpace(version.SystemId) &&
            !string.Equals(version.SystemId, manifest.Id, StringComparison.Ordinal))
        {
            result.AddError(SystemValidationCodes.VersionFileIdMismatch,
                $"version.json: systemId '{version.SystemId}' difere de system.json '{manifest.Id}'");
        }

        if (!VersionRules.IsPlatformCompatible(manifest.PlatformVersion, platformVersion))
        {
            result.AddError(SystemValidationCodes.PlatformIncompatible,
                $"sistema exige plataforma >= {manifest.PlatformVersion}; plataforma atual: {platformVersion ?? "desconhecida"}");
        }

        if (result.IsValid)
        {
            if (!string.IsNullOrWhiteSpace(manifest.EntryPoint))
            {
                result.EntryPointPath = Path.Combine(systemDirectory, NormalizeRelative(manifest.EntryPoint));
            }
            result.DataDirectory = ResolveDataDirectory(manifest, folderId);
        }

        return result;
    }

    /// <summary>Valida um pacote ja extraido em pasta temporaria (pre-instalacao).</summary>
    public static SystemValidationResult ValidatePackageDirectory(
        string extractedPackageDirectory,
        string expectedSystemId,
        string? platformVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedSystemId))
        {
            var invalid = new SystemValidationResult();
            invalid.AddError(SystemValidationCodes.IdMissing, "system-id esperado nao informado");
            return invalid;
        }

        var result = ValidateDirectory(extractedPackageDirectory, platformVersion);

        // Em um pacote o id tambem precisa bater com o id do manifesto de distribuicao.
        if (result.Manifest is not null &&
            !string.Equals(result.Manifest.Id, expectedSystemId, StringComparison.Ordinal))
        {
            result.AddError(SystemValidationCodes.IdMismatch,
                $"pacote declara id '{result.Manifest.Id}', esperado '{expectedSystemId}'");
        }

        return result;
    }

    private static bool ValidateStructure(string systemDirectory, SystemValidationResult result)
    {
        var ok = true;

        // Validar estrutura basica (sempre obrigatoria)
        foreach (var required in SystemContract.RequiredDirectories)
        {
            if (!Directory.Exists(Path.Combine(systemDirectory, required.Replace('/', Path.DirectorySeparatorChar))))
            {
                result.AddError(SystemValidationCodes.RequiredDirectoryMissing,
                    $"estrutura obrigatoria ausente: {required}/");
                ok = false;
            }
        }

        // Se tem entryPoint, validar estrutura completa
        var manifestPath = Path.Combine(systemDirectory, SystemContract.ManifestFileName);
        var hasEntryPoint = false;
        if (File.Exists(manifestPath))
        {
            var json = File.ReadAllText(manifestPath);
            try
            {
                var manifest = JsonSerializer.Deserialize<SystemManifest>(json, JsonOptions);
                hasEntryPoint = !string.IsNullOrWhiteSpace(manifest?.EntryPoint);
            }
            catch { /* ignore */ }
        }

        if (hasEntryPoint)
        {
            foreach (var required in SystemContract.RequiredDirectoriesWithEntryPoint)
            {
                if (!Directory.Exists(Path.Combine(systemDirectory, required.Replace('/', Path.DirectorySeparatorChar))))
                {
                    result.AddError(SystemValidationCodes.RequiredDirectoryMissing,
                        $"estrutura obrigatoria ausente (entryPoint declarado): {required}/");
                    ok = false;
                }
            }
        }

        return ok;
    }

    private static SystemManifest? ReadManifest(string systemDirectory, SystemValidationResult result)
    {
        var manifestPath = Path.Combine(systemDirectory, SystemContract.ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            result.AddError(SystemValidationCodes.ManifestMissing, $"{SystemContract.ManifestFileName} nao encontrado");
            return null;
        }

        var json = ReadMetadataFile(manifestPath, SystemValidationCodes.ManifestTooLarge,
            SystemValidationCodes.ManifestInvalidJson, result);
        if (json is null)
            return null;

        try
        {
            var manifest = JsonSerializer.Deserialize<SystemManifest>(json, JsonOptions);
            if (manifest is null)
            {
                result.AddError(SystemValidationCodes.ManifestInvalidJson, "system.json vazio");
                return null;
            }

            return manifest;
        }
        catch (JsonException ex)
        {
            result.AddError(SystemValidationCodes.ManifestInvalidJson, $"system.json invalido: {ex.Message}");
            return null;
        }
    }

    private static SystemVersionInfo? ReadVersion(string systemDirectory, SystemValidationResult result)
    {
        var versionPath = Path.Combine(systemDirectory, SystemContract.VersionFileName);

        if (!File.Exists(versionPath))
        {
            result.AddError(SystemValidationCodes.VersionFileMissing, $"{SystemContract.VersionFileName} nao encontrado");
            return null;
        }

        var json = ReadMetadataFile(versionPath, SystemValidationCodes.ManifestTooLarge,
            SystemValidationCodes.VersionFileInvalidJson, result);
        if (json is null)
            return null;

        try
        {
            var version = JsonSerializer.Deserialize<SystemVersionInfo>(json, JsonOptions);
            if (version is null)
            {
                result.AddError(SystemValidationCodes.VersionFileInvalidJson, "version.json vazio");
                return null;
            }

            return version;
        }
        catch (JsonException ex)
        {
            result.AddError(SystemValidationCodes.VersionFileInvalidJson, $"version.json invalido: {ex.Message}");
            return null;
        }
    }

    private static string? ReadMetadataFile(
        string path,
        string tooLargeCode,
        string invalidCode,
        SystemValidationResult result)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > SystemContract.MaxMetadataFileBytes)
            {
                result.AddError(tooLargeCode, $"arquivo de metadados muito grande: {path}");
                return null;
            }

            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.AddError(invalidCode, $"nao foi possivel ler {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    private static void ValidateId(SystemManifest manifest, string folderId, SystemValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            result.AddError(SystemValidationCodes.IdMissing, "system.json: id ausente");
            return;
        }

        if (!IsValidSystemId(manifest.Id))
        {
            result.AddError(SystemValidationCodes.IdInvalid,
                $"system.json: id invalido ('{manifest.Id}'); use kebab-case (a-z, 0-9, '-')");
            return;
        }

        if (!string.Equals(manifest.Id, folderId, StringComparison.Ordinal))
        {
            result.AddError(SystemValidationCodes.IdMismatch,
                $"system.json: id '{manifest.Id}' difere da pasta '{folderId}'");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
            result.AddError(SystemValidationCodes.NameMissing, "system.json: name ausente");
    }

    private static void ValidateEntryPoint(string systemDirectory, SystemManifest manifest, SystemValidationResult result)
    {
        // entryPoint e opcional: se omitido, a plataforma usa a API compartilhada
        if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
            return;

        var raw = manifest.EntryPoint.Trim().Replace('\\', '/');

        if (Path.IsPathRooted(raw) || raw.Contains(':') || raw.Split('/').Any(p => p == ".."))
        {
            result.AddError(SystemValidationCodes.EntryPointInvalid,
                $"system.json: entryPoint deve ser relativo ao sistema ('{manifest.EntryPoint}')");
            return;
        }

        if (!raw.StartsWith(SystemContract.EntryPointFolder + "/", StringComparison.Ordinal))
        {
            result.AddError(SystemValidationCodes.EntryPointInvalid,
                $"system.json: entryPoint deve ficar em {SystemContract.EntryPointFolder}/ ('{manifest.EntryPoint}')");
            return;
        }

        var fullPath = Path.Combine(systemDirectory, raw.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            result.AddError(SystemValidationCodes.EntryPointNotFound,
                $"entryPoint nao encontrado: {manifest.EntryPoint}");
        }
    }

    private static void ValidateDataDirectory(
        SystemManifest manifest,
        string folderId,
        SystemValidationResult result,
        string? expectedDataDirectory)
    {
        var declared = manifest.DataDirectory;
        var canonical = $"{SystemContract.DataFolderName}/{folderId}";

        if (string.IsNullOrWhiteSpace(declared))
        {
            // Ausente ⇒ a plataforma usa o canonico (data/<id>).
            if (expectedDataDirectory is not null &&
                !string.Equals(expectedDataDirectory, canonical, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(SystemValidationCodes.DataDirectoryInvalid,
                    $"dataDirectory esperado '{expectedDataDirectory}' difere do canonico '{canonical}'");
            }
            return;
        }

        var normalized = NormalizeRelative(declared);

        if (normalized.Length == 0 || Path.IsPathRooted(declared) || normalized.Split('/').Any(p => p == ".."))
        {
            result.AddError(SystemValidationCodes.DataDirectoryInvalid,
                $"system.json: dataDirectory invalido ('{declared}')");
            return;
        }

        // Isolamento: um sistema nunca aponta para a area de dados de outro sistema.
        var prefix = canonical + "/";
        if (!normalized.Equals(canonical, StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            result.AddError(SystemValidationCodes.DataDirectoryNotIsolated,
                $"system.json: dataDirectory deve ficar em {canonical} ('{declared}')");
            return;
        }

        if (expectedDataDirectory is not null &&
            !normalized.Equals(NormalizeRelative(expectedDataDirectory), StringComparison.OrdinalIgnoreCase))
        {
            result.AddError(SystemValidationCodes.DataDirectoryNotIsolated,
                $"dataDirectory '{normalized}' difere do esperado '{NormalizeRelative(expectedDataDirectory)}'");
        }
    }

    /// <summary>Resolve a pasta de dados canonica do sistema declarada no contrato.</summary>
    public static string ResolveDataDirectory(SystemManifest manifest, string systemId)
    {
        var declared = manifest.DataDirectory;
        if (string.IsNullOrWhiteSpace(declared))
            return $"{SystemContract.DataFolderName}/{systemId}";

        var normalized = NormalizeRelative(declared);
        return normalized.Length == 0 ? $"{SystemContract.DataFolderName}/{systemId}" : normalized;
    }

    /// <summary>Id de sistema valido: kebab-case (a-z, 0-9 e '-' entre segmentos).</summary>
    public static bool IsValidSystemId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
            return false;

        if (id.StartsWith('-') || id.EndsWith('-') || id.Contains("--"))
            return false;

        foreach (var c in id)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
                return false;
        }

        return true;
    }

    private static string NormalizeRelative(string value) =>
        value.Trim().Replace('\\', '/').Trim('/');

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
