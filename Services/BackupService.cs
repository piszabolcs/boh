using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BOH.Models;
using Newtonsoft.Json;

namespace BOH.Services;

/// <summary>
/// Copies every enabled pair into "&lt;destination&gt;\&lt;name&gt;_&lt;yyyy-MM-dd_HHmmss&gt;" and applies the retention rule.
/// A backup is written under a ".partial" name and only renamed once the copy has finished; it carries a marker
/// file (pair id + status), so retention deletes nothing but folders that this pair created.
/// </summary>
public class BackupService
{
    public const string MarkerFileName = ".boh-backup.json";
    public const string TimestampFormat = "yyyy-MM-dd_HHmmss";
    public const string IncompleteSuffix = "_incomplete";
    public const string InProgressSuffix = ".partial";
    public const string DeletingSuffix = ".deleting";
    private const string MarkerComplete = "complete";
    private const string MarkerIncomplete = "incomplete";

    private readonly Func<DateTime> _clock;

    /// <param name="clock">Source of the backup folder timestamps (tests pass a fixed clock).</param>
    public BackupService(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.Now);

    /// <summary>Wait before the single retry of a file that could not be copied (e.g. a briefly locked file).</summary>
    public TimeSpan FileRetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public event Action<LogEntry>? LogMessage;

    private void Log(string message, LogLevel level = LogLevel.Info)
        => LogMessage?.Invoke(new LogEntry { Message = message, Level = level });

