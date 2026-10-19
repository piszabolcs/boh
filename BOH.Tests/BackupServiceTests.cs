using BOH.Models;
using BOH.Services;
using Newtonsoft.Json.Linq;
using static BOH.Tests.TempDir;

namespace BOH.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly List<LogEntry> _log = new();
    private readonly BackupService _service;
    private DateTime _now = new(2026, 10, 11, 2, 0, 0);

    public BackupServiceTests()
    {
        _service = new BackupService(() => _now) { FileRetryDelay = TimeSpan.Zero };
        _service.LogMessage += e => { lock (_log) _log.Add(e); };
    }

    public void Dispose() => _tmp.Dispose();

    /// <summary>One backup run; the fake clock then moves on a minute so every run gets its own folder.</summary>
    private async Task<BackupResult> Run(BackupPair pair, BackupConfig config, CancellationToken ct = default)
    {
        var result = await _service.RunPairAsync(pair, config, ct);
        _now = _now.AddMinutes(1);
        return result;
    }

    private static JObject Marker(string backupFolder)
        => JObject.Parse(File.ReadAllText(Path.Combine(backupFolder, BackupService.MarkerFileName)));

    [Fact]
    public async Task Backup_CopiesTheWholeTree_IntoATimestampedFolderWithMarker()
    {
        var src = _tmp.MakeSource();
        var dst = _tmp.Sub("dst");
        var pair = Pair("Docs", src, dst);

        var result = await Run(pair, Config(pair));

        Assert.Equal(BackupStatus.Success, result.Status);
        Assert.Equal(Path.Combine(dst, "Docs_2026-10-11_020000"), result.DestinationUsed);
        Assert.Equal(4, result.FilesCopied);
        Assert.True(File.Exists(Path.Combine(result.DestinationUsed, "nested", "deeper", "ékezet ü.txt")));
        Assert.True(File.Exists(Path.Combine(result.DestinationUsed, "readonly.txt")));
        Assert.Equal(pair.Id, (string?)Marker(result.DestinationUsed)["PairId"]);
        Assert.Equal("complete", (string?)Marker(result.DestinationUsed)["Status"]);
        Assert.Single(Directory.GetDirectories(dst)); // no ".partial" work folder left behind
    }

    [Fact]
    public async Task DoneLogLine_ShowsAPositiveDuration()
    {
        var pair = Pair("Docs", _tmp.MakeSource(), _tmp.Sub("dst"));

        var result = await Run(pair, Config(pair));

        Assert.True(result.Duration >= TimeSpan.Zero);
        var done = _log.Single(e => e.Message.StartsWith("✓  Done"));
        Assert.DoesNotContain("(-", done.Message);
    }

    [Fact]
    public async Task Retention_KeepsTheNewestBackupsOfThePair()
    {
        var dst = _tmp.Sub("dst");
        var pair = Pair("Docs", _tmp.MakeSource(), dst);
        var config = Config(pair);
        config.KeepCount = 2;

        for (int i = 0; i < 4; i++)
            await Run(pair, config);

        var left = Directory.GetDirectories(dst).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(new[] { "Docs_2026-10-11_020200", "Docs_2026-10-11_020300" }, left);
    }

    [Fact]
    public async Task Retention_IgnoresAnotherPairWithTheSamePrefix_AndKeepsTheNewBackup()
    {
        // Phase-1 T2c: "Docs_Old_*" sorted as newer than "Docs_*", so the fresh Docs backup was deleted.
        var src = _tmp.MakeSource();
        var dst = _tmp.Sub("dst");
        var docs = Pair("Docs", src, dst);
        var docsOld = Pair("Docs_Old", src, dst);
        var config = Config(docs, docsOld);
        config.KeepAll = true;
        for (int i = 0; i < 3; i++)
            await Run(docsOld, config);
        config.KeepAll = false;
        config.KeepCount = 1;

        var first = await Run(docs, config);
        var second = await Run(docs, config);

        Assert.Equal(BackupStatus.Success, second.Status);
        Assert.True(Directory.Exists(second.DestinationUsed));
        Assert.False(Directory.Exists(first.DestinationUsed));
        Assert.Equal(3, Directory.GetDirectories(dst, "Docs_Old_*").Length);
    }

    [Fact]
    public async Task Retention_NeverTouchesFoldersItDidNotCreate()
    {
        var dst = _tmp.Sub("dst");
        var pair = Pair("Docs", _tmp.MakeSource(), dst);
        var config = Config(pair);
        config.KeepCount = 1;
        var userFolder = _tmp.Sub(@"dst\Docs_1999_scans");                 // phase-1 T2b: was deleted
        var noMarker = _tmp.Sub(@"dst\Docs_2020-01-01_000000");             // e.g. made by an old BOH version
        var otherPair = _tmp.Sub(@"dst\Docs_2020-01-02_000000");
        File.WriteAllText(Path.Combine(otherPair, BackupService.MarkerFileName), "{\"PairId\":\"someone-else\",\"Status\":\"complete\"}");

        await Run(pair, config);
        await Run(pair, config);

        Assert.True(Directory.Exists(userFolder));
        Assert.True(Directory.Exists(noMarker));
        Assert.True(Directory.Exists(otherPair));
    }

    [Fact]
    public async Task Retention_DeletesABackupWithReadOnlyFilesCompletely()
    {
        var dst = _tmp.Sub("dst");
        var pair = Pair("Docs", _tmp.MakeSource(), dst);
        var config = Config(pair);
        config.KeepCount = 1;

        var first = await Run(pair, config);
        var second = await Run(pair, config);

        Assert.False(Directory.Exists(first.DestinationUsed));
        Assert.False(Directory.Exists(first.DestinationUsed + BackupService.DeletingSuffix));
        Assert.Empty(second.Warnings);
        Assert.Single(Directory.GetDirectories(dst));
    }

    [Fact]
    public async Task FailedBackup_ReportsTheReason_AndDeletesNothing()
    {
        var dst = _tmp.Sub("dst");
        var pair = Pair("Docs", _tmp.MakeSource(), dst);
        var config = Config(pair);
        config.KeepAll = true;
        await Run(pair, config);
        await Run(pair, config);
        config.KeepAll = false;
        config.KeepCount = 1;
        pair.SourcePath = Path.Combine(_tmp.Root, "missing");

        var failed = await Run(pair, config);

        Assert.Equal(BackupStatus.Failed, failed.Status);
        Assert.StartsWith("Source folder not found", failed.ErrorMessage);
        Assert.Equal(2, Directory.GetDirectories(dst).Length);
    }

    [Fact]
    public async Task LockedFile_IsSkipped_AndTheBackupIsMarkedIncomplete()
    {
        var src = _tmp.MakeSource();
        var dst = _tmp.Sub("dst");
        var locked = Path.Combine(src, "locked.txt");
        File.WriteAllText(locked, "in use");
        var pair = Pair("Docs", src, dst);
        BackupResult result;

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            result = await Run(pair, Config(pair));

        Assert.Equal(BackupStatus.Partial, result.Status);
        Assert.EndsWith(BackupService.IncompleteSuffix, result.DestinationUsed);
        Assert.Contains(result.SkippedFiles, s => s.Contains("locked.txt"));
        Assert.Equal(4, result.FilesCopied);
        Assert.Equal("incomplete", (string?)Marker(result.DestinationUsed)["Status"]);
        Assert.Single(Directory.GetDirectories(dst));
    }

    [Fact]
    public async Task Retention_KeepsTheNewestCompleteBackup_WhileTheLatestIsIncomplete()
    {
        var src = _tmp.MakeSource();
        var dst = _tmp.Sub("dst");
        var locked = Path.Combine(src, "locked.txt");
        File.WriteAllText(locked, "in use");
        var pair = Pair("Docs", src, dst);
        var config = Config(pair);
        config.KeepCount = 1;

        var complete = await Run(pair, config);
        BackupResult incomplete;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            incomplete = await Run(pair, config);

        Assert.Equal(BackupStatus.Partial, incomplete.Status);
        Assert.True(Directory.Exists(complete.DestinationUsed));
        Assert.True(Directory.Exists(incomplete.DestinationUsed));

        var next = await Run(pair, config);
        Assert.Equal(new[] { next.DestinationUsed }, Directory.GetDirectories(dst));
    }

    [Fact]
    public async Task Cancel_RemovesTheUnfinishedFolder()
    {
        var dst = _tmp.Sub("dst");
        var pair = Pair("Docs", _tmp.MakeSource(), dst);
        using var cts = new CancellationTokenSource();
        _service.LogMessage += e => { if (e.Message.Contains("Writing to")) cts.Cancel(); };

        var result = await Run(pair, Config(pair), cts.Token);

        Assert.Equal(BackupStatus.Cancelled, result.Status);
        Assert.Empty(Directory.GetDirectories(dst));
    }

    [Fact]
    public async Task UnreachableDestination_FailsWithoutFallback_AndUsesTheFallbackWhenEnabled()
    {
        var fallback = _tmp.Sub("fallback");
        var pair = Pair("Docs", _tmp.MakeSource(), MissingDrive());
        var config = Config(pair);

        var failed = await Run(pair, config);
        config.EnableFallback = true;
        config.FallbackPath = fallback;
        var rescued = await Run(pair, config);

        Assert.Equal(BackupStatus.Failed, failed.Status);
        Assert.StartsWith("Destination not reachable", failed.ErrorMessage);
        Assert.Equal(BackupStatus.Success, rescued.Status);
        Assert.True(rescued.UsedFallback);
        Assert.StartsWith(fallback, rescued.DestinationUsed);
    }

    [Fact]
    public async Task PairNameWithPathTraversal_IsRejected()
    {
        // Phase-1 T8: "..\escaped" wrote the backup outside the destination folder.
        var dst = _tmp.Sub(@"trav\inner");
        var pair = Pair(@"..\escaped", _tmp.MakeSource(), dst);

        var result = await Run(pair, Config(pair));

        Assert.Equal(BackupStatus.Failed, result.Status);
        Assert.Empty(Directory.GetDirectories(Path.Combine(_tmp.Root, "trav"), "escaped*"));
        Assert.Empty(Directory.GetDirectories(dst));
    }

    [Fact]
    public async Task DuplicatePairNames_OnlyTheFirstPairRuns()
    {
        // Phase-1 T9: two pairs with the same name wrote into one folder.
        var dst = _tmp.Sub("dst");
        var config = Config(Pair("Docs", _tmp.MakeSource("src1"), dst), Pair("docs ", _tmp.MakeSource("src2"), dst));

        var results = await _service.RunAllAsync(config);

        Assert.Equal(BackupStatus.Success, results[0].Status);
        Assert.Equal(BackupStatus.Failed, results[1].Status);
        Assert.Single(Directory.GetDirectories(dst));
    }

    [Fact]
    public async Task DestinationInsideTheSource_IsRejected()
    {
        var src = _tmp.MakeSource();
        var pair = Pair("Docs", src, Path.Combine(src, "backups"));

        var result = await Run(pair, Config(pair));

        Assert.Equal(BackupStatus.Failed, result.Status);
        Assert.Contains("inside the source", result.ErrorMessage);
        Assert.False(Directory.Exists(Path.Combine(src, "backups")));
    }

    [Fact]
    public async Task JunctionLoopInTheSource_IsSkippedAndLogged()
    {
        var src = _tmp.MakeSource();
        var link = Path.Combine(src, "loop");
        CreateJunction(link, src);
        Assert.True(Directory.Exists(link));
        var pair = Pair("Docs", src, _tmp.Sub("dst"));

        var result = await Run(pair, Config(pair));
        Directory.Delete(link); // removes only the junction itself

        Assert.Equal(BackupStatus.Success, result.Status);
        Assert.Equal(4, result.FilesCopied);
        Assert.False(Directory.Exists(Path.Combine(result.DestinationUsed, "loop")));
        Assert.Contains(_log, e => e.Message.Contains("Skipped link"));
    }
}
