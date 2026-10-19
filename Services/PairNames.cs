using System.IO;
using BOH.Models;

namespace BOH.Services;

/// <summary>Rules for backup pair names, which become the prefix of every backup folder name.</summary>
public static class PairNames
{
    public const int MaxLength = 64;
    public const string DefaultPrefix = "Backup";

    /// <summary>Returns null when the name is usable as a folder name prefix, otherwise the reason.</summary>
    public static string? Validate(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return "The pair name is empty.";
        if (trimmed.Length > MaxLength)
            return $"The pair name \"{trimmed}\" is longer than {MaxLength} characters.";
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return $"The pair name \"{trimmed}\" contains a character that is not allowed in folder names (\\ / : * ? \" < > |).";
        if (trimmed.EndsWith('.'))
            return $"The pair name \"{trimmed}\" must not end with a dot.";
        return null;
    }

    /// <summary>All problems of a pair list: invalid names plus duplicates (names are compared case-insensitively).</summary>
    public static List<string> ValidateAll(IEnumerable<BackupPair> pairs)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            var error = Validate(pair.Name);
            if (error != null)
                errors.Add(error);
            else if (!seen.Add(pair.Name.Trim()))
                errors.Add($"The pair name \"{pair.Name.Trim()}\" is used more than once; each pair needs its own name.");
        }
        return errors;
    }

    /// <summary>The first free "Backup N" name.</summary>
    public static string NextDefaultName(IEnumerable<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            var candidate = $"{DefaultPrefix} {i}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }
}
