using System.Text.Json;

namespace SF.Platform.Tests;

/// <summary>
/// Cria um sistema de teste completo em disco (estrutura do contrato) para validar
/// o comportamento do validador sem depender de nenhum sistema real.
/// </summary>
public sealed class SystemFixture : IDisposable
{
    private readonly string _root;

    public SystemFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "sf-platform-tests", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    /// <summary>
    /// Cria <c>Systems\&lt;unico&gt;\&lt;system-id&gt;</c> — o nome da pasta continua sendo o id
    /// (o validador exige id == pasta), mas cada chamada tem pai proprio para os testes
    /// nao compartilharem estado entre si.
    /// </summary>
    public string CreateSystemDirectory(string systemId) =>
        CreateDirectory(Path.Combine(_root, "Systems", Guid.NewGuid().ToString("N")[..8], systemId));

    public string CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    public void WriteDefaultStructure(string systemDirectory)
    {
        foreach (var dir in new[]
                 {
                     "app", "app/executable", "app/libraries", "app/runtime", "app/resources",
                     "modules", "modules/core", "modules/features", "modules/integrations",
                     "assets", "configuration", "configuration/defaults", "configuration/schemas",
                     "migrations"
                 })
        {
            CreateDirectory(Path.Combine(systemDirectory, dir.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    public void WriteEntryPointFile(string systemDirectory, string entryPoint)
    {
        var full = Path.Combine(systemDirectory, entryPoint.Replace('/', Path.DirectorySeparatorChar));
        CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "stub");
    }

    public void WriteManifest(string systemDirectory, string json) =>
        File.WriteAllText(Path.Combine(systemDirectory, "system.json"), json);

    public void WriteVersion(string systemDirectory, string json) =>
        File.WriteAllText(Path.Combine(systemDirectory, "version.json"), json);

    public void WriteValidManifest(string systemDirectory, string id, string? platformVersion = "1.0.0", string entryPoint = "app/executable/Sistema.exe")
    {
        var manifest = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = "Sistema " + id,
            ["publisher"] = "SF Tecnologias",
            ["systemVersion"] = "1.0.0",
            ["entryPoint"] = entryPoint,
            ["dataDirectory"] = "data/" + id
        };

        if (platformVersion is not null)
            manifest["platformVersion"] = platformVersion;

        WriteManifest(systemDirectory, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void WriteValidVersion(string systemDirectory, string id, string version = "1.0.0") =>
        WriteVersion(systemDirectory, JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["systemId"] = id,
            ["version"] = version,
            ["build"] = "2026.10.02"
        }));

    /// <summary>Sistema completo e valido (estrutura + manifest + version + entry point).</summary>
    public string CreateValidSystem(string id = "sistema-teste", string? platformVersion = "1.0.0", string version = "1.0.0")
    {
        var dir = CreateSystemDirectory(id);
        WriteDefaultStructure(dir);
        WriteValidManifest(dir, id, platformVersion);
        WriteValidVersion(dir, id, version);
        WriteEntryPointFile(dir, "app/executable/Sistema.exe");
        return dir;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
            // best effort
        }
    }
}
