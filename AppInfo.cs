using System.Reflection;
using System.Windows;

namespace BOH;

/// <summary>Name and version for the footer signature and the About card. The version is read from the assembly,
/// which gets it from &lt;Version&gt; in BOH.csproj (the only place it is written).</summary>
public static class AppInfo
{
    public const string Name = "Backup O Horse";

    public static string Version { get; } = ReadVersion();

    public static string Signature => $"psz // v{Version}";

    /// <summary>Fade time of the signature glow; no animation when Windows animations are switched off.</summary>
    public static Duration GlowDuration =>
        new(SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero);

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = informational.IndexOf('+'); // "0.3.0+<commit id>" -> "0.3.0"
        return plus < 0 ? informational : informational[..plus];
    }
}
