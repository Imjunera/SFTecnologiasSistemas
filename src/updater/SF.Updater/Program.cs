using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

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
    private static UpdateState CurrentState = new();

    static async Task<int> Main(string[] args)
    {
        ParseArguments(args);
        InitializePaths();
        EnsureDirectories();

        LogFile = Path.Combine(LogsDir, $"updater-{DateTime.Now:yyyy-MM-dd}.log");
        Log("========================================");
        Log("SF.Updater iniciado");
        Log($"Arguments: manifest={CurrentState.ManifestUrl}, install-dir={InstallDir}");

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

            // Check disk space
            if (!CheckDiskSpace())
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
            var desktopPackage = await DownloadPackage(manifest.Components.Desktop, "desktop");
            var apiPackage = await DownloadPackage(manifest.Components.Api, "api");
            var updaterPackage = await DownloadPackage(manifest.Components.Updater, "updater");

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
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var sfRoot = Path.Combine(programData, AppName);
        ConfigDir = Path.Combine(sfRoot, "config");
        BackupsDir = Path.Combine(sfRoot, "backups");
        LogsDir = Path.Combine(sfRoot, "logs");
        UpdatesTempDir = Path.Combine(sfRoot, "updates", "temp");
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

    static bool CheckDiskSpace()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(InstallDir) ?? "C:\\");
            var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
            Log($"Espaco livre: {freeSpaceGB:F2} GB");

            // Require at least 500 MB free
            return freeSpaceGB >= 0.5;
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

    static async Task<string?> DownloadPackage(ComponentInfo? component, string name)
    {
        if (component == null || string.IsNullOrEmpty(component.PackageUrl))
        {
            Log($"AVISO: Pacote {name} nao disponivel no manifesto");
            return null;
        }

        try
        {
            Log($"Baixando pacote {name} de {component.PackageUrl}");

            var fileName = $"{name}-{component.Version}.zip";
            var filePath = Path.Combine(UpdatesTempDir, fileName);

            var bytes = await DownloadBytesAsync(component.PackageUrl);
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
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Console.WriteLine(line);
            File.AppendAllText(LogFile, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never throw (it can run inside catch blocks)
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
}

class ComponentInfo
{
    public string Version { get; set; } = string.Empty;
    public string? PackageUrl { get; set; }
    public string? Sha256 { get; set; }
}

class VersionInfo
{
    public string Version { get; set; } = string.Empty;
}
