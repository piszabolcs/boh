using BOH.Services;

namespace BOH;

public static class Program
{
    public const string RunBackupArgument = "--run-backup";

    [STAThread]
    public static int Main(string[] args)
    {
        // Scheduled runs never create the WPF Application or a window.
        if (args.Any(a => string.Equals(a, RunBackupArgument, StringComparison.OrdinalIgnoreCase)))
            return SilentRunner.Run(ConfigService.CreateDefault());

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
