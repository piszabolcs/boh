using System.Diagnostics;
using BOH.Models;

namespace BOH.Tests;

/// <summary>A throw-away folder under %TEMP%\boh-tests plus the one fixture generator all tests share.</summary>
public sealed class TempDir : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "boh-tests", Guid.NewGuid().ToString("N")[..8]);

    public TempDir() => Directory.CreateDirectory(Root);

    public string Sub(string relative)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>4 files: a.txt, nested\b.txt, nested\deeper\ékezet ü.txt and a read-only readonly.txt.</summary>
    public string MakeSource(string name = "src")
    {
        var src = Sub(name);
        Directory.CreateDirectory(Path.Combine(src, "nested", "deeper"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(src, "nested", "b.txt"), "bravo");
        File.WriteAllText(Path.Combine(src, "nested", "deeper", "ékezet ü.txt"), "unicode name");
        var readOnly = Path.Combine(src, "readonly.txt");
        File.WriteAllText(readOnly, "read-only");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        return src;
    }

    public static BackupPair Pair(string name, string source, string destination)
        => new() { Name = name, SourcePath = source, DestinationPath = destination };

    public static BackupConfig Config(params BackupPair[] pairs)
    {
        var config = new BackupConfig { KeepCount = 3, NetworkTimeoutSeconds = 1 };
        foreach (var pair in pairs)
            config.Pairs.Add(pair);
        return config;
    }

    /// <summary>A drive letter that does not exist on this machine.</summary>
    public static string MissingDrive()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        return "QRSTUVWXYZ".First(c => !used.Contains(c) && !Directory.Exists($"{c}:\\")) + @":\Backups";
    }

    /// <summary>Directory junction (no admin rights needed, unlike symbolic links).</summary>
    public static void CreateJunction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
    }

    public void Dispose()
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(Root, "*", options))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup of a temp folder; a leftover does not affect other tests
        }
    }
}
