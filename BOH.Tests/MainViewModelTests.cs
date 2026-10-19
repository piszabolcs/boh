using System.Globalization;
using BOH.Models;
using BOH.Services;
using BOH.ViewModels;

namespace BOH.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private MainViewModel NewViewModel(out ConfigService store)
    {
        store = new ConfigService(_tmp.Sub("settings"));
        return new MainViewModel(store, useTaskScheduler: false);
    }

    [Fact]
    public void UnsavedChanges_AreTrackedUntilSaved()
    {
        var vm = NewViewModel(out _);
        Assert.False(vm.IsDirty);

        vm.AddPairCommand.Execute(null);
        Assert.True(vm.IsDirty);
        Assert.True(vm.TrySave());
        Assert.False(vm.IsDirty);

        vm.Pairs[0].SourcePath = @"D:\Data";
        Assert.True(vm.IsDirty);
        Assert.True(vm.TrySave());
        vm.KeepCount = vm.KeepCount; // writing the same value is not a change
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void AddPair_GivesEveryNewPairAFreeDefaultName()
    {
        var vm = NewViewModel(out _);
        for (int i = 0; i < 3; i++)
            vm.AddPairCommand.Execute(null);

        vm.RemovePairCommand.Execute(vm.Pairs[0]);
        vm.AddPairCommand.Execute(null);

        Assert.Equal(new[] { "Backup 2", "Backup 3", "Backup 1" }, vm.Pairs.Select(p => p.Name));
    }

    [Fact]
    public void Save_IsRefusedWhilePairNamesClash()
    {
        var vm = NewViewModel(out var store);
        vm.Pairs.Add(new BackupPair { Name = "Docs" });
        vm.Pairs.Add(new BackupPair { Name = "docs" });

        Assert.False(vm.TrySave());
        Assert.False(File.Exists(store.ConfigPath));
        Assert.True(vm.IsDirty);
        Assert.Contains(vm.LogEntries, e => e.Level == LogLevel.Error && e.Message.Contains("more than once"));
    }

    [Fact]
    public void WeekdayNames_AndFirstDay_FollowTheCulture()
    {
        var hungarian = MainViewModel.BuildWeekDays(new CultureInfo("hu-HU"));
        var american = MainViewModel.BuildWeekDays(new CultureInfo("en-US"));

        Assert.Equal(new DayOption(DayOfWeek.Monday, "Hétfő"), hungarian[0]);
        Assert.Equal(new DayOption(DayOfWeek.Sunday, "Sunday"), american[0]);
        Assert.Equal(7, hungarian.Select(d => d.Day).Distinct().Count());
    }

    [Fact]
    public void Log_IsOldestFirst_AndCapped()
    {
        var vm = NewViewModel(out _);

        for (int i = 0; i < MainViewModel.MaxLogEntries + 5; i++)
            vm.AddLog(new LogEntry { Message = $"line {i}" });

        Assert.Equal(MainViewModel.MaxLogEntries, vm.LogEntries.Count);
        Assert.Equal("line 5", vm.LogEntries[0].Message);
        Assert.Equal($"line {MainViewModel.MaxLogEntries + 4}", vm.LogEntries[^1].Message);
    }
}
