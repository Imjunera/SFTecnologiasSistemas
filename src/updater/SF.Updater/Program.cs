using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SF.Platform.Contract;

namespace SF.Updater;

/// <summary>
/// SF.Updater - Atualizador independente do SF Tecnologias
/// Executa como processo separado, sem depender do Desktop estar funcionando.
/// </summary>
class Program
{
    private static readonly string AppName = "SF Tecnologias";
    private static readonly string ServiceName = "SFTecnologiasApi";
    private static string InstallDir = string.Empty;
    private static string DataDir = string.Empty;
    private static string ConfigDir = string.Empty;
    private static string BackupsDir = string.Empty;
    private static string LogsDir = string.Empty;
    private static string UpdatesTempDir = string.Empty;
    private static string LogFile = string.Empty;
    /// <summary>Alvo de sistema (--system &lt;id&gt;): instala/atualiza UM sistema, sem tocar na plataforma.</summary>
    private static string TargetSystemId = string.Empty;
    /// <summary>Retencao de backup por sistema (sem mecanismo de retencao existente no projeto).</summary>
    private const int SystemBackupRetention = 3;
    private static UpdateState CurrentState = new();
    private static PlatformLayout Layout = null!;
    private static readonly List<InstalledSystemState> InstalledSystems = new();
    private static SystemUpdater? _systemUpdater;

    static async Task<int> Main(string[] args)
    {
        ParseArguments(args);
        InitializePaths();
        _systemUpdater = new SystemUpdater(Layout, GetCurrentVersion(), Log);
        EnsureDirectories();
        LoadPreviousSystemState();

        LogFile = Path.Combine(LogsDir, $"updater-{DateTime.Now:yyyy-MM-dd}.log");
        Log("========================================");
        Log("SF.Updater iniciado");
        Log($"Arguments: manifest={CurrentState.ManifestUrl}, install-dir={InstallDir}, system={TargetSystemId}");

        // Fase 3/4 — alvo de sistema: instala ou atualiza UM sistema, sem tocar
        // em Desktop/API/Updater e sem reiniciar a plataforma.
        if (!string.IsNullOrEmpty(TargetSystemId))
        {
            return await RunSingleSystemUpdate();
        }

        try
        {
            // Check for concurrent updater
            if (IsUpdaterRunning())
            {
                Log("AVISO: Outra instancia do updater esta em execucao. Saindo...");
                return 1;
            }

            // Load current version
            var currentVersion = GetCurrentVersion();
            Log($"Versao atual: {currentVersion}");

            // Download and parse manifest
            Log("Baixando manifesto...");
            var manifest = await DownloadManifest(CurrentState.ManifestUrl);
            if (manifest == null)
            {
                Log("ERRO: Falha ao baixar ou parsear manifesto");
                SaveState(UpdateStatus.Failed, "Manifest download failed");
                return 1;
            }

            Log($"Manifesto: product={manifest.Product}, version={manifest.Version}");
            Log($"Desktop: {manifest.Components.Desktop?.Version}, API: {manifest.Components.Api?.Version}");

            // Validate version compatibility
            if (!ValidateVersionCompatibility(currentVersion, manifest))
            {
                Log("ERRO: Versao incompativel");
                SaveState(UpdateStatus.Failed, "Version incompatibility");
                return 1;
            }

            // Check disk space (manifesto declara sizeBytes dos componentes)
            var requiredBytes = (manifest.Components.Desktop?.SizeBytes ?? 0)
                + (manifest.Components.Api?.SizeBytes ?? 0)
                + (manifest.Components.Updater?.SizeBytes ?? 0);
            if (!CheckDiskSpace(requiredBytes))
            {
                Log("ERRO: Espaco em disco insuficiente");
                SaveState(UpdateStatus.Failed, "Insufficient disk space");
                return 1;
            }

            // Check permissions
            if (!CheckPermissions())
            {
                Log("ERRO: Permissoes insuficientes");
                SaveState(UpdateStatus.Failed, "Insufficient permissions");
                return 1;
            }

            // Download packages
            SaveState(UpdateStatus.Downloading, "Downloading packages");
            var desktopPackage = await DownloadPackage(manifest.Components.Desktop?.PackageUrl, manifest.Components.Desktop?.Version, "desktop");
            var apiPackage = await DownloadPackage(manifest.Components.Api?.PackageUrl, manifest.Components.Api?.Version, "api");
            var updaterPackage = await DownloadPackage(manifest.Components.Updater?.PackageUrl, manifest.Components.Updater?.Version, "updater");

            if (desktopPackage == null && apiPackage == null && updaterPackage == null)
            {
                Log("ERRO: Nenhum pacote foi baixado com sucesso");
                SaveState(UpdateStatus.Failed, "Download failed");
                return 1;
            }

            SaveState(UpdateStatus.Downloaded, "Packages downloaded");

            // Validate SHA-256
            SaveState(UpdateStatus.Validating, "Validating packages");
            if (desktopPackage != null && manifest.Components.Desktop?.Sha256 != null)
            {
                if (!ValidateSha256(desktopPackage, manifest.Components.Desktop.Sha256))
                {
                    Log("ERRO: SHA-256 do pacote Desktop incorreto");
                    SaveState(UpdateStatus.Failed, "Desktop package hash mismatch");
                    return 1;
                }
            }

            if (apiPackage != null && manifest.Components.Api?.Sha256 != null)
            {
                if (!ValidateSha256(apiPackage, manifest.Components.Api.Sha256))
                {
                    Log("ERRO: SHA-256 do pacote API incorreto");
                    SaveState(UpdateStatus.Failed, "API package hash mismatch");
                    return 1;
                }
            }

            if (updaterPackage != null && manifest.Components.Updater?.Sha256 != null)
            {
                if (!ValidateSha256(updaterPackage, manifest.Components.Updater.Sha256))
                {
                    Log("ERRO: SHA-256 do pacote Updater incorreto");
                    SaveState(UpdateStatus.Failed, "Updater package hash mismatch");
                    return 1;
                }
            }

            Log("SHA-256 validado com sucesso");
            SaveState(UpdateStatus.Validated, "Packages validated");

            // Stop services
            SaveState(UpdateStatus.StoppingServices, "Stopping services");
            await StopServices();
            SaveState(UpdateStatus.ServicesStopped, "Services stopped");

            // Create backup
            SaveState(UpdateStatus.BackingUp, "Creating backup");
            var backupPath = CreateBackup(currentVersion);
            if (backupPath == null)
            {
                Log("ERRO: Falha ao criar backup — abortando antes de substituir arquivos");
                SaveState(UpdateStatus.Failed, "Backup failed");
                await AttemptRecovery();
                return 1;
            }
            SaveState(UpdateStatus.BackupCompleted, $"Backup at {backupPath}");
            LastBackupPath = backupPath;

            // Update Desktop
            if (desktopPackage != null)
            {
                SaveState(UpdateStatus.UpdatingDesktop, "Updating desktop");
                UpdateDesktop(desktopPackage);
                SaveState(UpdateStatus.DesktopUpdated, "Desktop updated");
                Log("Desktop atualizado");
            }

            // Update API
            if (apiPackage != null)
            {
                SaveState(UpdateStatus.UpdatingApi, "Updating API");
                UpdateApi(apiPackage);
                SaveState(UpdateStatus.ApiUpdated, "API updated");
                Log("API atualizada");
            }

            // Self-update updater (swap running exe on Windows: rename allowed)
            if (updaterPackage != null)
            {
                SaveState(UpdateStatus.UpdatingUpdater, "Updating updater");
                UpdateUpdater(updaterPackage);
                SaveState(UpdateStatus.UpdaterUpdated, "Updater updated");
                Log("Updater atualizado (nova versao efetiva na proxima execucao)");
            }

            // Update systems (System Contract): software substituido, dados preservados
            var systemsOk = await UpdateSystems(manifest);
            if (!systemsOk)
            {
                Log("ERRO: Sistema obrigatorio nao pode ser instalado/atualizado — atualizacao nao concluida");
                SaveState(UpdateStatus.Failed, "Required system update failed");
                if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                {
                    await RollbackFromBackup(backupPath);
                }
                await AttemptRecovery();
                return 1;
            }

            // Start services
            SaveState(UpdateStatus.StartingServices, "Starting services");
            await StartServices();
            SaveState(UpdateStatus.ServicesStarted, "Services started");

            // Health check — falha ⇒ rollback, NUNCA Completed
            SaveState(UpdateStatus.HealthCheckPassed, "Running health check");
            var healthOk = await HealthCheck();
            if (!healthOk)
            {
                Log("ERRO: Health check falhou — iniciando rollback");
                SaveState(UpdateStatus.Failed, "Health check failed — rolling back");
                await RollbackFromBackup(backupPath);
                await AttemptRecovery();
                return 1;
            }

            // Start Desktop
            StartDesktop();

            // Complete
            SaveState(UpdateStatus.Completed, $"Updated from {currentVersion} to {manifest.Version}");
            Log($"Atualizacao concluida: {currentVersion} -> {manifest.Version}");

            CleanupSystemWorkDirectories();
            CleanupTempFiles();
            return 0;
        }
        catch (Exception ex)
        {
            Log($"ERRO FATAL: {ex.GetType().FullName}: {ex.Message}");
            Log($"Stack trace: {ex.StackTrace}");
            SaveState(UpdateStatus.Failed, $"{ex.GetType().Name}: {ex.Message}");

            // Rollback if backup was taken and files were replaced
            if (!string.IsNullOrEmpty(LastBackupPath) && Directory.Exists(LastBackupPath))
            {
                Log("Tentando rollback a partir do backup...");
                await RollbackFromBackup(LastBackupPath);
                SaveState(UpdateStatus.RollbackRequired, "Rolled back after failure");
            }

            await AttemptRecovery();
            return 1;
        }
    }

