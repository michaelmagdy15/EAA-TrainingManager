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

    public BackupService(string sourceDbPath)
    {
        _sourceDbPath = sourceDbPath;
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _backupFolder = Path.Combine(localAppData, "EAA_TrainingManager", "Backups");
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

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
            string backupFileName = $"eaa_backup_{timestamp}_{reason}.db";
            string destPath = Path.Combine(_backupFolder, backupFileName);

            // Use native SQLite online backup API to ensure full ACID safety without database locking
            using (var srcConn = new SqliteConnection($"Data Source={_sourceDbPath};"))
            {
                await srcConn.OpenAsync();
                using (var dstConn = new SqliteConnection($"Data Source={destPath};"))
                {
                    await dstConn.OpenAsync();
                    srcConn.BackupDatabase(dstConn);
                }
            }

            // Rolling retention: keep newest 30 backups to prevent disk bloat
            EnforceRetentionPolicy(30);

            return destPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BackupService.CreateSnapshotAsync] {ex.Message}");
            return null;
        }
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
                    try { files[i].Delete(); } catch { }
                }
            }
        }
        catch { }
    }

    public List<FileInfo> GetBackupSnapshots()
    {
        try
        {
            var dir = new DirectoryInfo(_backupFolder);
            return dir.GetFiles("eaa_backup_*.db").OrderByDescending(f => f.CreationTime).ToList();
        }
        catch
        {
            return new List<FileInfo>();
        }
    }
}
