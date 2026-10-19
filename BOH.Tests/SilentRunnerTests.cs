using System.Windows.Threading;
using BOH.Services;

namespace BOH.Tests;

public sealed class SilentRunnerTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private ConfigService StoreWith(params BOH.Models.BackupPair[] pairs)
    {
        var store = new ConfigService(_tmp.Sub("settings"));
        store.Save(TempDir.Config(pairs));
        return store;
    }

    [Fact]
    public void RunBackup_OnAUiThread_FinishesWithExitCode0()
    {
        // Phase-1 C-1: "--run-backup" blocked the WPF dispatcher thread and hung forever.
        var dst = _tmp.Sub("dst");
        var store = StoreWith(TempDir.Pair("Docs", _tmp.MakeSource(), dst));
        int? exitCode = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            exitCode = SilentRunner.Run(store);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the silent run did not finish");
        Assert.Equal(SilentRunner.ExitSuccess, exitCode);
        Assert.Single(Directory.GetDirectories(dst));
        Assert.Contains("Finished: 1/1", File.ReadAllText(store.LogPath));
    }

    [Fact]
    public void RunBackup_WithAFailingPair_ReturnsExitCode1()
    {
        var store = StoreWith(TempDir.Pair("Docs", Path.Combine(_tmp.Root, "missing"), _tmp.Sub("dst")));

        Assert.Equal(SilentRunner.ExitBackupProblem, SilentRunner.Run(store));
        Assert.Contains("Source folder not found", File.ReadAllText(store.LogPath));
    }

    [Fact]
    public void RunBackup_WithoutEnabledPairs_ReturnsExitCode2_AndSaysWhy()
    {
        var store = StoreWith();

        Assert.Equal(SilentRunner.ExitConfigProblem, SilentRunner.Run(store));
        Assert.Contains("No enabled backup pair", File.ReadAllText(store.LogPath));
    }
}