    static string? LastBackupPath;

    // ============================================
    // SISTEMAS (System Contract)
    // ============================================

    /// <summary>
    /// Instala/atualiza os sistemas declarados no manifesto.
    /// Regras do contrato:
    /// - sistema instalado e atualizado NAO e baixado de novo;
    /// - o pacote precisa validar contra o System Contract antes de entrar em vigor;
    /// - a pasta de dados (Data\&lt;id&gt;) nunca e tocada;
    /// - falha em sistema obrigatorio reprova a atualizacao inteira (a versao anterior permanece).
    /// </summary>
    static async Task<bool> UpdateSystems(UpdateManifest manifest)
    {
        var entries = manifest.Components.Systems ?? new List<SystemPackageEntry>();
        if (entries.Count == 0)
        {
            Log("Manifesto sem sistemas — nada a fazer.");
            return true;
        }

        SaveState(UpdateStatus.UpdatingSystems, $"Atualizando {entries.Count} sistema(s)");

        foreach (var entry in entries)
        {
            var ok = await UpdateSingleSystem(entry, manifest.Version);
            if (ok)
                continue;

            if (entry.Required)
            {
                Log($"Sistema obrigatorio '{entry.Id}' falhou — abortando atualizacao.");
                return false;
            }

            Log($"AVISO: sistema opcional '{entry.Id}' nao pode ser atualizado (instalacao atual mantida).");
        }

        SaveState(UpdateStatus.SystemsUpdated, $"Systems updated ({InstalledSystems.Count})");
        return true;
    }

