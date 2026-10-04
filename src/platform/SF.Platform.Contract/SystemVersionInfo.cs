using System.Text.Json.Serialization;

namespace SF.Platform.Contract;

/// <summary>
/// Representacao de <c>version.json</c>: permite que a plataforma descubra a versao
/// instalada do sistema sem executar o sistema.
/// </summary>
public sealed class SystemVersionInfo
{
    [JsonPropertyName("systemId")]
    public string SystemId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Identificador de build (ex.: data ou numero sequencial).</summary>
    [JsonPropertyName("build")]
    public string? Build { get; set; }
}
