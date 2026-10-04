namespace SF.Platform.Contract;

/// <summary>
/// Layout fisico da plataforma:
/// <code>
/// InstallDir\                     ← binarios (plataforma + sistemas) — substituiveis
///   SF Tecnologias BETA.exe       ← plataforma (UI)
///   SF.Updater.exe                ← atualizador
///   version.json                  ← versao da plataforma
///   Systems\&lt;system-id&gt;\           ← um sistema por pasta (software)
///
/// DataRoot\                       ← dados — NUNCA substituidos por atualizacao
///   data\&lt;system-id&gt;\              ← dados isolados de cada sistema
/// </code>
/// </summary>
public sealed class PlatformLayout
{
    public PlatformLayout(string installDir, string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(installDir))
            throw new ArgumentException("installDir obrigatorio", nameof(installDir));
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new ArgumentException("dataRoot obrigatorio", nameof(dataRoot));

        InstallDir = Path.GetFullPath(installDir);
        DataRoot = Path.GetFullPath(dataRoot);
    }

    /// <summary>
    /// Monta o layout a partir da pasta de dados (ex.: %ProgramData%\SF Tecnologias\data).
    /// Aceita tanto a raiz de dados quanto a propria pasta <c>data</c>,
    /// o que mantem compatibilidade com o argumento <c>--data-dir</c> do updater.
    /// </summary>
    public static PlatformLayout FromDataDirectory(string installDir, string dataDirectory)
    {
        var full = Path.GetFullPath(dataDirectory);
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);

        var dataRoot = string.Equals(leaf, SystemContract.DataFolderName, StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(trimmed)?.FullName ?? trimmed
            : trimmed;

        return new PlatformLayout(installDir, dataRoot);
    }

    /// <summary>Raiz dos binarios (ex.: %ProgramFiles%\SF Tecnologias).</summary>
    public string InstallDir { get; }

    /// <summary>Raiz dos dados (ex.: %ProgramData%\SF Tecnologias).</summary>
    public string DataRoot { get; }

    public string SystemsDir => Path.Combine(InstallDir, SystemContract.SystemsFolderName);

    public string PlatformVersionFile => Path.Combine(InstallDir, SystemContract.VersionFileName);

    public string UpdaterExecutable => Path.Combine(InstallDir, "SF.Updater.exe");

    public string DesktopExecutable => Path.Combine(InstallDir, "SF Tecnologias BETA.exe");

    public string SystemDir(string systemId) => Path.Combine(SystemsDir, RequireSafeSegment(systemId));

    public string SystemManifestFile(string systemId) =>
        Path.Combine(SystemDir(systemId), SystemContract.ManifestFileName);

    public string SystemVersionFile(string systemId) =>
        Path.Combine(SystemDir(systemId), SystemContract.VersionFileName);

    /// <summary>Pasta de dados canonica do sistema: <c>DataRoot\data\&lt;system-id&gt;</c>.</summary>
    public string SystemDataDir(string systemId) =>
        Path.Combine(DataRoot, SystemContract.DataFolderName, RequireSafeSegment(systemId));

    /// <summary>Pasta de dados declarada no contrato do sistema (respeita <c>dataDirectory</c>).</summary>
    public string SystemDataDir(string systemId, SystemManifest? manifest)
    {
        var declared = manifest?.DataDirectory;
        if (string.IsNullOrWhiteSpace(declared))
            return SystemDataDir(systemId);

        var normalized = declared.Replace('\\', '/').Trim('/');
        return Path.Combine(DataRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Resolve um caminho relativo dentro da pasta de dados do sistema.</summary>
    public string SystemDataPath(string systemId, params string[] relativeParts)
    {
        var parts = new List<string> { DataRoot, SystemContract.DataFolderName, RequireSafeSegment(systemId) };
        foreach (var part in relativeParts)
        {
            if (string.IsNullOrWhiteSpace(part) || part.Contains("..") || Path.IsPathRooted(part))
                throw new ArgumentException($"Segmento de caminho invalido: '{part}'", nameof(relativeParts));
            parts.Add(part);
        }

        return Path.Combine(parts.ToArray());
    }

    /// <summary>Descobre os sistemas instalados (apenas pastas validas em Systems/).</summary>
    public IEnumerable<string> EnumerateInstalledSystemIds()
    {
        if (!Directory.Exists(SystemsDir))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(SystemsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(dir);

            // Pastas de trabalho do updater (swap atomico) nao sao sistemas instalados.
            if (id.StartsWith('.') || id.Contains(".old-") || id.Contains(".staging-"))
                continue;

            if (File.Exists(Path.Combine(dir, SystemContract.ManifestFileName)))
                yield return id;
        }
    }

    /// <summary>Le a versao da plataforma a partir do InstallDir (null quando indisponivel).</summary>
    public string? ReadPlatformVersion()
    {
        if (!File.Exists(PlatformVersionFile))
            return null;

        try
        {
            var json = File.ReadAllText(PlatformVersionFile);
            var info = System.Text.Json.JsonSerializer.Deserialize<PlatformVersionInfo>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return info?.Version;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Garante que o sistema tem a pasta de dados (software x dados separados).</summary>
    public string EnsureSystemDataDir(string systemId, SystemManifest? manifest = null)
    {
        var dir = SystemDataDir(systemId, manifest);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string RequireSafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("system-id obrigatorio", nameof(value));

        var trimmed = value.Trim();
        if (trimmed.Contains("..") || trimmed.Contains('/') || trimmed.Contains('\\') || Path.IsPathRooted(trimmed))
            throw new ArgumentException($"system-id invalido: '{value}'", nameof(value));

        return trimmed;
    }
}

/// <summary>version.json da plataforma (InstallDir\version.json).</summary>
public sealed class PlatformVersionInfo
{
    public string Version { get; set; } = string.Empty;
    public string? BuildDate { get; set; }
    public string? BuildConfiguration { get; set; }
}