    static async Task<bool> UpdateSingleSystem(SystemPackageEntry entry, string platformVersion)
    {
        var problems = entry.ValidateForDistribution();
        if (problems.Count > 0)
        {
            Log($"ERRO: entrada de sistema invalida no manifesto: {string.Join("; ", problems)}");
            return false;
        }

        var installed = ReadInstalledSystem(entry.Id, platformVersion);
        var installedVersion = installed?.InstalledVersion;

        // Nunca baixar novamente um sistema que ja esta instalado e atualizado.
        if (installed is not null && installed.IsValid && VersionRules.AreEquivalent(installedVersion, entry.Version))
        {
            Log($"Sistema {entry.Id}: {installedVersion} ja instalado e atualizado — nenhum download.");
            RecordInstalledSystem(entry, installedVersion!, reused: true);
            return true;
        }

        if (!entry.IsCompatibleWith(platformVersion))
        {
            Log($"ERRO: sistema {entry.Id} exige plataforma >= {entry.MinimumPlatformVersion}; alvo {platformVersion}");
            return false;
        }

        Log(installed is null
            ? $"Sistema {entry.Id}: instalacao inicial da versao {entry.Version}"
            : $"Sistema {entry.Id}: atualizando {installedVersion} -> {entry.Version}");

        var package = await DownloadPackage(entry.PackageUrl, entry.Version, entry.Id);
        if (package is null)
        {
            Log($"ERRO: download do sistema {entry.Id} falhou");
            return false;
        }

        if (!ValidateSha256(package, entry.Sha256!))
        {
            Log($"ERRO: SHA-256 do sistema {entry.Id} invalido — pacote descartado");
            return false;
        }

        // O contrato compara system.json/id com o NOME da pasta e exige data/<pasta>:
        // a extracao precisa usar uma pasta chamada como o sistema.
        var stagingRoot = Path.Combine(Layout.SystemsDir, $".staging-{entry.Id}-{Guid.NewGuid().ToString("N")[..8]}");
        var stagingDir = Path.Combine(stagingRoot, entry.Id);
        try
        {
            Directory.CreateDirectory(stagingRoot);
            ZipFile.ExtractToDirectory(package, stagingDir);

            // Validacao de contrato ANTES de tocar na versao instalada.
            var validation = SystemValidator.ValidatePackageDirectory(stagingDir, entry.Id, platformVersion);
            if (!validation.IsValid)
            {
                Log($"ERRO: pacote do sistema {entry.Id} viola o System Contract: {validation.Describe()}");
                return false;
            }

            if (validation.Warnings.Count > 0)
                Log($"AVISO sistema {entry.Id}: {validation.Describe()}");

            try
            {
                SwapSystemDirectory(entry.Id, stagingDir, Layout.SystemDir(entry.Id));
            }
            catch (Exception ex)
            {
                Log($"ERRO: nao foi possivel substituir os arquivos de {entry.Id} (processo aberto/arquivo bloqueado?): {ex.Message}");
                return false;
            }

            // Software e dados sao separados: a pasta de dados e criada se faltar,
            // nunca substituida.
            var dataDir = Layout.EnsureSystemDataDir(entry.Id, validation.Manifest);
            Log($"Sistema {entry.Id} {entry.Version} em vigor. Dados preservados em {dataDir}");

            RecordInstalledSystem(entry, entry.Version, reused: false);
            return true;
        }
        catch (Exception ex)
        {
            Log($"ERRO ao instalar o sistema {entry.Id}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    /// <summary>
    /// Troca a pasta do sistema de forma atomica: a versao anterior sai do lugar,
    /// a nova entra; se a entrada falhar, a anterior volta.
    /// </summary>
    static void SwapSystemDirectory(string systemId, string stagingDir, string targetDir)
    {
        Directory.CreateDirectory(Layout.SystemsDir);

        string? previousDir = null;
        if (Directory.Exists(targetDir))
        {
            previousDir = Path.Combine(Layout.SystemsDir, $"{systemId}.old-{DateTime.Now:yyyyMMddHHmmss}");
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

        Log($"Sistema {systemId}: instalacao trocada atomicamente");
    }

    /// <summary>Le o estado instalado de um sistema pelo contrato (sem executar o sistema).</summary>
    static SystemValidationResult? ReadInstalledSystem(string systemId, string? platformVersion)
    {
        var dir = Layout.SystemDir(systemId);
        if (!Directory.Exists(dir))
            return null;

        // Com a plataforma real: sem isso o sistema instalado seria marcado
        // como invalido por platform_incompatible e forcaria um re-download.
        var result = SystemValidator.ValidateDirectory(dir, platformVersion);
        if (!result.IsValid)
            Log($"AVISO: instalacao atual de {systemId} nao valida ({result.Describe()}) — sera substituida.");

        return result;
    }

    static void RecordInstalledSystem(SystemPackageEntry entry, string version, bool reused)
    {
        InstalledSystems.RemoveAll(s => s.Id == entry.Id);
        InstalledSystems.Add(new InstalledSystemState
        {
            Id = entry.Id,
            Version = version,
            Sha256 = reused ? null : entry.Sha256,
            Reused = reused
        });

        CurrentState.Systems = InstalledSystems;
    }

    /// <summary>Limpa pastas de trabalho (.staging-*) e versoes antigas (.old-*) dos sistemas.</summary>
    static void CleanupSystemWorkDirectories()
    {
        try
        {
            if (!Directory.Exists(Layout.SystemsDir))
                return;

            foreach (var dir in Directory.EnumerateDirectories(Layout.SystemsDir))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith('.') || name.Contains(".old-") || name.Contains(".staging-"))
                    TryDeleteDirectory(dir);
            }
        }
        catch (Exception ex)
        {
            Log($"AVISO: falha ao limpar pastas de trabalho dos sistemas: {ex.Message}");
        }
    }

    static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            Log($"AVISO: nao foi possivel remover {dir}: {ex.Message}");
        }
    }

    // ============================================
    // ALVO DE SISTEMA — INSTALACAO/ATUALIZACAO INDIVIDUAL (Fase 3/4)
    // ============================================

