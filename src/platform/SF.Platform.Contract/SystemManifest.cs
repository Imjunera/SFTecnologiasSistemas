using System.Text.Json.Serialization;

namespace SF.Platform.Contract;

/// <summary>
/// Representacao de <c>system.json</c>: identidade e contrato basico do sistema.
/// Campos definitivos — nao adicionar propriedades sem necessidade real.
/// </summary>
public sealed class SystemManifest
{
    /// <summary>Identificador estavel do sistema (kebab-case). Igual ao nome da pasta.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Nome de exibicao.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Publicador.</summary>
    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    /// <summary>Versao do sistema declarada no pacote.</summary>
    [JsonPropertyName("systemVersion")]
    public string SystemVersion { get; set; } = string.Empty;

    /// <summary>
    /// Versao MINIMA da plataforma compativel com este sistema.
    /// A plataforma recusa instalar/executar sistemas que exijam mais do que ela oferece.
    /// </summary>
    [JsonPropertyName("platformVersion")]
    public string? PlatformVersion { get; set; }

    /// <summary>
    /// Entry point explicito do sistema (opcional). Se omitido, a plataforma usa a API compartilhada.
    /// Se informado, deve ser relativo a raiz do sistema e dentro de app/executable/.
    /// </summary>
    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; set; }

    /// <summary>
    /// UI do sistema (opcional) carregada pelo shell da plataforma, relativo a raiz do
    /// sistema (ex.: <c>app/resources/renderer/index.html</c>). Sistemas sem UI propria
    /// podem omitir.
    /// </summary>
    [JsonPropertyName("renderer")]
    public string? Renderer { get; set; }

    /// <summary>
    /// Diretorio de dados, relativo a raiz de dados da plataforma.
    /// Canonico: <c>data/&lt;id&gt;</c> (subpastas sao permitidas). Isolamento entre sistemas e obrigatorio.
    /// </summary>
    [JsonPropertyName("dataDirectory")]
    public string? DataDirectory { get; set; }

    /// <summary>Descricao curta (opcional).</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
