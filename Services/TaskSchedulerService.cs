using System.IO;
using System.Security.Principal;
using BOH.Models;
using Microsoft.Win32.TaskScheduler;

namespace BOH.Services;

/// <summary>
/// The Windows Task Scheduler entries that start "BOH.exe --run-backup". The task definitions are built by
/// pure methods (unit-tested) and only <see cref="CreateOrUpdate"/> / <see cref="RemoveAll"/> touch the system.
/// </summary>
public static class TaskSchedulerService
{
    public const string TaskName = "BOH_AutoBackup";
    /// <summary>Name kept from older versions, so registering again replaces their boot-time task.</summary>
    public const string SignInTaskName = "BOH_Startup";
    public static readonly TimeSpan SignInDelay = TimeSpan.FromMinutes(2);

    public static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>System.DayOfWeek counts from Sunday = 0; the Task Scheduler uses bit flags (Sunday = 1, Monday = 2, ...).</summary>
    public static DaysOfTheWeek ToDaysOfTheWeek(DayOfWeek day) => (DaysOfTheWeek)(1 << (int)day);

    /// <summary>The task must start BOH.exe itself; under "dotnet run" or "dotnet BOH.dll" the process is dotnet.exe.</summary>
    public static string GetExecutablePath(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath)
            || !string.Equals(Path.GetFileName(processPath), "BOH.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Scheduling needs BOH.exe, but this instance runs as \"{Path.GetFileName(processPath)}\". Start BOH.exe directly.");
        return processPath;
    }

    public static DateTime NextOccurrence(DateTime now, int hour, int minute)
    {
        var candidate = now.Date.AddHours(hour).AddMinutes(minute);
        return candidate <= now ? candidate.AddDays(1) : candidate;
    }

    /// <summary>The daily / weekly / monthly backup task.</summary>
    public static void BuildScheduledTask(TaskDefinition td, BackupConfig config, string exePath, DateTime now)
    {
        ApplyCommonSettings(td, "Backup O Horse: scheduled backup of all enabled pairs", exePath);
        var start = NextOccurrence(now, config.ScheduleHour, config.ScheduleMinute);
        Trigger trigger = config.ScheduleType switch
        {
            ScheduleType.Weekly => new WeeklyTrigger(ToDaysOfTheWeek(config.ScheduleWeekDay)) { StartBoundary = start },
            ScheduleType.Monthly => new MonthlyTrigger(config.ScheduleMonthDay) { StartBoundary = start },
            _ => new DailyTrigger { StartBoundary = start },
        };
        td.Triggers.Add(trigger);
    }

    /// <summary>Optional extra run shortly after the user signs in (replaces the old boot trigger, which needed
    /// a stored password to run before sign-in and therefore never ran).</summary>
    public static void BuildSignInTask(TaskDefinition td, string exePath, string userId)
    {
        ApplyCommonSettings(td, "Backup O Horse: backup after signing in", exePath);
        td.Triggers.Add(new LogonTrigger { UserId = userId, Delay = SignInDelay });
    }

    private static void ApplyCommonSettings(TaskDefinition td, string description, string exePath)
    {
        td.RegistrationInfo.Description = description;
        // Backing up the user's own folders needs no elevation; an elevated task that starts a user-writable exe
        // would be a privilege-escalation path.
        td.Principal.RunLevel = TaskRunLevel.LUA;
        td.Principal.LogonType = TaskLogonType.InteractiveToken;
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.StartWhenAvailable = true;
        td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
        td.Settings.ExecutionTimeLimit = TimeSpan.FromHours(4);
        td.Actions.Add(new ExecAction(exePath, Program.RunBackupArgument, Path.GetDirectoryName(exePath)));
    }

    public static void CreateOrUpdate(BackupConfig config)
    {
        var exePath = GetExecutablePath(Environment.ProcessPath);
        using var ts = new TaskService();
        var td = ts.NewTask();
        BuildScheduledTask(td, config, exePath, DateTime.Now);
        ts.RootFolder.RegisterTaskDefinition(TaskName, td);

        if (config.RunOnStartup)
        {
            var signIn = ts.NewTask();
            BuildSignInTask(signIn, exePath, WindowsIdentity.GetCurrent().Name);
            ts.RootFolder.RegisterTaskDefinition(SignInTaskName, signIn);
        }
        else
        {
            ts.RootFolder.DeleteTask(SignInTaskName, exceptionOnNotExists: false);
        }
    }

    public static void RemoveAll()
    {
        using var ts = new TaskService();
        ts.RootFolder.DeleteTask(TaskName, exceptionOnNotExists: false);
        ts.RootFolder.DeleteTask(SignInTaskName, exceptionOnNotExists: false);
    }

    /// <summary>Looks up only BOH's own task by name (no recursive search of every task folder).</summary>
    public static (bool mainExists, bool signInExists, DateTime? nextRun) GetStatus()
    {
        using var ts = new TaskService();
        using var main = ts.GetTask(TaskName);
        using var signIn = ts.GetTask(SignInTaskName);
        return (main != null, signIn != null, main?.NextRunTime);
    }
}
