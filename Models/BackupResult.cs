namespace BOH.Models;

public enum BackupStatus
{
    /// <summary>Every file was copied.</summary>
    Success,
    /// <summary>The backup folder exists but some files could not be copied (it is named "..._incomplete").</summary>
    Partial,
    /// <summary>No backup folder was created.</summary>
    Failed,
    /// <summary>Stopped by the user; the unfinished folder was removed.</summary>
    Cancelled
}

public class BackupResult
{
    public BackupStatus Status { get; set; } = BackupStatus.Failed;
    public bool Success => Status == BackupStatus.Success;
    public string PairName { get; set; } = string.Empty;
    public string DestinationUsed { get; set; } = string.Empty;
    public bool UsedFallback { get; set; }
    public long BytesCopied { get; set; }
    public int FilesCopied { get; set; }
    /// <summary>Files (or folders) that could not be copied, with the reason.</summary>
    public List<string> SkippedFiles { get; } = new();
    /// <summary>Non-fatal problems, e.g. an old backup that could not be deleted.</summary>
    public List<string> Warnings { get; } = new();
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Message { get; set; } = string.Empty;

    public string LevelTag => Tag(Level);

    public static string Tag(LogLevel level) => level switch
    {
        LogLevel.Info => "INFO",
        LogLevel.Success => " OK ",
        LogLevel.Warning => "WARN",
        LogLevel.Error => " ERR",
        _ => "    "
    };
}

public enum LogLevel { Info, Success, Warning, Error }
