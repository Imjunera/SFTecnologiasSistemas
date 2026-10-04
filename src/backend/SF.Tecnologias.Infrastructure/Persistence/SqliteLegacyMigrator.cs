using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SF.Tecnologias.Infrastructure.Persistence
{
    /// <summary>
    /// Fase 1 (Platform/System/Data): os dados de cada sistema ficam em
    /// %ProgramData%\SF Tecnologias\Data\<systemId>\database.sqlite (contrato system.json).
    /// Instalacoes legadas usavam %ProgramData%\SF Tecnologias\data\SFTecnologias.db.
    ///
    /// Este migrador copia (nunca move) o banco legado para o destino configurado na
    /// PRIMEIRA execucao com o novo layout. O arquivo legado permanece intacto como
    /// fallback. Roda antes de qualquer acesso ao DbContext, valendo tanto para o
    /// servico Windows quanto para a API spawnada pelo desktop.
    /// </summary>
    public static class SqliteLegacyMigrator
    {
        private static readonly Regex DataSourceRegex =
            new(@"Data Source\s*=\s*([^;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void MigrateLegacyDatabaseIfApplicable(string? connectionString)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(connectionString))
                    return;

                var match = DataSourceRegex.Match(connectionString);
                if (!match.Success)
                    return;

                var target = Environment.ExpandEnvironmentVariables(match.Groups[1].Value.Trim());

                // Apenas caminhos absolutos (dev usa caminho relativo: nada a migrar)
                if (!Path.IsPathRooted(target))
                    return;

                // Ja existe: nada a fazer (nunca sobrescrever dados existentes)
                if (File.Exists(target))
                    return;

                var legacyDb = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "SF Tecnologias", "data", "SFTecnologias.db");

                if (!File.Exists(legacyDb))
                    return;

                // Nao migrar o arquivo sobre ele mesmo
                if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(legacyDb),
                        StringComparison.OrdinalIgnoreCase))
                    return;

                var targetDir = Path.GetDirectoryName(target);
                if (string.IsNullOrEmpty(targetDir))
                    return;

                Directory.CreateDirectory(targetDir);
                File.Copy(legacyDb, target);

                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    if (File.Exists(legacyDb + suffix))
                        File.Copy(legacyDb + suffix, target + suffix);
                }

                Console.WriteLine($"[DATA] Banco legado migrado para o layout por sistema: {target}");
            }
            catch (Exception ex)
            {
                // Nao derruba o startup por causa da migracao
                Console.WriteLine("[DATA] AVISO: falha ao migrar banco legado: " + ex.Message);
            }
        }
    }
}
