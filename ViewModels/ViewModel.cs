using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using BOH.Models;
using BOH.Services;

namespace BOH.ViewModels;

/// <summary>A weekday in the schedule picker; the name comes from the current UI culture.</summary>
public sealed record DayOption(DayOfWeek Day, string Name)
{
    public override string ToString() => Name;
}

public class MainViewModel : INotifyPropertyChanged
{
    public const int MaxLogEntries = 1000;

    private readonly ConfigService _store;
    private readonly BackupService _backupService = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private CancellationTokenSource? _cts;

    public MainViewModel() : this(ConfigService.CreateDefault(), useTaskScheduler: true) { }

    /// <param name="useTaskScheduler">False for tests and screenshots: the Task Scheduler is not queried.</param>
    public MainViewModel(ConfigService store, bool useTaskScheduler)
    {
        _store = store;
        Config = store.Load();
        WeekDays = BuildWeekDays(CultureInfo.CurrentUICulture);
        Pairs.CollectionChanged += OnPairsChanged;
        foreach (var pair in Pairs)
            pair.PropertyChanged += OnPairChanged;
        _backupService.LogMessage += AddLog;

        AddPairCommand = new RelayCommand(_ => Pairs.Add(new BackupPair { Name = PairNames.NextDefaultName(Pairs.Select(p => p.Name)) }));
        RemovePairCommand = new RelayCommand(p => { if (p is BackupPair pair) Pairs.Remove(pair); });
        BrowseSourceCommand = new RelayCommand(p => BrowseFolder(p as BackupPair, isSource: true));
        BrowseDestCommand = new RelayCommand(p => BrowseFolder(p as BackupPair, isSource: false));
        BrowseFallbackCommand = new RelayCommand(_ => BrowseFallback());
        RunNowCommand = new RelayCommand(_ => _ = RunNowAsync(), _ => !IsRunning);
        CancelRunCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsRunning);
        SaveConfigCommand = new RelayCommand(_ => TrySave());
        RegisterTaskCommand = new RelayCommand(_ => RegisterTask(), _ => !IsRunning);
        RemoveTaskCommand = new RelayCommand(_ => RemoveTask(), _ => TaskExists);
        ClearLogCommand = new RelayCommand(_ => LogEntries.Clear());

