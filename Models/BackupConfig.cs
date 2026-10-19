using System.Collections.ObjectModel;

namespace BOH.Models;

public class BackupConfig
{
    public ObservableCollection<BackupPair> Pairs { get; set; } = new();
    public int KeepCount { get; set; } = 3;
    public bool KeepAll { get; set; } = false;
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Daily;
    public int ScheduleHour { get; set; } = 2;
    public int ScheduleMinute { get; set; } = 0;
    public DayOfWeek ScheduleWeekDay { get; set; } = DayOfWeek.Monday;
    public int ScheduleMonthDay { get; set; } = 1;
    public bool RunOnStartup { get; set; } = false;
    public bool EnableFallback { get; set; } = false;
    public string FallbackPath { get; set; } = string.Empty;
    public int NetworkTimeoutSeconds { get; set; } = 10;
}

public enum ScheduleType { Daily, Weekly, Monthly }