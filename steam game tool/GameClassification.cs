using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace steam_game_tool;

public sealed record PlayerSelection(BackendEvidence? Player, string Reason, string Confidence);

public static partial class GameClassification
{
    [GeneratedRegex(@"[^\p{L}\p{Nd}]")]
    private static partial Regex NonAlphanumeric();

    private static string Key(string value) =>
        NonAlphanumeric().Replace(GameNames.Normalize(value), "").ToUpperInvariant();

    public static PlayerSelection Select(SteamGame game)
    {
        var candidates = game.BackendEvidence.Where(e => e.DataPath is not null && e.Backend != UnityBackend.Unknown).ToArray();
        if (candidates.Length == 0) candidates = game.BackendEvidence.Where(e => e.Backend != UnityBackend.Unknown).ToArray();
        if (candidates.Length == 0) return new(null, "no-backend-evidence", "low");
        if (candidates.Length == 1) return new(candidates[0], "single-player", candidates[0].IsBundled ? "low" : "high");

        var installName = Path.GetFileName(Path.TrimEndingDirectorySeparator(game.InstallDir));
        var installKey = Key(installName);
        static string DataName(BackendEvidence e) => e.DataPath is null ? "" : Path.GetFileName(e.DataPath)[..^5];
        // 정규화 키는 후보마다 한 번만 계산한다. 좁히기 단계마다 정규식을 다시 돌리지 않는다.
        var dataKeys = candidates.ToDictionary<BackendEvidence, BackendEvidence, string>(
            e => e, e => Key(DataName(e)), ReferenceEqualityComparer.Instance);

        PlayerSelection? Narrow(Func<BackendEvidence, bool> predicate, string reason)
        {
            var matching = candidates.Where(predicate).ToArray();
            if (matching.Length == 0) return null;
            candidates = matching;
            return matching.Length == 1 ? new(matching[0], reason, matching[0].IsBundled ? "low" : "medium") : null;
        }

        var selection = Narrow(e => DataName(e).Equals(installName, StringComparison.Ordinal), "exact-install-name")
            ?? Narrow(e => dataKeys[e] == installKey, "normalized-install-name")
            ?? Narrow(e => dataKeys[e].Length > 0 && installKey.Length > 0 &&
                (dataKeys[e].StartsWith(installKey, StringComparison.Ordinal) ||
                 installKey.StartsWith(dataKeys[e], StringComparison.Ordinal)), "prefix-install-name")
            ?? Narrow(e => e.ExecutablePath is not null, "matched-player-executable")
            ?? Narrow(e => e.HasManaged || e.Rule == "global-metadata.dat", "player-data-evidence");
        if (selection is not null) return selection;

        long Size(BackendEvidence e)
        {
            if (e.DataPath is null) return 0;
            try
            {
                return new DirectoryInfo(Path.Combine(game.InstallDir, e.DataPath)).EnumerateFiles("*",
                    new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint }).Sum(f => f.Length);
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }
        var sized = candidates.Select(e => (Evidence: e, Size: Size(e))).OrderByDescending(e => e.Size).ToArray();
        if (sized.Length > 1 && sized[0].Size > sized[1].Size)
            return new(sized[0].Evidence, "largest-data-folder", "low");
        return new(candidates.OrderBy(e => e.Layer == "outer" ? 0 : 1)
            .ThenBy(e => e.PlayerPath, StringComparer.OrdinalIgnoreCase).First(), "outer-then-path-tiebreak", "low");
    }

    public static void Validate(IEnumerable<SteamGame> games)
    {
        foreach (var g in games)
        {
            int backendBuckets = (g.ClassifiedBackend == UnityBackend.Mono ? 1 : 0) +
                (g.ClassifiedBackend == UnityBackend.Il2Cpp ? 1 : 0) + (g.IsBackendUnresolved ? 1 : 0);
            int runtimeBuckets = (g.HasLegacyMono ? 1 : 0) + (g.HasModernMono ? 1 : 0) + (g.IsRuntimeUnresolved ? 1 : 0);
            if (backendBuckets != 1 || runtimeBuckets != (g.ClassifiedBackend == UnityBackend.Mono ? 1 : 0))
                throw new InvalidOperationException($"분류 검증 실패: 한 게임이 백엔드·런타임 분류 중 정확히 한 곳에 들어가지 않습니다: {g.InstallDir}");
        }
    }
}
