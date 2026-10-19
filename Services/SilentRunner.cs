using System.Globalization;
using System.IO;
using BOH.Models;

namespace BOH.Services;

/// <summary>"BOH.exe --run-backup" (used by the scheduled task): no window, log file, exit code.</summary>
public static class SilentRunner
{
    /// <summary>Every enabled pair was backed up completely.</summary>
    public const int ExitSuccess = 0;
    /// <summary>At least one pair failed or is incomplete.</summary>
    public const int ExitBackupProblem = 1;
    /// <summary>Unusable settings, no enabled pair, or an unexpected error.</summary>
    public const int ExitConfigProblem = 2;

    /// <summary>Blocks until the run is finished. The work runs on the thread pool, so the wait cannot deadlock
    /// on a UI synchronization context (the cause of the old endless hang).</summary>
    public static int Run(ConfigService store) => Task.Run(() => RunAsync(store)).GetAwaiter().GetResult();

    public static async Task<int> RunAsync(ConfigService store)
    {
        var log = new FileLog(store.LogPath);
        try
        {
            log.Write(LogLevel.Info, $"Scheduled run started, settings: {store.ConfigPath}");
            var config = store.Load();
            if (store.LoadWarning != null)
            {
                log.Write(LogLevel.Error, store.LoadWarning);
                return ExitConfigProblem;
            }
            if (!config.Pairs.Any(p => p.Enabled))
            {
                log.Write(LogLevel.Warning, "No enabled backup pair; nothing to do.");
                return ExitConfigProblem;
            }

            var service = new BackupService();
            service.LogMessage += e => log.Write(e.Level, e.Message);
            var results = await service.RunAllAsync(config).ConfigureAwait(false);
            int complete = results.Count(r => r.Status == BackupStatus.Success);
            bool allDone = complete == results.Count;
            log.Write(allDone ? LogLevel.Success : LogLevel.Warning, $"Finished: {complete}/{results.Count} pair(s) backed up completely.");
            return allDone ? ExitSuccess : ExitBackupProblem;
        }
        catch (Exception ex)
        {
            log.Write(LogLevel.Error, $"Unexpected error: {ex}");
            return ExitConfigProblem;
        }
    }

    private sealed class FileLog(string path)
    {
        private readonly object _gate = new();

        public void Write(LogLevel level, string message)
        {
            var line = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] [{LogEntry.Tag(level)}] {message}{Environment.NewLine}";
            lock (_gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.AppendAllText(path, line);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A log that cannot be written must not stop the backup; the exit code still reports the outcome.
                }
            }
        }
    }
}
