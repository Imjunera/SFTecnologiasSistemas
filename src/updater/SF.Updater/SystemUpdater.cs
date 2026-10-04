using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SF.Platform.Contract;

namespace SF.Updater;

/// <summary>
/// Logica compartilhada de instalacao/atualizacao de sistema (System Contract).
/// Usada tanto pelo update de plataforma quanto pelo update individual de sistema (--system).
/// </summary>
public sealed class SystemUpdater
{
    private readonly PlatformLayout _layout;
    private readonly string _platformVersion;
    private readonly Action<string> _log;

    public SystemUpdater(PlatformLayout layout, string platformVersion, Action<string> log)
    {
        _layout = layout;
        _platformVersion = platformVersion;
        _log = log;
    }

    /// <summary>
    /// Instala ou atualiza um unico sistema a partir de uma entrada do manifesto.
    /// Retorna true se sucesso, false se falha (ja loga detalhes).
    /// </summary>
    public async Task<bool> InstallOrUpdateSystemAsync(SystemPackageEntry entry, bool isPlatformUpdate = false)
    {
        var problems = entry.ValidateForDistribution();
        if (problems.Count > 0)
        {
            _log($"ERRO: entrada de sistema invalida no manifesto: {string.Join("; ", problems)}");
            return false;
        }

        var installed = ReadInstalledSystem(entry.Id);
        var installedVersion = installed?.InstalledVersion;

        // Nunca baixar novamente um sistema que ja esta instalado e atualizado.
        if (installed is not null && installed.IsValid && VersionRules.AreEquivalent(installedVersion, entry.Version))
        {
            _log($"Sistema {entry.Id}: {installedVersion} ja instalado e atualizado — nenhum download.");
            RecordInstalledSystem(entry, installedVersion!, reused: true);
            return true;
        }

        if (!entry.IsCompatibleWith(_platformVersion))
        {
            _log($"ERRO: sistema {entry.Id} exige plataforma >= {entry.MinimumPlatformVersion}; alvo {_platformVersion}");
            return false;
        }

        _log(installed is null
            ? $"Sistema {entry.Id}: instalacao inicial da versao {entry.Version}"
            : $"Sistema {entry.Id}: atualizando {installedVersion} -> {entry.Version}");

        // Download ANTES de parar/processar qualquer coisa: falha de rede nao toca em nada.
        var package = await DownloadPackageAsync(entry.PackageUrl, entry.Version, entry.Id);
        if (package is null)
        {
            _log($"ERRO: download do sistema {entry.Id} falhou");
            return false;
        }

        if (!ValidateSha256(package, entry.Sha256!))
        {
            _log($"ERRO: SHA-256 do sistema {entry.Id} invalido — pacote descartado");
            return false;
        }

        var targetDir = _layout.SystemDir(entry.Id);
        var stagingRoot = Path.Combine(_layout.SystemsDir, $".staging-{entry.Id}-{Guid.NewGuid().ToString("N")[..8]}");
        var stagingDir = Path.Combine(stagingRoot, entry.Id);

        // Extrai em area temporaria e valida ANTES de tocar na versao instalada.
        SystemValidationResult validation;
        try
        {
            Directory.CreateDirectory(stagingRoot);
            ZipFile.ExtractToDirectory(package, stagingDir);

            validation = SystemValidator.ValidatePackageDirectory(stagingDir, entry.Id, _platformVersion);
            if (!validation.IsValid)
            {
                _log($"ERRO: pacote do sistema {entry.Id} viola o System Contract: {validation.Describe()}");
                return false;
            }

            if (validation.Warnings.Count > 0)
                _log($"AVISO sistema {entry.Id}: {validation.Describe()}");
        }
        catch (Exception ex)
        {
            _log($"ERRO ao extrair/validar o pacote de {entry.Id}: {ex.GetType().Name}: {ex.Message}");
            TryDeleteDirectory(stagingRoot);
            return false;
        }

        // A partir daqui os arquivos instalados podem ser substituidos: tudo e reversivel.
        StopSystemProcesses(validation.Manifest, targetDir);

        var systemBackup = BackupSystem(entry.Id, installedVersion);
        if (installed is not null && installed.IsValid && systemBackup is null)
        {
            _log($"ERRO: falha ao criar backup de {entry.Id} — abortando antes de substituir arquivos");
            TryDeleteDirectory(stagingRoot);
            return false;
        }

        // Migration: a nova versao pode migrar o banco. Backup do banco e preservado
        // ate a pos-validacao; rollback o restaura se a migration falhar.
        var dataBackup = BackupSystemData(entry.Id);

        try
        {
            // Revalida o staging imediatamente antes da troca atomica.
            var stageValidation = SystemValidator.ValidatePackageDirectory(stagingDir, entry.Id, _platformVersion);
            if (!stageValidation.IsValid)
                throw new InvalidOperationException($"revalidacao do staging: {stageValidation.Describe()}");

            SwapSystemDirectory(entry.Id, stagingDir, targetDir);

            // Pos-validacao: a instalacao precisa continuar valida DEPOIS da troca.
            var after = SystemValidator.ValidateDirectory(targetDir, _platformVersion);
            if (!after.IsValid)
                throw new InvalidOperationException($"pos-validacao: {after.Describe()}");

            var dataDir = _layout.EnsureSystemDataDir(entry.Id, after.Manifest);
            RecordInstalledSystem(entry, entry.Version, reused: false);
            PruneSystemBackups(entry.Id);

            _log($"Sistema {entry.Id} {entry.Version} em vigor. Dados preservados em {dataDir}");
            return true;
        }
        catch (Exception ex)
        {
            _log($"ERRO ao instalar o sistema {entry.Id}: {ex.GetType().Name}: {ex.Message}");
            RollbackSystem(entry.Id, systemBackup, dataBackup);
            return false;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private SystemValidationResult? ReadInstalledSystem(string systemId)
    {
        var dir = _layout.SystemDir(systemId);
        if (!Directory.Exists(dir))
            return null;

        var result = SystemValidator.ValidateDirectory(dir, _platformVersion);
        if (!result.IsValid)
            _log($"AVISO: instalacao atual de {systemId} nao valida ({result.Describe()}) — sera substituida.");

        return result;
    }

    private void RecordInstalledSystem(SystemPackageEntry entry, string version, bool reused)
    {
        // O estado instalado e mantido no UpdaterState/registry pelo chamador se necessario
        // Aqui apenas logamos
        _log($"Registrado: {entry.Id} v{version} (reused: {reused})");
    }

    private async Task<string?> DownloadPackageAsync(string? packageUrl, string? version, string name)
    {
        if (string.IsNullOrEmpty(packageUrl))
        {
            _log($"AVISO: Pacote {name} nao disponivel no manifesto");
            return null;
        }

        try
        {
            _log($"Baixando pacote {name} de {packageUrl}");

            var fileName = $"{name}-{version}.zip";
            var updatesTempDir = Path.Combine(_layout.DataRoot, "updates", "temp");
            Directory.CreateDirectory(updatesTempDir);
            var filePath = Path.Combine(updatesTempDir, fileName);

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);
            var bytes = await client.GetByteArrayAsync(packageUrl);
            await File.WriteAllBytesAsync(filePath, bytes);

            _log($"Pacote {name} baixado: {filePath} ({bytes.Length} bytes)");
            return filePath;
        }
        catch (Exception ex)
        {
            _log($"ERRO ao baixar pacote {name}: {ex.Message}");
            return null;
        }
    }

    private static bool ValidateSha256(string filePath, string expectedHash)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            return hashString == expectedHash.ToLowerInvariant();
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    private void StopSystemProcesses(SystemManifest? manifest, string systemDir)
    {
        var entryPoint = manifest?.EntryPoint;
        if (string.IsNullOrWhiteSpace(entryPoint))
            return;

        var processName = Path.GetFileNameWithoutExtension(entryPoint);
        if (string.IsNullOrWhiteSpace(processName))
            return;

        try
        {
            foreach (var proc in Process.GetProcessesByName(processName))
            {
                try
                {
                    var exePath = proc.MainModule?.FileName;
                    if (exePath is null ||
                        !exePath.StartsWith(systemDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    _log($"Parando processo do sistema {processName} (PID {proc.Id})...");
                    proc.Kill();
                    proc.WaitForExit(10000);
                }
                catch
                {
                    // Processo de outra instalacao ou ja finalizado: ignora.
                }
            }
        }
        catch (Exception ex)
        {
            _log($"AVISO: nao foi possivel verificar processos de {processName}: {ex.Message}");
        }
    }

    private string? BackupSystem(string systemId, string? version)
    {
        var sourceDir = _layout.SystemDir(systemId);
        if (!Directory.Exists(sourceDir))
            return null;

        try
        {
            var root = Path.Combine(_layout.DataRoot, "backups", "systems", systemId);
            Directory.CreateDirectory(root);

            var backupDir = Path.Combine(root, $"backup-{version ?? "unknown"}-{DateTime.Now:yyyyMMdd-HHmmss}");
            if (Directory.Exists(backupDir))
                TryDeleteDirectory(backupDir);

            CopyDirectory(sourceDir, backupDir);
            _log($"Backup do sistema {systemId}: {backupDir}");
            return backupDir;
        }
        catch (Exception ex)
        {
            _log($"ERRO: falha ao criar backup de {systemId}: {ex.Message}");
            return null;
        }
    }

    private string? BackupSystemData(string systemId)
    {
        try
        {
            var dataDir = _layout.SystemDataDir(systemId);
            var dbFile = Path.Combine(dataDir, "database.sqlite");
            if (!File.Exists(dbFile))
                return null;

            var backupDir = Path.Combine(dataDir, "backups", $"migration-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(backupDir);

            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var file = dbFile + suffix;
                if (File.Exists(file))
                    File.Copy(file, Path.Combine(backupDir, Path.GetFileName(file)), true);
            }

            _log($"Backup de banco de {systemId}: {backupDir}");
            return backupDir;
        }
        catch (Exception ex)
        {
            _log($"AVISO: falha ao backupar banco de {systemId}: {ex.Message}");
            return null;
        }
    }

    private void RollbackSystem(string systemId, string? systemBackup, string? dataBackup)
    {
        try
        {
            if (!string.IsNullOrEmpty(systemBackup) && Directory.Exists(systemBackup))
            {
                var targetDir = _layout.SystemDir(systemId);
                var failedDir = Path.Combine(_layout.SystemsDir, $".failed-{systemId}-{Guid.NewGuid().ToString("N")[..8]}");

                if (Directory.Exists(targetDir))
                    Directory.Move(targetDir, failedDir);

                try
                {
                    MoveDirectory(systemBackup, targetDir);
                }
                catch
                {
                    if (Directory.Exists(failedDir))
                    {
                        TryDeleteDirectory(targetDir);
                        if (!Directory.Exists(targetDir))
                            MoveDirectory(failedDir, targetDir);
                    }
                    throw;
                }

                TryDeleteDirectory(failedDir);
                _log($"Rollback do sistema {systemId}: versao anterior restaurada");
            }
            else
            {
                _log($"Rollback do sistema {systemId}: sem backup de software (instalacao inicial abortada)");
            }

            if (!string.IsNullOrEmpty(dataBackup) && Directory.Exists(dataBackup))
            {
                var dataDir = _layout.SystemDataDir(systemId);
                foreach (var file in Directory.GetFiles(dataBackup))
                {
                    var dest = Path.Combine(dataDir, Path.GetFileName(file));
                    File.Copy(file, dest, true);
                }
                _log($"Rollback de banco de {systemId}: restaurado de {dataBackup}");
            }
        }
        catch (Exception ex)
        {
            _log($"ERRO no rollback de {systemId}: {ex.Message}");
        }
    }

    private void PruneSystemBackups(string systemId)
    {
        try
        {
            var root = Path.Combine(_layout.DataRoot, "backups", "systems", systemId);
            if (!Directory.Exists(root))
                return;

            var dirs = Directory.GetDirectories(root)
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .Skip(3) // SystemBackupRetention = 3
                .ToList();

            foreach (var dir in dirs)
            {
                TryDeleteDirectory(dir);
                _log($"Backup antigo removido ({systemId}): {dir}");
            }
        }
        catch (Exception ex)
        {
            _log($"AVISO: falha ao aplicar retencao de backup de {systemId}: {ex.Message}");
        }
    }

    private static void SwapSystemDirectory(string systemId, string stagingDir, string targetDir)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);

        string? previousDir = null;
        if (Directory.Exists(targetDir))
        {
            previousDir = Path.Combine(Path.GetDirectoryName(targetDir)!, $"{systemId}.old-{DateTime.Now:yyyyMMddHHmmss}");
            TryDeleteDirectory(previousDir);
            Directory.Move(targetDir, previousDir);
        }

        try
        {
            Directory.Move(stagingDir, targetDir);
        }
        catch
        {
            if (previousDir is not null && !Directory.Exists(targetDir) && Directory.Exists(previousDir))
                Directory.Move(previousDir, targetDir);
            throw;
        }

        if (previousDir is not null)
            TryDeleteDirectory(previousDir);
    }

    private static void MoveDirectory(string sourceDir, string destDir)
    {
        var sameVolume = string.Equals(
            Path.GetPathRoot(Path.GetFullPath(sourceDir)),
            Path.GetPathRoot(Path.GetFullPath(destDir)),
            StringComparison.OrdinalIgnoreCase);

        if (sameVolume)
        {
            Directory.Move(sourceDir, destDir);
            return;
        }

        CopyDirectory(sourceDir, destDir);
        TryDeleteDirectory(sourceDir);
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destDir, Path.GetRelativePath(sourceDir, dir)));
        }
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(destDir, Path.GetRelativePath(sourceDir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            // log warning if needed
        }
    }
}