using System;
using System.Linq;

namespace steam_game_tool;

public sealed record PlayerSelection(BackendEvidence? Player, string Reason, string Confidence);

public static class GameClassification
{
    private static string Key(string value) => System.Text.RegularExpressions.Regex.Replace(
        GameNames.Normalize(value), @"[^\p{L}\p{Nd}]", "").ToUpperInvariant();

    public static PlayerSelection Select(SteamGame game)
    {
        var candidates = game.BackendEvidence.Where(e => e.DataPath is not null && e.Backend != UnityBackend.Unknown).ToArray();
        if (candidates.Length == 0) candidates = game.BackendEvidence.Where(e => e.Backend != UnityBackend.Unknown).ToArray();
        if (candidates.Length == 0) return new(null, "no-backend-evidence", "low");
        if (candidates.Length == 1) return new(candidates[0], "single-player", candidates[0].IsBundled ? "low" : "high");
        var installName = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(game.InstallDir));
        string DataName(BackendEvidence e) => e.DataPath is null ? "" : System.IO.Path.GetFileName(e.DataPath)[..^5];
        PlayerSelection? Narrow(Func<BackendEvidence, bool> predicate, string reason)
        {
            var matching = candidates.Where(predicate).ToArray();
            if (matching.Length == 0) return null;
            candidates = matching;
            return matching.Length == 1 ? new(matching[0], reason, matching[0].IsBundled ? "low" : "medium") : null;
        }
        var selection = Narrow(e => DataName(e).Equals(installName, StringComparison.Ordinal), "exact-install-name")
            ?? Narrow(e => Key(DataName(e)) == Key(installName), "normalized-install-name")
            ?? Narrow(e => Key(DataName(e)).Length > 0 && Key(installName).Length > 0 &&
                (Key(DataName(e)).StartsWith(Key(installName), StringComparison.Ordinal) ||
                 Key(installName).StartsWith(Key(DataName(e)), StringComparison.Ordinal)), "prefix-install-name")
            ?? Narrow(e => e.ExecutablePath is not null, "matched-player-executable")
            ?? Narrow(e => e.HasManaged || e.Rule == "global-metadata.dat", "player-data-evidence");
        if (selection is not null) return selection;
        long Size(BackendEvidence e)
        {
            if (e.DataPath is null) return 0;
            try
            {
                return new System.IO.DirectoryInfo(System.IO.Path.Combine(game.InstallDir, e.DataPath)).EnumerateFiles("*",
                    new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                        AttributesToSkip = System.IO.FileAttributes.ReparsePoint }).Sum(f => f.Length);
            }
            catch (System.IO.IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }
        var sized = candidates.Select(e => (Evidence: e, Size: Size(e))).OrderByDescending(e => e.Size).ToArray();
        if (sized.Length > 1 && sized[0].Size > sized[1].Size)
            return new(sized[0].Evidence, "largest-data-folder", "low");
        return new(candidates.OrderBy(e => e.Layer == "outer" ? 0 : 1)
            .ThenBy(e => e.PlayerPath, StringComparer.OrdinalIgnoreCase).First(), "outer-then-path-tiebreak", "low");
    }
    public static void Validate(System.Collections.Generic.IEnumerable<SteamGame> games)
    {
        foreach (var g in games)
        {
            int backendBuckets = (g.ClassifiedBackend == UnityBackend.Mono ? 1 : 0) +
                (g.ClassifiedBackend == UnityBackend.Il2Cpp ? 1 : 0) + (g.IsBackendUnresolved ? 1 : 0);
            int runtimeBuckets = (g.HasLegacyMono ? 1 : 0) + (g.HasModernMono ? 1 : 0) + (g.IsRuntimeUnresolved ? 1 : 0);
            if (backendBuckets != 1 || runtimeBuckets != (g.ClassifiedBackend == UnityBackend.Mono ? 1 : 0))
                throw new InvalidOperationException($"Classification partition failed: {g.InstallDir}");
        }
    }
}
