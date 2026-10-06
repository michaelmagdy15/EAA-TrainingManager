using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace EAATrainingManager.Services;

public class BackupService
{
    private readonly string _sourceDbPath;
    private readonly string _backupFolder;

    public BackupService(string sourceDbPath, string? backupFolder = null)
    {
        _sourceDbPath = Path.GetFullPath(sourceDbPath);
        if (string.IsNullOrWhiteSpace(backupFolder))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            backupFolder = Path.Combine(localAppData, "EAA_TrainingManager", "Backups");
        }
        _backupFolder = Path.GetFullPath(backupFolder);
        Directory.CreateDirectory(_backupFolder);
    }

    public string BackupFolder => _backupFolder;

    /// <summary>
    /// Creates a hot timestamped snapshot of the SQLite database using the native SQLite Backup API.
    /// </summary>
    public async Task<string?> CreateSnapshotAsync(string reason = "Auto")
    {
        try
        {
            if (!File.Exists(_sourceDbPath)) return null;

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss_fff");
            string safeReason = SanitizeFileComponent(reason);
            string backupFileName = $"eaa_backup_{timestamp}_{safeReason}.db";
            string destPath = Path.Combine(_backupFolder, backupFileName);

            using (var srcConn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _sourceDbPath,
                Pooling = false
            }.ToString()))
            {
                await srcConn.OpenAsync();
                using (var dstConn = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = destPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                }.ToString()))
                {
                    await dstConn.OpenAsync();
                    srcConn.BackupDatabase(dstConn);
                }
            }

            await ValidateDatabaseAsync(destPath);

            // Rolling retention: keep newest 30 backups to prevent disk bloat
            EnforceRetentionPolicy(30);

            return destPath;
        }
        catch (Exception ex)
        {
            AppLogService.LogException("Backup.CreateSnapshot", ex, "SQLiteDatabase", Path.GetFileName(_sourceDbPath));
            return null;
        }
    }

    public async Task<bool> RestoreSnapshotAsync(string snapshotPath)
    {
        string correlationId = Guid.NewGuid().ToString("N");
        try
        {
            string fullSnapshotPath = Path.GetFullPath(snapshotPath);
            if (!File.Exists(fullSnapshotPath))
                throw new FileNotFoundException("The selected database snapshot does not exist.", fullSnapshotPath);
            if (string.Equals(fullSnapshotPath, _sourceDbPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active database cannot be used as its own restore snapshot.");

            await ValidateDatabaseAsync(fullSnapshotPath);

            if (File.Exists(_sourceDbPath))
            {
                string? safetySnapshot = await CreateSnapshotAsync("PreRestore");
                if (string.IsNullOrWhiteSpace(safetySnapshot))
                    throw new IOException("Could not create a pre-restore safety snapshot; restore was not attempted.");
            }

            // Connections are short-lived in this service; clear only idle pooled handles before replacing pages.
            SqliteConnection.ClearAllPools();
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = fullSnapshotPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString()))
            {
                using var target = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = _sourceDbPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                }.ToString());
                await source.OpenAsync();
                await target.OpenAsync();
                source.BackupDatabase(target);
            }

            await ValidateDatabaseAsync(_sourceDbPath);
            return true;
        }
        catch (Exception ex)
        {
            AppLogService.LogException("Backup.RestoreSnapshot", ex, "SQLiteDatabase", Path.GetFileName(_sourceDbPath), correlationId);
            return false;
        }
    }

    private static async Task ValidateDatabaseAsync(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        string? result = Convert.ToString(await command.ExecuteScalarAsync());
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SQLite integrity check failed for '{Path.GetFileName(databasePath)}': {result ?? "no result"}.");
    }

    private static string SanitizeFileComponent(string value)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? "Auto" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');
        return safe.Length > 40 ? safe[..40] : safe;
    }

    private void EnforceRetentionPolicy(int maxBackupsToKeep)
    {
        try
        {
            var dir = new DirectoryInfo(_backupFolder);
            var files = dir.GetFiles("eaa_backup_*.db").OrderByDescending(f => f.CreationTime).ToList();
            if (files.Count > maxBackupsToKeep)
            {
                for (int i = maxBackupsToKeep; i < files.Count; i++)
                {
                    try { files[i].Delete(); }
                    catch (Exception ex)
                    {
                        AppLogService.LogException("Backup.Retention.Delete", ex, "BackupFile", files[i].Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogService.LogException("Backup.Retention", ex, "BackupDirectory", _backupFolder);
        }
    }

    public List<FileInfo> GetBackupSnapshots()
    {
        try
        {
            var dir = new DirectoryInfo(_backupFolder);
            return dir.GetFiles("eaa_backup_*.db").OrderByDescending(f => f.CreationTime).ToList();
        }
        catch (Exception ex)
        {
            AppLogService.LogException("Backup.ListSnapshots", ex, "BackupDirectory", _backupFolder);
            return new List<FileInfo>();
        }
    }
}
