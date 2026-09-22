using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace steam_game_tool;

public static class ScanInventory
{
    public static void ValidatePersistent(ScanResult result)
    {
        var roots = string.Join("\n", result.Roots.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)).ToUpperInvariant();
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(roots)));
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamGameTool", "scan-baselines");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, key + ".json");
        var current = new Baseline(result.Games.Select(g => g.InstallDir).ToArray(),
            SteamScanner.Filter(result, Marker.Any).Select(g => g.InstallDir).ToArray());
        if (File.Exists(path))
        {
            var previous = System.Text.Json.JsonSerializer.Deserialize<Baseline>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Invalid scan baseline.");
            Compare(previous.Scanned, current.Scanned);
            ValidateUnityRetention(previous.Unity, current.Unity);
        }
        File.WriteAllText(path + ".tmp", System.Text.Json.JsonSerializer.Serialize(current));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public sealed record Baseline(string[] Scanned, string[] Unity);

    public static void ValidateUnityRetention(IEnumerable<string> previousUnity, IEnumerable<string> currentUnity,
        Func<string, bool>? directoryExists = null)
    {
        directoryExists ??= Directory.Exists;
        var current = currentUnity.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lost = previousUnity.Where(p => !current.Contains(p) && directoryExists(p)).ToArray();
        if (lost.Length > 0)
            throw new InvalidOperationException("Unity classification regression for installed entries: " + string.Join(", ", lost));
    }
    // Compare only scans of the same roots. A still-existing folder disappearing is a regression.
    // Actual directory additions/removals are reported, not treated as classification changes.
    public static string Compare(IEnumerable<string> previous, IEnumerable<string> current,
        Func<string, bool>? directoryExists = null)
    {
        directoryExists ??= Directory.Exists;
        var before = previous.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = current.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = after.Except(before).ToArray();
        var removed = before.Except(after).ToArray();
        var lost = removed.Where(directoryExists).ToArray();
        if (lost.Length > 0)
            throw new InvalidOperationException("Existing installation folders disappeared from scan: " + string.Join(", ", lost));
        return $"Previous: {before.Count}; current: {after.Count}; added: {added.Length}; removed on disk: {removed.Length}\r\n" +
            string.Join("\r\n", added.Select(p => "+ " + p).Concat(removed.Select(p => "- " + p)));
    }
}