    public async Task<List<BackupResult>> RunAllAsync(BackupConfig config, CancellationToken ct = default)
    {
        var results = new List<BackupResult>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in config.Pairs.Where(p => p.Enabled))
        {
            ct.ThrowIfCancellationRequested();
            if (!names.Add((pair.Name ?? string.Empty).Trim()))
            {
                Log($"▶  Started: {pair.Name}");
                var duplicate = new BackupResult { PairName = pair.Name ?? string.Empty, StartTime = DateTime.Now };
                results.Add(Fail(duplicate, $"Another enabled pair is already named \"{pair.Name?.Trim()}\"; each pair needs its own name."));
                continue;
            }
            results.Add(await RunPairAsync(pair, config, ct).ConfigureAwait(false));
        }
        return results;
    }

    public async Task<BackupResult> RunPairAsync(BackupPair pair, BackupConfig config, CancellationToken ct = default)
    {
        var result = new BackupResult { PairName = pair.Name ?? string.Empty, StartTime = DateTime.Now };
        Log($"▶  Started: {pair.Name}");
        string? workPath = null;
        string destination = string.Empty;

        try
        {
            var nameError = PairNames.Validate(pair.Name);
            if (nameError != null)
                return Fail(result, nameError);
            var name = pair.Name!.Trim();

            var source = ResolvePath(pair.SourcePath);
            if (source.Length == 0 || !Directory.Exists(source))
                return Fail(result, $"Source folder not found: {source}");

            destination = ResolvePath(pair.DestinationPath);
            if (!await IsPathReachableAsync(destination, config.NetworkTimeoutSeconds).ConfigureAwait(false))
            {
                if (!config.EnableFallback || string.IsNullOrWhiteSpace(config.FallbackPath))
                    return Fail(result, $"Destination not reachable: {destination}");
                destination = ResolvePath(config.FallbackPath);
                result.UsedFallback = true;
                Log($"⚠  Destination not reachable, using the fallback folder: {destination}", LogLevel.Warning);
            }

            var overlap = CheckOverlap(source, destination);
            if (overlap != null)
                return Fail(result, overlap);

            var finalPath = Path.Combine(destination, $"{name}_{_clock().ToString(TimestampFormat, CultureInfo.InvariantCulture)}");
            if (Directory.Exists(finalPath) || Directory.Exists(finalPath + IncompleteSuffix) || Directory.Exists(finalPath + InProgressSuffix))
                return Fail(result, $"A backup with the same timestamp already exists: {finalPath}");

            workPath = finalPath + InProgressSuffix;
            Directory.CreateDirectory(workPath);
            Log($"   Writing to: {workPath}");
            await Task.Run(() => CopyTree(source, workPath, result, ct), ct).ConfigureAwait(false);
            if (result.FilesCopied == 0 && result.SkippedFiles.Count > 0)
                throw new IOException($"no file could be copied (first problem: {result.SkippedFiles[0]})");

            result.Status = result.SkippedFiles.Count == 0 ? BackupStatus.Success : BackupStatus.Partial;
            WriteMarker(workPath, pair, result);
            result.DestinationUsed = result.Status == BackupStatus.Success ? finalPath : finalPath + IncompleteSuffix;
            Directory.Move(workPath, result.DestinationUsed);
            Log($"   Copied {result.FilesCopied} files, {FormatBytes(result.BytesCopied)} → {Path.GetFileName(result.DestinationUsed)}", LogLevel.Success);
        }
        catch (OperationCanceledException)
        {
            result.Status = BackupStatus.Cancelled;
            result.ErrorMessage = "Cancelled.";
            Log("   Cancelled; the unfinished backup folder is removed.", LogLevel.Warning);
            TryDeleteTree(workPath, result);
        }
        catch (Exception ex)
        {
            // One pair failing must not stop the others; the reason is logged and reported in the result.
            Fail(result, $"Backup failed: {ex.Message}");
            TryDeleteTree(workPath, result);
        }

        if ((result.Status is BackupStatus.Success or BackupStatus.Partial) && !config.KeepAll && config.KeepCount > 0)
        {
            try
            {
                await Task.Run(() => ApplyRetention(destination, pair, config.KeepCount, result)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // e.g. the network share dropped after the copy: the new backup is fine, old ones stay for now
                Warn(result, $"Old backups were not checked: {ex.Message}");
            }
        }

        result.EndTime = DateTime.Now;
        if (result.Status == BackupStatus.Success)
            Log($"✓  Done: {pair.Name} ({Seconds(result.Duration)})", LogLevel.Success);
        else if (result.Status == BackupStatus.Partial)
            Log($"⚠  Done with problems: {pair.Name}: {result.SkippedFiles.Count} item(s) skipped, the backup is marked incomplete ({Seconds(result.Duration)})", LogLevel.Warning);
        return result;
    }

    private BackupResult Fail(BackupResult result, string message)
    {
        result.Status = BackupStatus.Failed;
        result.ErrorMessage = message;
        result.EndTime = DateTime.Now;
        Log("✗  " + message, LogLevel.Error);
        return result;
    }

    // ── Copy ────────────────────────────────────────────────────────────────

    private void CopyTree(string source, string target, BackupResult result, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            CopyFile(file, Path.Combine(target, Path.GetFileName(file)), result);
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            if (new DirectoryInfo(dir).LinkTarget != null)
            {
                // Junctions and symbolic links are not followed: they can loop or point outside the source.
                Log($"   Skipped link (junction / symbolic link): {dir}", LogLevel.Warning);
                continue;
            }
            try
            {
                CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)), result, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.SkippedFiles.Add($"{dir}{Path.DirectorySeparatorChar} ({ex.Message})");
                Log($"   Skipped folder: {dir}: {ex.Message}", LogLevel.Warning);
            }
        }
    }

    private void CopyFile(string source, string target, BackupResult result)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, target, overwrite: true);
                result.FilesCopied++;
                result.BytesCopied += new FileInfo(target).Length;
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 1 && ex is not FileNotFoundException)
                {
                    Thread.Sleep(FileRetryDelay);
                    continue;
                }
                result.SkippedFiles.Add($"{source} ({ex.Message})");
                Log($"   Skipped file: {source}: {ex.Message}", LogLevel.Warning);
                return;
            }
        }
    }

    // ── Retention ───────────────────────────────────────────────────────────

    /// <summary>
    /// Keeps the newest <paramref name="keepCount"/> backups of this pair. Only folders named exactly
    /// "&lt;name&gt;_&lt;timestamp&gt;[_incomplete]" whose marker carries this pair's id are considered; the backup
    /// just made and the newest complete backup are never deleted.
    /// </summary>
    private void ApplyRetention(string destination, BackupPair pair, int keepCount, BackupResult result)
    {
        var pattern = new Regex($@"^{Regex.Escape(pair.Name.Trim())}_(\d{{4}}-\d{{2}}-\d{{2}}_\d{{6}})({IncompleteSuffix})?$",
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var backups = new List<(string Path, DateTime Time, bool Complete)>();
        foreach (var dir in Directory.EnumerateDirectories(destination))
        {
            var match = pattern.Match(Path.GetFileName(dir));
            if (!match.Success
                || !DateTime.TryParseExact(match.Groups[1].Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                || ReadMarker(dir) is not { } marker
                || !string.Equals(marker.PairId, pair.Id, StringComparison.Ordinal))
                continue;
            backups.Add((dir, time, marker.Status == MarkerComplete && !match.Groups[2].Success));
        }

        var newestFirst = backups.OrderByDescending(b => b.Time).ToList();
        var newestComplete = newestFirst.FirstOrDefault(b => b.Complete).Path;
        foreach (var old in newestFirst.Skip(keepCount))
        {
            if (SamePath(old.Path, result.DestinationUsed) || SamePath(old.Path, newestComplete))
                continue;
            DeleteBackup(old.Path, result);
        }
    }

    private void DeleteBackup(string folder, BackupResult result)
    {
        try
        {
            // Rename first: if the delete stops half-way, the rest no longer looks like a valid backup.
            var doomed = folder + DeletingSuffix;
            Directory.Move(folder, doomed);
            DeleteTree(doomed);
            Log($"   Deleted old backup: {Path.GetFileName(folder)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn(result, $"Could not delete old backup {Path.GetFileName(folder)}: {ex.Message}");
        }
    }

    private void TryDeleteTree(string? folder, BackupResult result)
    {
        if (folder == null || !Directory.Exists(folder))
            return;
        try
        {
            DeleteTree(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn(result, $"Could not remove the unfinished folder {folder}: {ex.Message}");
        }
    }

    /// <summary>Deletes a folder tree; read-only files (File.Copy keeps that attribute) would otherwise stop it.</summary>
    private static void DeleteTree(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
                entry.Attributes &= ~FileAttributes.ReadOnly;
        }
        Directory.Delete(folder, recursive: true);
    }

    private void Warn(BackupResult result, string message)
    {
        result.Warnings.Add(message);
        Log("   " + message, LogLevel.Warning);
    }

    // ── Marker ──────────────────────────────────────────────────────────────

    internal sealed class BackupMarker
    {
        public string PairId { get; set; } = string.Empty;
        public string PairName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int Files { get; set; }
        public long Bytes { get; set; }
        public List<string> Skipped { get; set; } = new();
    }

    private void WriteMarker(string folder, BackupPair pair, BackupResult result)
    {
        var marker = new BackupMarker
        {
            PairId = pair.Id,
            PairName = pair.Name.Trim(),
            Status = result.Status == BackupStatus.Success ? MarkerComplete : MarkerIncomplete,
            CreatedAt = _clock(),
            Files = result.FilesCopied,
            Bytes = result.BytesCopied,
            Skipped = result.SkippedFiles.ToList(),
        };
        File.WriteAllText(Path.Combine(folder, MarkerFileName), JsonConvert.SerializeObject(marker, Formatting.Indented));
    }

    private static BackupMarker? ReadMarker(string folder)
    {
        var path = Path.Combine(folder, MarkerFileName);
        try
        {
            return File.Exists(path) ? JsonConvert.DeserializeObject<BackupMarker>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // unreadable marker: the folder is treated as foreign and kept
        }
    }

    // ── Paths ───────────────────────────────────────────────────────────────

    private static string? CheckOverlap(string source, string destination)
    {
        var src = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var dst = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            return "Source and destination are the same folder.";
        var sourcePrefix = src.EndsWith(Path.DirectorySeparatorChar) ? src : src + Path.DirectorySeparatorChar;
        if (dst.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            return $"The destination {dst} is inside the source folder, so the backup would copy itself.";
        return null;
    }

    /// <summary>The drive or share of the path answers within the timeout (no ICMP ping: firewalls block it).</summary>
    private static async Task<bool> IsPathReachableAsync(string path, int timeoutSeconds)
    {
        string? root;
        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        if (string.IsNullOrEmpty(root))
            return false;

        // Directory.Exists on an offline network share can hang for a long time, hence the time limit.
        var check = Task.Run(() => Directory.Exists(root));
        var timeout = Task.Delay(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 60)));
        return await Task.WhenAny(check, timeout).ConfigureAwait(false) == check && check.Result;
    }

    /// <summary>Relative paths (portable use, e.g. "..\Backups") are relative to the folder of BOH.exe.</summary>
    private static string ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        path = path.Trim();
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static bool SamePath(string? a, string? b)
        => a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F1} KB"),
        < 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):F1} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):F2} GB"),
    };

    private static string Seconds(TimeSpan duration)
        => string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:F1} s");
}
