using BOH.Models;
using BOH.Services;
using Microsoft.Win32.TaskScheduler;

namespace BOH.Tests;

/// <summary>Task definitions are only built in memory; nothing is registered in the Task Scheduler.</summary>
public sealed class TaskSchedulerServiceTests : IDisposable
{
    private const string Exe = @"D:\Tools\BOH\BOH.exe";
    private static readonly DateTime Noon = new(2026, 10, 11, 12, 0, 0);
    private readonly TaskService _ts = new();

    public void Dispose() => _ts.Dispose();

    [Fact]
    public void WeekdayMapping_IsCorrectForAllSevenDays()
    {
        // Phase-1 C-5: the plain cast turned Monday into Sunday and Sunday into "no day".
        var expected = new[]
        {
            DaysOfTheWeek.Sunday, DaysOfTheWeek.Monday, DaysOfTheWeek.Tuesday, DaysOfTheWeek.Wednesday,
            DaysOfTheWeek.Thursday, DaysOfTheWeek.Friday, DaysOfTheWeek.Saturday,
        };
        for (int day = 0; day < 7; day++)
            Assert.Equal(expected[day], TaskSchedulerService.ToDaysOfTheWeek((DayOfWeek)day));
    }

    [Fact]
    public void ScheduledTask_RunsUnelevated_AlsoOnBattery_AndStartsBohExe()
    {
        var td = _ts.NewTask();

        TaskSchedulerService.BuildScheduledTask(td, new BackupConfig(), Exe, Noon);

        Assert.Equal(TaskRunLevel.LUA, td.Principal.RunLevel);
        Assert.False(td.Settings.DisallowStartIfOnBatteries);
        Assert.False(td.Settings.StopIfGoingOnBatteries);
        Assert.True(td.Settings.StartWhenAvailable);
        Assert.Equal(TaskInstancesPolicy.IgnoreNew, td.Settings.MultipleInstances);
        var action = Assert.IsType<ExecAction>(Assert.Single(td.Actions));
        Assert.Equal(Exe, action.Path);
        Assert.Equal("--run-backup", action.Arguments);
        Assert.Equal(@"D:\Tools\BOH", action.WorkingDirectory);
        var daily = Assert.IsType<DailyTrigger>(Assert.Single(td.Triggers));
        Assert.Equal(new DateTime(2026, 10, 12, 2, 0, 0), daily.StartBoundary); // 02:00 already passed today
    }

    [Fact]
    public void WeeklyTask_UsesTheSelectedWeekday()
    {
        var td = _ts.NewTask();
        var config = new BackupConfig
        {
            ScheduleType = ScheduleType.Weekly,
            ScheduleWeekDay = DayOfWeek.Monday,
            ScheduleHour = 21,
            ScheduleMinute = 30,
        };

        TaskSchedulerService.BuildScheduledTask(td, config, Exe, Noon);

        var weekly = Assert.IsType<WeeklyTrigger>(Assert.Single(td.Triggers));
        Assert.Equal(DaysOfTheWeek.Monday, weekly.DaysOfWeek);
        Assert.Equal(new DateTime(2026, 10, 11, 21, 30, 0), weekly.StartBoundary);
    }

    [Fact]
    public void SignInTask_UsesALogonTriggerForTheUser_InsteadOfABootTrigger()
    {
        var td = _ts.NewTask();

        TaskSchedulerService.BuildSignInTask(td, Exe, @"PC\demo");

        var logon = Assert.IsType<LogonTrigger>(Assert.Single(td.Triggers));
        Assert.Equal(@"PC\demo", logon.UserId);
        Assert.Equal(TimeSpan.FromMinutes(2), logon.Delay);
        Assert.Equal(TaskRunLevel.LUA, td.Principal.RunLevel);
    }

    [Fact]
    public void ExecutablePath_MustBeBohExe_NotTheDotnetHost()
    {
        Assert.Equal(Exe, TaskSchedulerService.GetExecutablePath(Exe));
        Assert.Throws<InvalidOperationException>(() => TaskSchedulerService.GetExecutablePath(@"C:\Program Files\dotnet\dotnet.exe"));
        Assert.Throws<InvalidOperationException>(() => TaskSchedulerService.GetExecutablePath(null));
    }
}
