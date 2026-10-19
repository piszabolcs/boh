using BOH.Services;

namespace BOH.Tests;

public sealed class ConfigServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void SettingsLiveNextToTheExe_OnlyWhenAConfigIsAlreadyThere()
    {
        // Phase-1 C-6: the single-file exe read and wrote its config in the %TEMP% extraction folder.
        var exeFolder = _tmp.Sub("app");
        var appData = _tmp.Sub("appdata");

        Assert.Equal(Path.Combine(appData, "BOH"), ConfigService.ResolveFolder(exeFolder, appData));
        File.WriteAllText(Path.Combine(exeFolder, ConfigService.ConfigFileName), "{}");
        Assert.Equal(exeFolder, ConfigService.ResolveFolder(exeFolder, appData));
    }

    [Fact]
    public void DamagedSettings_AreKeptAside_AndDefaultsAreUsed()
    {
        var store = new ConfigService(_tmp.Sub("settings"));
        File.WriteAllText(store.ConfigPath, "{ \"Pairs\": [ broken");

        var config = store.Load();

        Assert.Empty(config.Pairs);
        Assert.NotNull(store.LoadWarning);
        var copy = Assert.Single(Directory.GetFiles(store.Folder, ConfigService.ConfigFileName + ".bad-*"));
        Assert.Equal("{ \"Pairs\": [ broken", File.ReadAllText(copy));
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips_WithoutLeavingATempFile()
    {
        var store = new ConfigService(Path.Combine(_tmp.Root, "new-folder"));
        var config = TempDir.Config(TempDir.Pair("Docs", @"C:\Data", @"E:\Backups"));
        config.ScheduleWeekDay = DayOfWeek.Friday;

        store.Save(config);
        var loaded = store.Load();

        Assert.Null(store.LoadWarning);
        Assert.Equal("Docs", Assert.Single(loaded.Pairs).Name);
        Assert.Equal(DayOfWeek.Friday, loaded.ScheduleWeekDay);
        Assert.Equal(new[] { store.ConfigPath }, Directory.GetFiles(store.Folder));
    }
}
