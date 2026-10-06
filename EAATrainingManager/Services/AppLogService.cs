using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace EAATrainingManager.Services;

public static class AppLogService
{
    private const long MaximumLogFileSize = 5 * 1024 * 1024;
    private const int MaximumRotatedFiles = 5;
    private static readonly object Sync = new();
    private static string? _configuredDirectory;
    private static readonly string SessionId = Guid.NewGuid().ToString("N");

    public static void ConfigureDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("A log directory is required.", nameof(directory));

        lock (Sync)
            _configuredDirectory = Path.GetFullPath(directory);
    }

    public static void LogException(
        string operation,
        Exception exception,
        string? entityType = null,
        string? entityId = null,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);

        string eventCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId;

        var entry = new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            level = "Error",
            operation,
            entityType,
            entityId,
            user = Environment.UserName,
            sessionId = SessionId,
            correlationId = eventCorrelationId,
            exceptionType = exception.GetType().FullName,
            message = exception.Message,
            stackTrace = exception.StackTrace
        };

        try
        {
            lock (Sync)
            {
                string directory = GetLogDirectory();
                Directory.CreateDirectory(directory);
                string activeLog = Path.Combine(directory, "application.jsonl");
                RotateIfNeeded(activeLog);
                File.AppendAllText(activeLog, JsonSerializer.Serialize(entry) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception loggingException)
        {
            System.Diagnostics.Debug.WriteLine($"[AppLogService] Failed to persist diagnostic event {eventCorrelationId}: {loggingException}");
        }
    }

    private static string GetLogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_configuredDirectory))
            return _configuredDirectory;

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "EAA_TrainingManager", "Logs");
    }

    private static void RotateIfNeeded(string activeLog)
    {
        if (!File.Exists(activeLog) || new FileInfo(activeLog).Length < MaximumLogFileSize)
            return;

        string oldest = $"{activeLog}.{MaximumRotatedFiles}";
        if (File.Exists(oldest))
            File.Delete(oldest);

        for (int suffix = MaximumRotatedFiles - 1; suffix >= 1; suffix--)
        {
            string existing = $"{activeLog}.{suffix}";
            if (File.Exists(existing))
                File.Move(existing, $"{activeLog}.{suffix + 1}", overwrite: true);
        }

        File.Move(activeLog, $"{activeLog}.1", overwrite: true);
    }
}
