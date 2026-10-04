using SF.Platform.Contract;

namespace SF.Platform.Tests;

/// <summary>
/// Layout fisico: Platform (binarios) x Systems (software dos sistemas) x Data (dados).
/// </summary>
public class PlatformLayoutTests : IClassFixture<SystemFixture>
{
    private readonly SystemFixture _fixture;

    public PlatformLayoutTests(SystemFixture fixture) => _fixture = fixture;

    private PlatformLayout CreateLayout() =>
        new(Path.Combine(_fixture.Root, "ProgramFiles", "SF Tecnologias"),
            Path.Combine(_fixture.Root, "ProgramData", "SF Tecnologias"));

    [Fact]
    public void ResolveCaminhosDeSistemaEDados()
    {
        var layout = CreateLayout();

        Assert.Equal(Path.Combine(layout.InstallDir, "Systems", "h2-conveniencia"), layout.SystemDir("h2-conveniencia"));
        Assert.Equal(
            Path.Combine(layout.DataRoot, "data", "h2-conveniencia"),
            layout.SystemDataDir("h2-conveniencia"));
        Assert.EndsWith("system.json", layout.SystemManifestFile("h2-conveniencia"));
        Assert.EndsWith("version.json", layout.SystemVersionFile("h2-conveniencia"));
    }

    [Fact]
    public void DadosNuncaFicamDentroDoInstallDir()
    {
        var layout = CreateLayout();

        var dataDir = layout.SystemDataDir("h2-conveniencia");

        Assert.False(dataDir.StartsWith(layout.InstallDir, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(layout.DataRoot, dataDir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemIdComTravessiaEhRejeitado()
    {
        var layout = CreateLayout();

        Assert.Throws<ArgumentException>(() => layout.SystemDir("../livraria"));
        Assert.Throws<ArgumentException>(() => layout.SystemDir("h2\\..\\livraria"));
        Assert.Throws<ArgumentException>(() => layout.SystemDataDir(".."));
        Assert.Throws<ArgumentException>(() => layout.SystemDir(""));
    }

    [Fact]
    public void DataDirectoryDeclaradoEhRespeitado()
    {
        var layout = CreateLayout();
        var manifest = new SystemManifest { Id = "h2", DataDirectory = "data/h2/db" };

        Assert.Equal(
            Path.Combine(layout.DataRoot, "data", "h2", "db"),
            layout.SystemDataDir("h2", manifest));
    }

    [Fact]
    public void SemDataDirectoryDeclarado_UsaOCanonico()
    {
        var layout = CreateLayout();
        var manifest = new SystemManifest { Id = "h2" };

        Assert.Equal(layout.SystemDataDir("h2"), layout.SystemDataDir("h2", manifest));
    }

    [Fact]
    public void EnsureSystemDataDir_CriaAPastaEDevolveOCaminho()
    {
        var layout = CreateLayout();
        var manifest = new SystemManifest { Id = "h2", DataDirectory = "data/h2" };

        var dir = layout.EnsureSystemDataDir("h2", manifest);

        Assert.True(Directory.Exists(dir));
        Assert.Equal(layout.SystemDataDir("h2", manifest), dir);
    }

    [Fact]
    public void SystemDataPath_ResolveSubpastasEBlocqueiaTravessia()
    {
        var layout = CreateLayout();

        Assert.Equal(
            Path.Combine(layout.DataRoot, "data", "h2", "logs"),
            layout.SystemDataPath("h2", "logs"));

        Assert.Throws<ArgumentException>(() => layout.SystemDataPath("h2", "..", "livraria"));
    }

    [Fact]
    public void EnumerateInstalledSystemIds_ListaApenasSistemasInstalados()
    {
        var layout = CreateLayout();
        _fixture.CreateValidSystem("sistema-a");
        _fixture.CreateValidSystem("sistema-b");

        Directory.CreateDirectory(layout.SystemsDir);
        CopySystemInto(layout, "sistema-a");
        CopySystemInto(layout, "sistema-b");
        // Pasta de trabalho do updater e pasta sem system.json nao contam como instaladas.
        Directory.CreateDirectory(Path.Combine(layout.SystemsDir, "sistema-a.old-20261002"));
        Directory.CreateDirectory(Path.Combine(layout.SystemsDir, "pasta-solta"));

        var ids = layout.EnumerateInstalledSystemIds().ToArray();

        Assert.Contains("sistema-a", ids);
        Assert.Contains("sistema-b", ids);
        Assert.DoesNotContain("sistema-a.old-20261002", ids);
        Assert.DoesNotContain("pasta-solta", ids);
    }

    [Fact]
    public void ReadPlatformVersion_LeVersionJsonDoInstallDir()
    {
        var layout = CreateLayout();
        Directory.CreateDirectory(layout.InstallDir);
        File.WriteAllText(layout.PlatformVersionFile, """{ "version": "1.4.2", "buildDate": "2026-10-02" }""");

        Assert.Equal("1.4.2", layout.ReadPlatformVersion());
    }

    [Fact]
    public void ReadPlatformVersion_SemArquivo_RetornaNull()
    {
        Assert.Null(new PlatformLayout(
            Path.Combine(_fixture.Root, "sem-install"),
            Path.Combine(_fixture.Root, "sem-data")).ReadPlatformVersion());
    }

    private void CopySystemInto(PlatformLayout layout, string id)
    {
        // Recria a estrutura minima dentro do SystemsDir real (copiar de um unico
        // fixture dir e simples e suficiente para exercitar a descoberta).
        var target = layout.SystemDir(id);
        foreach (var dir in new[] { "app/executable", "migrations" })
            Directory.CreateDirectory(Path.Combine(target, dir.Replace('/', Path.DirectorySeparatorChar)));

        File.WriteAllText(Path.Combine(target, "app", "executable", "Sistema.exe"), "stub");
        File.WriteAllText(Path.Combine(target, "system.json"), $$"""
            { "id": "{{id}}", "name": "{{id}}", "systemVersion": "1.0.0",
              "entryPoint": "app/executable/Sistema.exe", "dataDirectory": "data/{{id}}" }
            """);
        File.WriteAllText(Path.Combine(target, "version.json"), $$"""
            { "systemId": "{{id}}", "version": "1.0.0" }
            """);
    }
}
