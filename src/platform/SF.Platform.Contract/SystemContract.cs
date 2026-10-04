namespace SF.Platform.Contract;

/// <summary>
/// Nomes canonicos do contrato de plataforma/sistemas.
/// Alterar qualquer constante aqui e uma mudanca de contrato (ver SYSTEM-CONTRACT.md).
/// </summary>
public static class SystemContract
{
    /// <summary>Arquivo de identidade/contrato do sistema.</summary>
    public const string ManifestFileName = "system.json";

    /// <summary>Arquivo com a versao instalada (lida sem executar o sistema).</summary>
    public const string VersionFileName = "version.json";

    /// <summary>Pasta que agrupa os sistemas instalados dentro do InstallDir.</summary>
    public const string SystemsFolderName = "Systems";

    /// <summary>Pasta raiz dos dados (dentro do DataRoot, ex.: %ProgramData%\SF Tecnologias).</summary>
    public const string DataFolderName = "data";

    /// <summary>Subpasta onde o entry point do sistema deve ficar.</summary>
    public const string EntryPointFolder = "app/executable";

    /// <summary>Estrutura minima obrigatoria (validada antes de executar o sistema).</summary>
    public static readonly string[] RequiredDirectories =
    {
        "migrations"
    };

    /// <summary>
    /// Estrutura obrigatoria quando o sistema declara entryPoint proprio.
    /// Se entryPoint omitido, a plataforma usa API compartilhada e estas pastas nao sao necessarias.
    /// </summary>
    public static readonly string[] RequiredDirectoriesWithEntryPoint =
    {
        "app",
        EntryPointFolder,
        "migrations"
    };

    /// <summary>Limite de tamanho para os arquivos de metadados do contrato.</summary>
    public const int MaxMetadataFileBytes = 64 * 1024;
}