        if (useTaskScheduler)
            RefreshTaskStatus();
        if (store.LoadWarning != null)
            AddLog(store.LoadWarning, LogLevel.Warning);
    }

    public BackupConfig Config { get; }
    public ObservableCollection<BackupPair> Pairs => Config.Pairs;
    /// <summary>Oldest first, capped at <see cref="MaxLogEntries"/>.</summary>
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public IReadOnlyList<DayOption> WeekDays { get; }
    public bool IsAdmin => TaskSchedulerService.IsAdmin;

    // ── State ──────────────────────────────────────────────────────────────

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            _isRunning = value;
            OnPropertyChanged();
            // Re-evaluate "Back up now" / "Stop" at once instead of on the next mouse or key event.
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool _isDirty;
    /// <summary>Settings changed since the last load or save.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set { if (_isDirty != value) { _isDirty = value; OnPropertyChanged(); } }
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        internal set { _statusText = value; OnPropertyChanged(); }
    }

    private bool _taskExists;
    public bool TaskExists
    {
        get => _taskExists;
        private set { _taskExists = value; OnPropertyChanged(); }
    }

    private string _nextRunText = "—";
    public string NextRunText
    {
        get => _nextRunText;
        private set { _nextRunText = value; OnPropertyChanged(); }
    }

    // ── Settings (every change marks the window as having unsaved changes) ─────

    public bool KeepAll
    {
        get => Config.KeepAll;
        set { if (Config.KeepAll != value) { Config.KeepAll = value; Changed(); OnPropertyChanged(nameof(KeepCountEnabled)); } }
    }
    public bool KeepCountEnabled => !Config.KeepAll;

    public int KeepCount
    {
        get => Config.KeepCount;
        set => SetConfig(Config.KeepCount, Math.Max(1, value), v => Config.KeepCount = v);
    }

    public ScheduleType ScheduleType
    {
        get => Config.ScheduleType;
        set
        {
            if (Config.ScheduleType == value) return;
            Config.ScheduleType = value;
            Changed();
            OnPropertyChanged(nameof(ShowWeekDay));
            OnPropertyChanged(nameof(ShowMonthDay));
        }
    }
    public bool ShowWeekDay => Config.ScheduleType == ScheduleType.Weekly;
    public bool ShowMonthDay => Config.ScheduleType == ScheduleType.Monthly;

    public int ScheduleHour
    {
        get => Config.ScheduleHour;
        set => SetConfig(Config.ScheduleHour, Math.Clamp(value, 0, 23), v => Config.ScheduleHour = v);
    }
    public int ScheduleMinute
    {
        get => Config.ScheduleMinute;
        set => SetConfig(Config.ScheduleMinute, Math.Clamp(value, 0, 59), v => Config.ScheduleMinute = v);
    }
    public DayOfWeek ScheduleWeekDay
    {
        get => Config.ScheduleWeekDay;
        set => SetConfig(Config.ScheduleWeekDay, value, v => Config.ScheduleWeekDay = v);
    }
    public int ScheduleMonthDay
    {
        get => Config.ScheduleMonthDay;
        set => SetConfig(Config.ScheduleMonthDay, Math.Clamp(value, 1, 28), v => Config.ScheduleMonthDay = v);
    }
    public bool RunOnStartup
    {
        get => Config.RunOnStartup;
        set => SetConfig(Config.RunOnStartup, value, v => Config.RunOnStartup = v);
    }

    public bool EnableFallback
    {
        get => Config.EnableFallback;
        set => SetConfig(Config.EnableFallback, value, v => Config.EnableFallback = v);
    }
    public string FallbackPath
    {
        get => Config.FallbackPath;
        set => SetConfig(Config.FallbackPath, value, v => Config.FallbackPath = v);
    }
    public int NetworkTimeoutSeconds
    {
        get => Config.NetworkTimeoutSeconds;
        set => SetConfig(Config.NetworkTimeoutSeconds, Math.Clamp(value, 1, 60), v => Config.NetworkTimeoutSeconds = v);
    }

    // ── Commands ───────────────────────────────────────────────────────────

    public ICommand AddPairCommand { get; }
    public ICommand RemovePairCommand { get; }
    public ICommand BrowseSourceCommand { get; }
    public ICommand BrowseDestCommand { get; }
    public ICommand BrowseFallbackCommand { get; }
    public ICommand RunNowCommand { get; }
    public ICommand CancelRunCommand { get; }
    public ICommand SaveConfigCommand { get; }
    public ICommand RegisterTaskCommand { get; }
    public ICommand RemoveTaskCommand { get; }
    public ICommand ClearLogCommand { get; }

    /// <summary>Validates the pair names and saves; on problems the log says why and nothing is written.</summary>
    public bool TrySave()
    {
        var errors = PairNames.ValidateAll(Pairs);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
                AddLog(error, LogLevel.Error);
            StatusText = "Settings not saved: fix the pair names (see the log)";
            return false;
        }
        try
        {
            _store.Save(Config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog($"Settings could not be saved to {_store.ConfigPath}: {ex.Message}", LogLevel.Error);
            StatusText = "Settings not saved";
            return false;
        }
        IsDirty = false;
        StatusText = $"Settings saved ({Now("HH:mm:ss")})";
        AddLog($"Settings saved to {_store.ConfigPath}", LogLevel.Success);
        return true;
    }

    /// <summary>Adds a log line from any thread; the oldest lines are dropped beyond <see cref="MaxLogEntries"/>.</summary>
    public void AddLog(LogEntry entry)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => AddLog(entry));
            return;
        }
        LogEntries.Add(entry);
        while (LogEntries.Count > MaxLogEntries)
            LogEntries.RemoveAt(0);
    }

    private void AddLog(string message, LogLevel level = LogLevel.Info)
        => AddLog(new LogEntry { Message = message, Level = level });

    public static IReadOnlyList<DayOption> BuildWeekDays(CultureInfo culture)
    {
        var format = culture.DateTimeFormat;
        return Enumerable.Range(0, 7)
            .Select(i => (DayOfWeek)(((int)format.FirstDayOfWeek + i) % 7))
            .Select(day => new DayOption(day, culture.TextInfo.ToTitleCase(format.GetDayName(day))))
            .ToList();
    }

    public static string Summarize(IReadOnlyCollection<BackupResult> results)
    {
        int complete = results.Count(r => r.Status == BackupStatus.Success);
        int incomplete = results.Count(r => r.Status == BackupStatus.Partial);
        int failed = results.Count - complete - incomplete;
        var parts = new List<string> { $"{complete} complete" };
        if (incomplete > 0) parts.Add($"{incomplete} incomplete");
        if (failed > 0) parts.Add($"{failed} failed");
        return $"Finished {Now("HH:mm")}: {string.Join(", ", parts)}";
    }

    private async Task RunNowAsync()
    {
        if (!Pairs.Any(p => p.Enabled))
        {
            StatusText = "Nothing to back up: add a pair or enable one";
            return;
        }
        IsRunning = true;
        StatusText = "Backing up…";
        _cts = new CancellationTokenSource();
        try
        {
            StatusText = Summarize(await _backupService.RunAllAsync(Config, _cts.Token));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Stopped";
        }
        catch (Exception ex)
        {
            StatusText = "Backup stopped by an error (see the log)";
            AddLog($"Unexpected error: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }

    private void RegisterTask()
    {
        if (!TrySave())
            return;
        try
        {
            TaskSchedulerService.CreateOrUpdate(Config);
            AddLog("Windows scheduled task saved.", LogLevel.Success);
        }
        catch (Exception ex)
        {
            AddLog($"Could not save the scheduled task: {ex.Message}", LogLevel.Error);
        }
        RefreshTaskStatus();
    }

    private void RemoveTask()
    {
        try
        {
            TaskSchedulerService.RemoveAll();
            AddLog("Scheduled tasks removed.", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            AddLog($"Could not remove the scheduled tasks: {ex.Message}", LogLevel.Error);
        }
        RefreshTaskStatus();
    }

    private void RefreshTaskStatus()
    {
        try
        {
            var (mainExists, _, nextRun) = TaskSchedulerService.GetStatus();
            TaskExists = mainExists;
            NextRunText = nextRun is { } next && next > DateTime.MinValue
                ? next.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture)
                : "—";
        }
        catch (Exception ex)
        {
            TaskExists = false;
            NextRunText = "unknown";
            AddLog($"Task Scheduler is not available: {ex.Message}", LogLevel.Warning);
        }
    }

    private void BrowseFolder(BackupPair? pair, bool isSource)
    {
        if (pair == null)
            return;
        var picked = PickFolder(isSource ? "Select the source folder" : "Select the destination folder",
                                isSource ? pair.SourcePath : pair.DestinationPath);
        if (picked == null)
            return;
        if (isSource)
            pair.SourcePath = picked;
        else
            pair.DestinationPath = picked;
    }

    private void BrowseFallback()
    {
        var picked = PickFolder("Select the fallback folder", FallbackPath);
        if (picked != null)
            FallbackPath = picked;
    }

    private static string? PickFolder(string title, string? current)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            dialog.InitialDirectory = current;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void OnPairsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (BackupPair pair in e.OldItems)
                pair.PropertyChanged -= OnPairChanged;
        if (e.NewItems != null)
            foreach (BackupPair pair in e.NewItems)
                pair.PropertyChanged += OnPairChanged;
        IsDirty = true;
    }

    private void OnPairChanged(object? sender, PropertyChangedEventArgs e) => IsDirty = true;

    private void SetConfig<T>(T current, T value, Action<T> apply, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(current, value))
        {
            apply(value);
            IsDirty = true;
        }
        // Raised even when unchanged, so a clamped value (e.g. 0 → 1) is shown again in the text box.
        OnPropertyChanged(name);
    }

    private void Changed([CallerMemberName] string? name = null)
    {
        IsDirty = true;
        OnPropertyChanged(name);
    }

    private static string Now(string format) => DateTime.Now.ToString(format, CultureInfo.InvariantCulture);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
