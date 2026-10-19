using System.Globalization;
using System.IO;
using BOH.Models;
using Newtonsoft.Json;

namespace BOH.Services;

/// <summary>
/// Loads and saves boh_config.json. Location: next to BOH.exe when a boh_config.json is already there
/// (portable mode), otherwise %APPDATA%\BOH. The log of scheduled runs (boh_log.txt) goes to the same folder.
/// </summary>
public sealed class ConfigService
{
    public const string ConfigFileName = "boh_config.json";
    public const string LogFileName = "boh_log.txt";

    public ConfigService(string folder) => Folder = folder;

    public string Folder { get; }
    public string ConfigPath => Path.Combine(Folder, ConfigFileName);
    public string LogPath => Path.Combine(Folder, LogFileName);

    /// <summary>Set by <see cref="Load"/> when the file could not be used and the defaults were returned.</summary>
    public string? LoadWarning { get; private set; }

    public static ConfigService CreateDefault()
        => new(ResolveFolder(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));

    public static string ResolveFolder(string appFolder, string appDataFolder)
        => File.Exists(Path.Combine(appFolder, ConfigFileName)) ? appFolder : Path.Combine(appDataFolder, "BOH");

    public BackupConfig Load()
    {
        LoadWarning = null;
        if (!File.Exists(ConfigPath))
            return new BackupConfig();
        try
        {
            return JsonConvert.DeserializeObject<BackupConfig>(File.ReadAllText(ConfigPath)) ?? new BackupConfig();
        }
        catch (JsonException ex)
        {
            LoadWarning = $"The settings file {ConfigPath} is damaged ({ex.Message}). {KeepCopy()} Default settings are used.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadWarning = $"The settings file {ConfigPath} could not be read ({ex.Message}). Default settings are used.";
        }
        return new BackupConfig();
    }

    /// <summary>Writes a temporary file first and then replaces the settings file in one step.</summary>
    public void Save(BackupConfig config)
    {
        Directory.CreateDirectory(Folder);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(config, Formatting.Indented));
        File.Move(temp, ConfigPath, overwrite: true);
    }

    private string KeepCopy()
    {
        var copy = $"{ConfigPath}.bad-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        try
        {
            File.Copy(ConfigPath, copy, overwrite: true);
            return $"The original is kept as {Path.GetFileName(copy)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"A copy could not be kept ({ex.Message}).";
        }
    }
}
