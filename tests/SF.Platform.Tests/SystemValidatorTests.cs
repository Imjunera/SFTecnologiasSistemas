using SF.Platform.Contract;

namespace SF.Platform.Tests;

/// <summary>
/// Validador de sistema: os 10 itens obrigatorios do contrato.
/// Sistema invalido nunca deve ser executado/instalado.
/// </summary>
public class SystemValidatorTests : IClassFixture<SystemFixture>
{
    private readonly SystemFixture _fixture;

    public SystemValidatorTests(SystemFixture fixture) => _fixture = fixture;

    private static bool HasCode(SystemValidationResult result, string code) =>
        result.Errors.Any(e => e.Code == code);

    [Fact]
    public void SistemaCompleto_EhValido()
    {
        var dir = _fixture.CreateValidSystem("h2-conveniencia");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.True(result.IsValid, result.Describe());
        Assert.Equal("h2-conveniencia", result.Manifest!.Id);
        Assert.Equal("1.0.0", result.InstalledVersion);
        Assert.EndsWith("Sistema.exe", result.EntryPointPath);
        Assert.Equal("data/h2-conveniencia", result.DataDirectory);
    }

    [Fact]
    public void PastaInexistente_EhInvalido()
    {
        var result = SystemValidator.ValidateDirectory(
            Path.Combine(_fixture.Root, "Systems", "nao-existe"), "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.SystemDirMissing));
    }

    [Fact]
    public void SemSystemJson_EhInvalido()
    {
        var dir = _fixture.CreateSystemDirectory("sem-manifest");
        _fixture.WriteDefaultStructure(dir);
        _fixture.WriteValidVersion(dir, "sem-manifest");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.ManifestMissing));
    }

    [Fact]
    public void SystemJsonCorrompido_EhInvalido()
    {
        var dir = _fixture.CreateValidSystem("manifest-quebrado");
        _fixture.WriteManifest(dir, "{ isso nao e json");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.ManifestInvalidJson));
    }

    [Fact]
    public void EstruturaMinimaAusente_EhInvalido()
    {
        var dir = _fixture.CreateSystemDirectory("sem-estrutura");
        _fixture.WriteValidManifest(dir, "sem-estrutura");
        _fixture.WriteValidVersion(dir, "sem-estrutura");
        _fixture.WriteEntryPointFile(dir, "app/executable/Sistema.exe");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.RequiredDirectoryMissing));
    }

    [Fact]
    public void VersionJsonAusente_EhInvalido()
    {
        var dir = _fixture.CreateValidSystem("sem-version");
        File.Delete(Path.Combine(dir, "version.json"));

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.VersionFileMissing));
    }

    [Fact]
    public void SystemIdDivergenteDaPasta_EhInvalido()
    {
        var dir = _fixture.CreateValidSystem("h2-conveniencia");
        _fixture.WriteValidManifest(dir, "outro-id");
        _fixture.WriteValidVersion(dir, "outro-id");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.IdMismatch));
    }

    [Theory]
    [InlineData("H2 Conveniencia")]
    [InlineData("H2-Conveniencia")]
    [InlineData("h2_conveniencia")]
    [InlineData("-h2")]
    [InlineData("h2--conveniencia")]
    public void SystemIdInvalido_EhInvalido(string id)
    {
        var dir = _fixture.CreateSystemDirectory(id);
        _fixture.WriteDefaultStructure(dir);
        _fixture.WriteValidManifest(dir, id);
        _fixture.WriteValidVersion(dir, id);
        _fixture.WriteEntryPointFile(dir, "app/executable/Sistema.exe");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.IdInvalid));
    }

    [Fact]
    public void VersionJsonComIdDiferente_EhInvalido()
    {
        var dir = _fixture.CreateValidSystem("h2-conveniencia");
        _fixture.WriteValidVersion(dir, "livraria");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.VersionFileIdMismatch));
    }

    [Fact]
    public void VersaoInvalida_EhInvalido()
    {
        var dir = _fixture.CreateValidSystem("versao-ruim", version: "abc");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.VersionFileVersionInvalid));
    }

    [Fact]
    public void EntryPointOmitido_EhValido_UsaApiCompartilhada()
    {
        var dir = _fixture.CreateSystemDirectory("sem-entry");
        _fixture.WriteDefaultStructure(dir);
        // Manifest SEM entryPoint (propriedade omitida)
        _fixture.WriteManifest(dir, """
            {
              "id": "sem-entry",
              "name": "Sistema sem-entry",
              "publisher": "SF Tecnologias",
              "systemVersion": "1.0.0",
              "dataDirectory": "data/sem-entry",
              "platformVersion": "1.0.0"
            }
            """);
        _fixture.WriteValidVersion(dir, "sem-entry");
        // NAO escreve entry point file - sistema usa API compartilhada

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.True(result.IsValid, result.Describe());
        Assert.Null(result.Manifest!.EntryPoint);
        Assert.Null(result.EntryPointPath);
    }

    [Theory]
    [InlineData("C:/Windows/System32/cmd.exe")]
    [InlineData("../../../Windows/System32/cmd.exe")]
    [InlineData("app/executable/../../../Windows/System32/cmd.exe")]
    [InlineData("bin/Sistema.exe")]
    public void EntryPointForaDoContrato_EhInvalido(string entryPoint)
    {
        var dir = _fixture.CreateSystemDirectory("entry-fora");
        _fixture.WriteDefaultStructure(dir);
        _fixture.WriteValidManifest(dir, "entry-fora", entryPoint: entryPoint);
        _fixture.WriteValidVersion(dir, "entry-fora");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.EntryPointInvalid));
    }

    [Fact]
    public void EntryPointDeclaradoMasAusente_EhInvalido()
    {
        var dir = _fixture.CreateValidSystem("entry-ausente");
        File.Delete(Path.Combine(dir, "app", "executable", "Sistema.exe"));

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.EntryPointNotFound));
    }

    [Fact]
    public void PlataformaIncompativel_EhInvalido()
    {
        var dir = _fixture.CreateValidSystem("futuro", platformVersion: "2.0.0");

        var result = SystemValidator.ValidateDirectory(dir, "1.9.9");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.PlatformIncompatible));
    }

    [Fact]
    public void PlataformaIgualAoMinimoExigido_EhValido()
    {
        var dir = _fixture.CreateValidSystem("exato", platformVersion: "1.0.0");

        Assert.True(SystemValidator.ValidateDirectory(dir, "1.0.0").IsValid);
        Assert.True(SystemValidator.ValidateDirectory(dir, "1.0.0.0").IsValid);
    }

    [Fact]
    public void SemRequisitoDePlataforma_EhCompativel()
    {
        var dir = _fixture.CreateValidSystem("sem-requisito", platformVersion: null);

        Assert.True(SystemValidator.ValidateDirectory(dir, "0.1.0").IsValid);
    }

    [Fact]
    public void DataDirectoryDeOutroSistema_ViolaIsolamento()
    {
        var dir = _fixture.CreateSystemDirectory("h2-conveniencia");
        _fixture.WriteDefaultStructure(dir);
        _fixture.WriteManifest(dir, """
            {
              "id": "h2-conveniencia",
              "name": "H2",
              "systemVersion": "1.0.0",
              "entryPoint": "app/executable/Sistema.exe",
              "dataDirectory": "data/livraria"
            }
            """);
        _fixture.WriteValidVersion(dir, "h2-conveniencia");
        _fixture.WriteEntryPointFile(dir, "app/executable/Sistema.exe");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.DataDirectoryNotIsolated));
    }

    [Theory]
    [InlineData("../data/h2")]
    [InlineData("C:/dados/h2")]
    [InlineData("outros/h2")]
    public void DataDirectoryInvalido_EhInvalido(string dataDirectory)
    {
        var dir = _fixture.CreateSystemDirectory("h2");
        _fixture.WriteDefaultStructure(dir);
        _fixture.WriteManifest(dir, $$"""
            {
              "id": "h2",
              "name": "H2",
              "systemVersion": "1.0.0",
              "entryPoint": "app/executable/Sistema.exe",
              "dataDirectory": "{{dataDirectory}}"
            }
            """);
        _fixture.WriteValidVersion(dir, "h2");
        _fixture.WriteEntryPointFile(dir, "app/executable/Sistema.exe");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(
            HasCode(result, SystemValidationCodes.DataDirectoryInvalid) ||
            HasCode(result, SystemValidationCodes.DataDirectoryNotIsolated));
    }

    [Fact]
    public void DataDirectoryCanonicoComSubpasta_EhValido()
    {
        var dir = _fixture.CreateSystemDirectory("h2");
        _fixture.WriteDefaultStructure(dir);
        _fixture.WriteManifest(dir, """
            {
              "id": "h2",
              "name": "H2",
              "systemVersion": "1.0.0",
              "entryPoint": "app/executable/Sistema.exe",
              "dataDirectory": "data/h2/db"
            }
            """);
        _fixture.WriteValidVersion(dir, "h2");
        _fixture.WriteEntryPointFile(dir, "app/executable/Sistema.exe");

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.True(result.IsValid, result.Describe());
        Assert.Equal("data/h2/db", result.DataDirectory);
    }

    [Fact]
    public void MetadadosGigantes_SaoRejeitados()
    {
        var dir = _fixture.CreateValidSystem("gigante");
        _fixture.WriteManifest(dir, new string('x', SystemContract.MaxMetadataFileBytes + 1));

        var result = SystemValidator.ValidateDirectory(dir, "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.ManifestTooLarge));
    }

    [Fact]
    public void PacoteComIdDiferenteDoEsperado_EhInvalido()
    {
        var dir = _fixture.CreateValidSystem("h2-conveniencia");

        var result = SystemValidator.ValidatePackageDirectory(dir, "livraria", "1.0.0");

        Assert.False(result.IsValid);
        Assert.True(HasCode(result, SystemValidationCodes.IdMismatch));
    }

    [Fact]
    public void PacoteValidoComIdEsperado_Passa()
    {
        var dir = _fixture.CreateValidSystem("h2-conveniencia");

        var result = SystemValidator.ValidatePackageDirectory(dir, "h2-conveniencia", "1.0.0");

        Assert.True(result.IsValid, result.Describe());
    }
}