    /// <summary>
    /// Instala ou atualiza SOMENTE o sistema indicado por <c>--system</c>.
    /// Mesmo contrato do update de plataforma (manifesto -> SHA-256 -> staging ->
    /// validacao -> backup -> troca atomica -> pos-validacao), porem:
    /// - Desktop/API/Updater nao sao tocados e a plataforma nao reinicia;
    /// - a pasta de dados nunca e substituida (apenas recebe backup de migration);
    /// - falha em qualquer etapa restaura a versao anterior e o banco.
    /// </summary>
    static async Task<int> RunSingleSystemUpdate()
    {
        Log($"Alvo de sistema: {TargetSystemId}");

        if (IsUpdaterRunning())
        {
            Log("AVISO: Outra instancia do updater esta em execucao. Saindo...");
            SaveState(UpdateStatus.Failed, "Another updater instance is running");
            return 1;
        }

        var platformVersion = Layout.ReadPlatformVersion() ?? GetCurrentVersion();
        Log($"Plataforma: {platformVersion}");

        var manifest = await DownloadManifest(CurrentState.ManifestUrl);
        if (manifest == null)
        {
            Log("ERRO: Falha ao baixar ou parsear manifesto");
            SaveState(UpdateStatus.Failed, "Manifest download failed");
            return 1;
        }

        var entry = (manifest.Components.Systems ?? new List<SystemPackageEntry>())
            .FirstOrDefault(s => string.Equals(s.Id, TargetSystemId, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            Log($"ERRO: sistema '{TargetSystemId}' nao consta no manifesto (manifesto sem sistemas ou outro id)");
            SaveState(UpdateStatus.Failed, $"System '{TargetSystemId}' not found in manifest");
            return 1;
        }

        var problems = entry.ValidateForDistribution();
        if (problems.Count > 0)
        {
            Log($"ERRO: entrada de sistema invalida no manifesto: {string.Join("; ", problems)}");
            SaveState(UpdateStatus.Failed, "Invalid system entry");
            return 1;
        }

        var installed = ReadInstalledSystem(entry.Id, platformVersion);
        var installedVersion = installed?.InstalledVersion;

        // Sem re-download quando a versao local ja e a disponivel.
        if (installed is not null && installed.IsValid && VersionRules.AreEquivalent(installedVersion, entry.Version))
        {
            Log($"Sistema {entry.Id}: {installedVersion} ja instalado e atualizado — nenhum download.");
            RecordInstalledSystem(entry, installedVersion!, reused: true);
            SaveState(UpdateStatus.Completed, $"Sistema {entry.Id} ja esta na versao {installedVersion}");
            return 0;
        }

        if (!entry.IsCompatibleWith(platformVersion))
        {
            Log($"ERRO: sistema {entry.Id} exige plataforma >= {entry.MinimumPlatformVersion}; alvo {platformVersion}");
            SaveState(UpdateStatus.Failed, "Platform incompatible");
            return 1;
        }

        if (!CheckDiskSpace(entry.SizeBytes ?? 0))
        {
            Log("ERRO: Espaco em disco insuficiente");
            SaveState(UpdateStatus.Failed, "Insufficient disk space");
            return 1;
        }

        if (!CheckPermissions())
        {
            Log("ERRO: Permissoes insuficientes");
            SaveState(UpdateStatus.Failed, "Insufficient permissions");
            return 1;
        }

        Log(installed is null
            ? $"Sistema {entry.Id}: instalacao inicial da versao {entry.Version}"
            : $"Sistema {entry.Id}: atualizando {installedVersion} -> {entry.Version}");

        // Download ANTES de parar/processar qualquer coisa: falha de rede nao toca em nada.
        SaveState(UpdateStatus.Downloading, $"Downloading {entry.Id}");
        var package = await DownloadPackage(entry.PackageUrl, entry.Version, entry.Id);
        if (package is null)
        {
            Log($"ERRO: download do sistema {entry.Id} falhou");
            SaveState(UpdateStatus.Failed, "Download failed");
            return 1;
        }

        SaveState(UpdateStatus.Validating, $"Validating {entry.Id}");
        if (!ValidateSha256(package, entry.Sha256!))
        {
            Log($"ERRO: SHA-256 do sistema {entry.Id} invalido — pacote descartado");
            SaveState(UpdateStatus.Failed, "Package hash mismatch");
            return 1;
        }

        var targetDir = Layout.SystemDir(entry.Id);
        // O contrato compara system.json/id com o NOME da pasta e exige data/<pasta>:
        // o staging tem que ter o nome do sistema. A raiz "." leva a limpeza automatica.
        var stagingRoot = Path.Combine(Layout.SystemsDir, $".staging-{entry.Id}-{Guid.NewGuid().ToString("N")[..8]}");
        var stagingDir = Path.Combine(stagingRoot, entry.Id);

        // Extrai em area temporaria e valida ANTES de tocar na versao instalada.
        SystemValidationResult validation;
        try
        {
            Directory.CreateDirectory(stagingRoot);
            ZipFile.ExtractToDirectory(package, stagingDir);

            validation = SystemValidator.ValidatePackageDirectory(stagingDir, entry.Id, platformVersion);
            if (!validation.IsValid)
            {
                Log($"ERRO: pacote do sistema {entry.Id} viola o System Contract: {validation.Describe()}");
                SaveState(UpdateStatus.Failed, "Package violates System Contract");
                TryDeleteDirectory(stagingRoot);
                return 1;
            }

            if (validation.Warnings.Count > 0)
                Log($"AVISO sistema {entry.Id}: {validation.Describe()}");
        }
        catch (Exception ex)
        {
            Log($"ERRO ao extrair/validar o pacote de {entry.Id}: {ex.GetType().Name}: {ex.Message}");
            SaveState(UpdateStatus.Failed, $"Extraction failed: {ex.Message}");
            TryDeleteDirectory(stagingRoot);
            return 1;
        }

        // A partir daqui os arquivos instalados podem ser substituidos: tudo e reversivel.
        SaveState(UpdateStatus.StoppingServices, $"Stopping {entry.Id}");
        StopSystemProcesses(validation.Manifest, targetDir);

        var systemBackup = BackupSystem(entry.Id, installedVersion);
        if (installed is not null && installed.IsValid && systemBackup is null)
        {
            Log($"ERRO: falha ao criar backup de {entry.Id} — abortando antes de substituir arquivos");
            SaveState(UpdateStatus.Failed, "Backup failed");
            TryDeleteDirectory(stagingRoot);
            return 1;
        }

        // Migration: a nova versao pode migrar o banco. Backup do banco e preservado
        // ate a pos-validacao; rollback o restaura se a migration falhar.
        var dataBackup = BackupSystemData(entry.Id);

        SaveState(UpdateStatus.BackupCompleted, $"Backup of {entry.Id} ready");

        try
        {
            // Revalida o staging imediatamente antes da troca atomica.
            var stageValidation = SystemValidator.ValidatePackageDirectory(stagingDir, entry.Id, platformVersion);
            if (!stageValidation.IsValid)
                throw new InvalidOperationException($"revalidacao do staging: {stageValidation.Describe()}");

            SwapSystemDirectory(entry.Id, stagingDir, targetDir);

            // Pos-validacao: a instalacao precisa continuar valida DEPOIS da troca.
            var after = SystemValidator.ValidateDirectory(targetDir, platformVersion);
            if (!after.IsValid)
                throw new InvalidOperationException($"pos-validacao: {after.Describe()}");

            var dataDir = Layout.EnsureSystemDataDir(entry.Id, after.Manifest);
            RecordInstalledSystem(entry, entry.Version, reused: false);
            PruneSystemBackups(entry.Id);

            SaveState(UpdateStatus.SystemsUpdated, $"{entry.Id} {entry.Version}");
            Log($"Sistema {entry.Id} {entry.Version} em vigor. Dados preservados em {dataDir}");

            CleanupSystemWorkDirectories();
            CleanupTempFiles();
            SaveState(UpdateStatus.Completed, $"{entry.Id}: {installedVersion ?? "0.0.0"} -> {entry.Version}");
            Log($"Atualizacao individual concluida: {entry.Id} -> {entry.Version}");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"ERRO ao instalar o sistema {entry.Id}: {ex.GetType().Name}: {ex.Message}");
            RollbackSystem(entry.Id, systemBackup, dataBackup);
            SaveState(UpdateStatus.Failed, $"{entry.Id}: {ex.Message}");
            return 1;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    /// <summary>Para o processo do sistema (entry point) para liberar arquivos antes da troca.</summary>
    static void StopSystemProcesses(SystemManifest? manifest, string systemDir)
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

                    Log($"Parando processo do sistema {processName} (PID {proc.Id})...");
                    proc.Kill();
                    proc.WaitForExit(10000);
                }
                catch
                {
                    // Processo de outro instalacao ou ja finalizado: ignora.
                }
            }
        }
        catch (Exception ex)
        {
            Log($"AVISO: nao foi possivel verificar processos de {processName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Backup da pasta do sistema em %ProgramData%\SF Tecnologias\backups\systems\&lt;id&gt;.
    /// Retorna null quando nao ha versao instalada (instalacao inicial) ou quando o backup falhou.
    /// </summary>
    static string? BackupSystem(string systemId, string? version)
    {
        var sourceDir = Layout.SystemDir(systemId);
        if (!Directory.Exists(sourceDir))
            return null;

        try
        {
            var root = Path.Combine(BackupsDir, "systems", systemId);
            Directory.CreateDirectory(root);

            var backupDir = Path.Combine(root, $"backup-{version ?? "unknown"}-{DateTime.Now:yyyyMMdd-HHmmss}");
            if (Directory.Exists(backupDir))
                TryDeleteDirectory(backupDir);

            CopyDirectory(sourceDir, backupDir);
            Log($"Backup do sistema {systemId}: {backupDir}");
            return backupDir;
        }
        catch (Exception ex)
        {
            Log($"ERRO: falha ao criar backup de {systemId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Backup do banco do sistema ANTES de uma troca que pode conter migration.
    /// Nunca move nem apaga o banco original.
    /// </summary>
    static string? BackupSystemData(string systemId)
    {
        try
        {
            var dataDir = Layout.SystemDataDir(systemId);
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

            Log($"Backup de banco de {systemId}: {backupDir}");
            return backupDir;
        }
        catch (Exception ex)
        {
            Log($"AVISO: falha ao backupar banco de {systemId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Restaura a versao anterior do sistema e, quando houve, o banco copiado.</summary>
    static void RollbackSystem(string systemId, string? systemBackup, string? dataBackup)
    {
        try
        {
            if (!string.IsNullOrEmpty(systemBackup) && Directory.Exists(systemBackup))
            {
                var targetDir = Layout.SystemDir(systemId);
                var failedDir = Path.Combine(Layout.SystemsDir, $".failed-{systemId}-{Guid.NewGuid().ToString("N")[..8]}");

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
                        // Uma copia parcial nao pode ficar no lugar da versao boa.
                        TryDeleteDirectory(targetDir);
                        if (!Directory.Exists(targetDir))
                            MoveDirectory(failedDir, targetDir);
                    }
                    throw;
                }

                TryDeleteDirectory(failedDir);
                Log($"Rollback do sistema {systemId}: versao anterior restaurada");
            }
            else
            {
                Log($"Rollback do sistema {systemId}: sem backup de software (instalacao inicial abortada)");
            }

            if (!string.IsNullOrEmpty(dataBackup) && Directory.Exists(dataBackup))
            {
                var dataDir = Layout.SystemDataDir(systemId);
                foreach (var file in Directory.GetFiles(dataBackup))
                {
                    var dest = Path.Combine(dataDir, Path.GetFileName(file));
                    File.Copy(file, dest, true);
                }
                Log($"Rollback de banco de {systemId}: restaurado de {dataBackup}");
            }
        }
        catch (Exception ex)
        {
            Log($"ERRO no rollback de {systemId}: {ex.Message}");
        }
    }

    /// <summary>Retem no maximo <see cref="SystemBackupRetention"/> backups por sistema.</summary>
    static void PruneSystemBackups(string systemId)
    {
        try
        {
            var root = Path.Combine(BackupsDir, "systems", systemId);
            if (!Directory.Exists(root))
                return;

            var dirs = Directory.GetDirectories(root)
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .Skip(SystemBackupRetention)
                .ToList();

            foreach (var dir in dirs)
            {
                TryDeleteDirectory(dir);
                Log($"Backup antigo removido ({systemId}): {dir}");
            }
        }
        catch (Exception ex)
        {
            Log($"AVISO: falha ao aplicar retencao de backup de {systemId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Move uma pasta. Directory.Move nao atravessa unidades: quando o backup fica em
    /// ProgramData (C:) e a instalacao em outra unidade, cai para copia + remocao.
    /// </summary>
    static void MoveDirectory(string sourceDir, string destDir)
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

    static void CopyDirectory(string sourceDir, string destDir)
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

    static void ParseArguments(string[] args)
    {
        CurrentState = new UpdateState
        {
            Timestamp = DateTime.UtcNow,
            Status = UpdateStatus.Idle
        };

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--manifest":
                    if (i + 1 < args.Length) CurrentState.ManifestUrl = args[++i];
                    break;
                case "--install-dir":
                    if (i + 1 < args.Length) InstallDir = args[++i];
                    break;
                case "--data-dir":
                    if (i + 1 < args.Length) DataDir = args[++i];
                    break;
                case "--system":
                    if (i + 1 < args.Length) TargetSystemId = args[++i].Trim();
                    break;
            }
        }

        // Default paths
        if (string.IsNullOrEmpty(InstallDir))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            InstallDir = Path.Combine(programFiles, AppName);
        }

        if (string.IsNullOrEmpty(DataDir))
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            DataDir = Path.Combine(programData, AppName, "data");
        }
    }

    static void InitializePaths()
    {
        // Raiz acompanha o --data-dir (PlatformLayout.DataRoot): em producao continua
        // %ProgramData%\SF Tecnologias; em testes/instalacao portatil, a pasta isolada
        // informada. Antes era fixa em ProgramData, o que acoplava testes ao estado real.
        Layout = PlatformLayout.FromDataDirectory(InstallDir, DataDir);
        var sfRoot = Layout.DataRoot;
        ConfigDir = Path.Combine(sfRoot, "config");
        BackupsDir = Path.Combine(sfRoot, "backups");
        LogsDir = Path.Combine(sfRoot, "logs");
        UpdatesTempDir = Path.Combine(sfRoot, "updates", "temp");

        Log($"Layout: install={Layout.InstallDir}, data={Layout.DataRoot}, systems={Layout.SystemsDir}");
    }

    static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(UpdatesTempDir);
    }

    static bool IsUpdaterRunning()
    {
        var currentProcess = Process.GetCurrentProcess();
        var running = Process.GetProcessesByName("SF.Updater");
        return running.Length > 1; // More than just ourselves
    }

    static string GetCurrentVersion()
    {
        // Preferred: version.json at InstallDir root (injected at package build)
        // Fallback: Electron packaged path resources/app/version.json
        var candidates = new[]
        {
            Path.Combine(InstallDir, "version.json"),
            Path.Combine(InstallDir, "resources", "app", "version.json"),
            Path.Combine(InstallDir, "resources", "app.asar.unpacked", "version.json"),
        };

        foreach (var versionFile in candidates)
        {
            if (!File.Exists(versionFile)) continue;
            try
            {
                var json = File.ReadAllText(versionFile);
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (!string.IsNullOrEmpty(versionInfo?.Version))
                {
                    Log($"Versao lida de {versionFile}: {versionInfo.Version}");
                    return versionInfo.Version;
                }
            }
            catch (Exception ex)
            {
                Log($"AVISO: Falha ao ler {versionFile}: {ex.Message}");
            }
        }

        Log("AVISO: version.json nao encontrado — assumindo 0.0.0");
        return "0.0.0";
    }

    static async Task<UpdateManifest?> DownloadManifest(string? manifestUrl)
    {
        if (string.IsNullOrEmpty(manifestUrl))
        {
            Log("ERRO: URL do manifesto nao fornecida");
            return null;
        }

        try
        {
            var json = await DownloadStringAsync(manifestUrl);
            return JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            Log($"ERRO ao baixar manifesto: {ex.Message}");
            return null;
        }
    }

    static async Task<string> DownloadStringAsync(string url)
    {
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var path = url.Replace("file:///", "").Replace("file://", "");
            path = Uri.UnescapeDataString(path);
            return await File.ReadAllTextAsync(path);
        }
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return await client.GetStringAsync(url);
    }

    static async Task<byte[]> DownloadBytesAsync(string url)
    {
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var path = url.Replace("file:///", "").Replace("file://", "");
            path = Uri.UnescapeDataString(path);
            return await File.ReadAllBytesAsync(path);
        }
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(10);
        return await client.GetByteArrayAsync(url);
    }

    static bool ValidateVersionCompatibility(string currentVersion, UpdateManifest manifest)
    {
        if (!Version.TryParse(currentVersion, out var current))
        {
            Log($"AVISO: Nao foi possivel parsear versao atual: {currentVersion}");
            return true; // Allow update if we can't parse current version
        }

        if (!Version.TryParse(manifest.Version, out var target))
        {
            Log($"ERRO: Versao alvo invalida: {manifest.Version}");
            return false;
        }

        if (target <= current)
        {
            Log($"AVISO: Versao alvo ({manifest.Version}) nao e mais recente que atual ({currentVersion})");
            // Still allow for same version (reinstall)
        }

        if (!string.IsNullOrEmpty(manifest.MinimumUpdaterVersion))
        {
            if (!Version.TryParse(manifest.MinimumUpdaterVersion, out var minUpdater))
            {
                Log($"AVISO: Versao minima do updater invalida: {manifest.MinimumUpdaterVersion}");
            }
            else
            {
                var updaterVersion = typeof(Program).Assembly.GetName().Version ?? new Version(1, 0, 0);
                if (updaterVersion < minUpdater)
                {
                    Log($"ERRO: Updater muito antigo. Minimo: {manifest.MinimumUpdaterVersion}, Atual: {updaterVersion}");
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Verifica espaco livre ANTES de baixar. Piso de 500MB para instalacao/backup;
    /// quando o manifesto declara sizeBytes do pacote, exige espaco para o download
    /// E a extracao (x2 do tamanho declarado) + piso. Recusa sem tocar em nada.
    /// </summary>
    static bool CheckDiskSpace(long requiredBytes = 0)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(InstallDir) ?? "C:\\");
            var freeBytes = drive.AvailableFreeSpace;
            Log($"Espaco livre: {freeBytes / (1024.0 * 1024 * 1024):F2} GB");

            const long floorBytes = 500L * 1024 * 1024;
            var required = requiredBytes > 0 ? requiredBytes * 2 + floorBytes : floorBytes;
            if (freeBytes < required)
            {
                Log($"ERRO: espaco insuficiente: livre={freeBytes} bytes, necessario={required} bytes");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log($"AVISO: Nao foi possivel verificar espaco em disco: {ex.Message}");
            return true; // Assume OK if we can't check
        }
    }

    static bool CheckPermissions()
    {
        try
        {
            // Try to create a test file in the install directory
            var testFile = Path.Combine(InstallDir, ".update-test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static async Task<string?> DownloadPackage(string? packageUrl, string? version, string name)
    {
        if (string.IsNullOrEmpty(packageUrl))
        {
            Log($"AVISO: Pacote {name} nao disponivel no manifesto");
            return null;
        }

        try
        {
            Log($"Baixando pacote {name} de {packageUrl}");

            var fileName = $"{name}-{version}.zip";
            var filePath = Path.Combine(UpdatesTempDir, fileName);

            var bytes = await DownloadBytesAsync(packageUrl);
            await File.WriteAllBytesAsync(filePath, bytes);

            Log($"Pacote {name} baixado: {filePath} ({bytes.Length} bytes)");
            return filePath;
        }
        catch (Exception ex)
        {
            Log($"ERRO ao baixar pacote {name}: {ex.Message}");
            return null;
        }
    }

    static bool ValidateSha256(string filePath, string expectedHash)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            Log($"SHA-256 calculado: {hashString}");
            Log($"SHA-256 esperado: {expectedHash.ToLowerInvariant()}");

            return hashString == expectedHash.ToLowerInvariant();
        }
        catch (Exception ex)
        {
            Log($"ERRO ao calcular SHA-256: {ex.Message}");
            return false;
        }
    }

    static async Task StopServices()
    {
        // Stop Windows Service if registered
        try
        {
            var service = await RunCommand("sc.exe", $"query {ServiceName}");
            if (service.Contains("RUNNING"))
            {
                Log($"Parando servico {ServiceName}...");
                await RunCommand("sc.exe", $"stop {ServiceName}");

                // Wait for service to stop
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(1000);
                    var status = await RunCommand("sc.exe", $"query {ServiceName}");
                    if (status.Contains("STOPPED"))
                    {
                        Log("Servico parado com sucesso");
                        break;
                    }
                    if (i == 29)
                    {
                        Log("AVISO: Servico nao parou dentro do timeout. Forcando...");
                        await RunCommand("sc.exe", $"stop {ServiceName}");
                        await Task.Delay(2000);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"AVISO: Erro ao verificar/parar servico: {ex.Message}");
        }

        // Kill Desktop process if running
        try
        {
            var desktopProcs = Process.GetProcessesByName("SF Tecnologias BETA");
            foreach (var proc in desktopProcs)
            {
                Log($"Matando processo Desktop (PID: {proc.Id})...");
                proc.Kill();
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Log($"AVISO: Erro ao matar Desktop: {ex.Message}");
        }
    }

    static string? CreateBackup(string version)
    {
        try
        {
            var backupDir = Path.Combine(BackupsDir, $"backup-{version}-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(backupDir);

            // Backup Desktop files (excluding updater temp)
            var desktopFiles = Directory.GetFiles(InstallDir, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains("SF.Updater.exe") && !f.Contains("updates") && !f.EndsWith(".old"))
                .ToList();

            foreach (var file in desktopFiles)
            {
                var relativePath = Path.GetRelativePath(InstallDir, file);
                var destPath = Path.Combine(backupDir, relativePath);
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);
                File.Copy(file, destPath, true);
            }

            Log($"Backup criado: {backupDir} ({desktopFiles.Count} arquivos)");
            return backupDir;
        }
        catch (Exception ex)
        {
            Log($"ERRO: Falha ao criar backup: {ex.Message}");
            return null;
        }
    }

    static async Task RollbackFromBackup(string backupPath)
    {
        try
        {
            if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
            {
                Log("ERRO: Backup indisponivel para rollback");
                return;
            }

            Log($"Rollback: restaurando de {backupPath}");

            // Stop services before restoring files
            await StopServices();

            var files = Directory.GetFiles(backupPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(backupPath, file);
                // Never restore into data dir; only InstallDir
                var destPath = Path.Combine(InstallDir, relativePath);
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);

                try
                {
                    File.Copy(file, destPath, true);
                }
                catch (IOException)
                {
                    var tempName = destPath + ".old";
                    if (File.Exists(tempName)) File.Delete(tempName);
                    if (File.Exists(destPath)) File.Move(destPath, tempName);
                    File.Copy(file, destPath, true);
                    try { File.Delete(tempName); } catch { }
                }
            }

            Log($"Rollback concluido: {files.Length} arquivos restaurados");
            SaveState(UpdateStatus.RollbackRequired, $"Rolled back from {backupPath}");
            await StartServices();
        }
        catch (Exception ex)
        {
            Log($"ERRO no rollback: {ex.Message}");
        }
    }

    static void UpdateUpdater(string packagePath)
    {
        // Extract to temp, then swap running SF.Updater.exe (rename allowed while running)
        var tempExtract = Path.Combine(UpdatesTempDir, "updater-extract-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            ZipFile.ExtractToDirectory(packagePath, tempExtract);

            var newExe = Directory.GetFiles(tempExtract, "SF.Updater.exe", SearchOption.AllDirectories).FirstOrDefault();
            var currentExe = Path.Combine(InstallDir, "SF.Updater.exe");

            if (newExe != null && File.Exists(currentExe))
            {
                var oldPath = currentExe + ".old";
                if (File.Exists(oldPath)) File.Delete(oldPath);
                File.Move(currentExe, oldPath);
                File.Copy(newExe, currentExe, true);
                try { File.Delete(oldPath); } catch { /* cleaned on next run if locked */ }
                Log("SF.Updater.exe substituido");
            }
            else
            {
                // Fallback: extract into InstallDir (skips locked self)
                foreach (var file in Directory.GetFiles(tempExtract, "*.*", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(file).Equals("SF.Updater.exe", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var rel = Path.GetRelativePath(tempExtract, file);
                    var dest = Path.Combine(InstallDir, rel);
                    var dir = Path.GetDirectoryName(dest);
                    if (dir != null) Directory.CreateDirectory(dir);
                    File.Copy(file, dest, true);
                }
                Log("Updater extraido (self-swap parcial — verifique .old)");
            }
        }
        finally
        {
            try { Directory.Delete(tempExtract, true); } catch { }
        }
    }

    static void UpdateDesktop(string packagePath)
    {
        ExtractPackage(packagePath, InstallDir);
    }

    static void UpdateApi(string packagePath)
    {
        var apiDir = Path.Combine(InstallDir, "resources", "api");
        Directory.CreateDirectory(apiDir);
        ExtractPackage(packagePath, apiDir);
    }

    static void ExtractPackage(string packagePath, string targetDir)
    {
        Log($"Extraindo pacote {Path.GetFileName(packagePath)} para {targetDir}");

        // Extract to a temp directory first, then move
        var tempExtract = Path.Combine(UpdatesTempDir, "extract-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            ZipFile.ExtractToDirectory(packagePath, tempExtract);

            // Copy files from temp to target
            var files = Directory.GetFiles(tempExtract, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(tempExtract, file);
                var destPath = Path.Combine(targetDir, relativePath);
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);

                try
                {
                    File.Copy(file, destPath, true);
                }
                catch (IOException)
                {
                    // File might be in use, try to rename and replace
                    var tempName = destPath + ".old";
                    if (File.Exists(tempName)) File.Delete(tempName);
                    File.Move(destPath, tempName);
                    File.Copy(file, destPath, true);
                    try { File.Delete(tempName); } catch { }
                }
            }

            Log($"Pacote extraido: {files.Length} arquivos");
        }
        finally
        {
            // Cleanup temp extract
            try { Directory.Delete(tempExtract, true); } catch { }
        }
    }

    static async Task StartServices()
    {
        // Start Windows Service
        try
        {
            var service = await RunCommand("sc.exe", $"query {ServiceName}");
            if (service.Contains("STOPPED") || service.Contains("PAUSED"))
            {
                Log($"Iniciando servico {ServiceName}...");
                await RunCommand("sc.exe", $"start {ServiceName}");
                await Task.Delay(3000);
            }
        }
        catch (Exception ex)
        {
            Log($"AVISO: Erro ao iniciar servico: {ex.Message}");
        }
    }

    static void StartDesktop()
    {
        try
        {
            var desktopExe = Path.Combine(InstallDir, "SF Tecnologias BETA.exe");
            if (File.Exists(desktopExe))
            {
                Log("Iniciando Desktop...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = desktopExe,
                    UseShellExecute = true,
                    WorkingDirectory = InstallDir
                });
            }
            else
            {
                Log("AVISO: Executavel do Desktop nao encontrado");
            }
        }
        catch (Exception ex)
        {
            Log($"AVISO: Erro ao iniciar Desktop: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    static async Task<bool> HealthCheck()
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            for (int i = 0; i < 15; i++)
            {
                try
                {
                    var response = await client.GetAsync("http://localhost:5000/health");
                    if (response.IsSuccessStatusCode)
                    {
                        Log("Health check OK");
                        return true;
                    }
                    Log($"Health check attempt {i + 1}: HTTP {(int)response.StatusCode}");
                }
                catch (Exception ex)
                {
                    Log($"Health check attempt {i + 1}: {ex.GetType().Name}: {ex.Message}");
                }
                await Task.Delay(2000);
            }

            Log("AVISO: Health check falhou apos 15 tentativas");
            return false;
        }
        catch (Exception ex)
        {
            Log($"AVISO: Erro no health check: {ex.GetType().FullName}: {ex.Message}");
            return false;
        }
    }

    static async Task AttemptRecovery()
    {
        Log("Tentando recuperar...");

        // Try to restart API
        try
        {
            var service = await RunCommand("sc.exe", $"query {ServiceName}");
            if (service.Contains("STOPPED"))
            {
                Log("Tentando reiniciar API...");
                await RunCommand("sc.exe", $"start {ServiceName}");
            }
        }
        catch { }

        // Try to restart Desktop
        StartDesktop();
    }

    static void CleanupTempFiles()
    {
        try
        {
            if (Directory.Exists(UpdatesTempDir))
            {
                Directory.Delete(UpdatesTempDir, true);
                Directory.CreateDirectory(UpdatesTempDir);
                Log("Arquivos temporarios limpos");
            }
        }
        catch { }
    }

    /// <summary>
    /// Carrega do updater-state.json os sistemas ja registrados como instalados.
    /// Sem isto cada execucao (inclusive uma que falhou) gravaria Systems vazio e
    /// apagaria a auditoria de versoes instaladas.
    /// </summary>
    static void LoadPreviousSystemState()
    {
        try
        {
            var stateFile = Path.Combine(ConfigDir, "updater-state.json");
            if (!File.Exists(stateFile))
                return;

            var previous = JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(stateFile), new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

            if (previous?.Systems == null || previous.Systems.Count == 0)
                return;

            InstalledSystems.Clear();
            InstalledSystems.AddRange(previous.Systems);
            CurrentState.Systems = InstalledSystems;
        }
        catch
        {
            // Estado anterior ilegivel nao pode impedir a atualizacao.
        }
    }

    static void SaveState(UpdateStatus status, string message)
    {
        CurrentState.Status = status;
        CurrentState.Message = message;
        CurrentState.Timestamp = DateTime.UtcNow;

        var stateFile = Path.Combine(ConfigDir, "updater-state.json");
        try
        {
            var json = JsonSerializer.Serialize(CurrentState, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
            File.WriteAllText(stateFile, json);
        }
        catch { }

        Log($"Estado: {status} - {message}");
    }

    static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

        try
        {
            Console.WriteLine(line);
        }
        catch
        {
            // Console pode nao existir (servico) - o arquivo segue sendo a fonte.
        }

        try
        {
            File.AppendAllText(LogFile, line + Environment.NewLine);
        }
        catch
        {
            if (string.IsNullOrEmpty(LogFile))
                return;

            try
            {
                // O arquivo pode ter sido criado por um processo elevado com ACL restrita:
                // recria com a heranca da pasta para o processo atual voltar a escrever nele.
                if (File.Exists(LogFile))
                    File.Delete(LogFile);
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
            catch
            {
                try
                {
                    LogFile = Path.Combine(Path.GetTempPath(), $"SF-Updater-{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(LogFile, line + Environment.NewLine);
                }
                catch
                {
                    // Logging must never throw (it can run inside catch blocks)
                }
            }
        }
    }

    static async Task<string> RunCommand(string command, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }
        catch (Exception ex)
        {
            Log($"ERRO ao executar {command}: {ex.Message}");
            return string.Empty;
        }
    }
}

// ============================================
// MODELS
// ============================================

enum UpdateStatus
{
    Idle,
    Downloading,
    Downloaded,
    Validating,
    Validated,
    StoppingServices,
    ServicesStopped,
    BackingUp,
    BackupCompleted,
    UpdatingDesktop,
    DesktopUpdated,
    UpdatingApi,
    ApiUpdated,
    UpdatingUpdater,
    UpdaterUpdated,
    UpdatingSystems,
    SystemsUpdated,
    StartingServices,
    ServicesStarted,
    HealthCheckPassed,
    Completed,
    Failed,
    RollbackRequired
}

class UpdateState
{
    public string? ManifestUrl { get; set; }
    public UpdateStatus Status { get; set; }
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Version { get; set; }
    public List<InstalledSystemState> Systems { get; set; } = new();
}

/// <summary>Sistema que ficou instalado apos a atualizacao (auditoria do updater-state.json).</summary>
class InstalledSystemState
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Sha256 { get; set; }
    /// <summary>true quando a versao instalada ja estava atualizada e nao foi baixada de novo.</summary>
    public bool Reused { get; set; }
}

class UpdateManifest
{
    public string Product { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? MinimumUpdaterVersion { get; set; }
    public ComponentGroup Components { get; set; } = new();
    public bool Mandatory { get; set; }
    public string? ReleaseNotes { get; set; }
}

class ComponentGroup
{
    public ComponentInfo? Desktop { get; set; }
    public ComponentInfo? Api { get; set; }
    public ComponentInfo? Updater { get; set; }

    /// <summary>Sistemas empresariais independentes (System Contract).</summary>
    public List<SystemPackageEntry>? Systems { get; set; }
}

class ComponentInfo
{
    public string Version { get; set; } = string.Empty;
    public string? PackageUrl { get; set; }
    public string? Sha256 { get; set; }
    /// <summary>Tamanho declarado do pacote (guarda de espaco em disco do updater).</summary>
    public long? SizeBytes { get; set; }
}

class VersionInfo
{
    public string Version { get; set; } = string.Empty;
}
