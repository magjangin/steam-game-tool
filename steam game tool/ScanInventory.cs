using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace steam_game_tool;

/// <summary>
/// 이전 스캔에 있던 게임이 목록에서 조용히 사라지지 않게 하는 검사.
/// <para>
/// 걸린 폴더는 스캔을 막지 않고 경고로 돌려준다. 예전에는 예외를 던져 기준선을 갱신하지 못했으므로,
/// 폴더 하나가 걸리면 그 폴더를 치울 때까지 모든 스캔이 실패했다(docs/07 10번).
/// 대신 걸린 폴더를 새 기준선에도 남겨서, 폴더가 디스크에서 없어지거나 다시 분류될 때까지 매번 알린다.
/// </para>
/// </summary>
public static class ScanInventory
{
    /// <summary>검사 결과. 둘 다 비어 있으면 문제없다.</summary>
    /// <param name="MissingFolders">디스크에는 아직 있는데 이번 스캔 결과에서 빠진 폴더.</param>
    /// <param name="LostUnity">이전에 Unity 로 분류됐고 디스크에도 아직 있는데 이번에는 Unity 가 아닌 폴더.</param>
    public sealed record InventoryCheck(IReadOnlyList<string> MissingFolders, IReadOnlyList<string> LostUnity)
    {
        public static InventoryCheck None { get; } = new([], []);

        public bool IsClean => MissingFolders.Count == 0 && LostUnity.Count == 0;

        public InventoryCheck Merge(InventoryCheck other) => new(
            MissingFolders.Union(other.MissingFolders, StringComparer.OrdinalIgnoreCase).ToArray(),
            LostUnity.Union(other.LostUnity, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>기준선 파일 내용. 스캔한 모든 폴더와, 그중 Unity 로 분류된 폴더(보기 「전체 Unity 게임」).</summary>
    public sealed record Baseline(string[] Scanned, string[] Unity);

    /// <summary>앱을 다시 켜도 비교할 수 있게 기준선을 두는 곳. 루트 구성마다 파일이 하나씩 생긴다.</summary>
    public static string BaselineDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamGameTool", "scan-baselines");

    public static Baseline BaselineOf(ScanResult result) => new(
        result.Games.Select(g => g.InstallDir).ToArray(),
        SteamScanner.Filter(result, Marker.Any).Select(g => g.InstallDir).ToArray());

    /// <summary>
    /// 같은 루트 구성의 기준선 파일과 비교하고 기준선을 갱신한다.
    /// 걸린 폴더는 새 기준선에도 남겨 두므로 해결될 때까지 다음 스캔에서도 다시 걸린다.
    /// 기준선 파일이 없거나 읽을 수 없으면 비교 없이 새로 만든다.
    /// </summary>
    /// <param name="baselineDirectory">기준선 폴더. null 이면 <see cref="BaselineDirectory"/>. 테스트는 임시 폴더를 넘긴다.</param>
    public static InventoryCheck CheckPersistent(ScanResult result, string? baselineDirectory = null)
    {
        var directory = baselineDirectory ?? BaselineDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, BaselineKey(result.Roots) + ".json");
        var current = BaselineOf(result);

        var check = InventoryCheck.None;
        if (ReadBaseline(path) is { } previous)
            check = Check(previous, current);

        var next = new Baseline(
            current.Scanned.Union(check.MissingFolders, StringComparer.OrdinalIgnoreCase).ToArray(),
            current.Unity.Union(check.LostUnity, StringComparer.OrdinalIgnoreCase).ToArray());
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(next));
        File.Move(path + ".tmp", path, overwrite: true);
        return check;
    }

    /// <summary>기준선 파일 이름. 스캔한 루트 목록(정렬·대문자)의 SHA-256 이다. 기존 파일과 맞도록 계산 방식을 바꾸지 않는다.</summary>
    private static string BaselineKey(IEnumerable<string> roots)
    {
        var joined = string.Join("\n", roots.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    private static Baseline? ReadBaseline(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Baseline>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    /// <summary>이전 기준선과 이번 결과를 비교한다. 디스크에서 실제로 없어진 폴더는 문제로 보지 않는다.</summary>
    public static InventoryCheck Check(Baseline previous, Baseline current, Func<string, bool>? directoryExists = null) =>
        new(MissingFolders(previous.Scanned, current.Scanned, directoryExists),
            LostUnity(previous.Unity, current.Unity, directoryExists));

    /// <summary>이전에 있던 폴더 중 디스크에는 아직 있는데 이번 결과에서 빠진 것.</summary>
    public static string[] MissingFolders(IEnumerable<string> previous, IEnumerable<string> current,
        Func<string, bool>? directoryExists = null) => StillOnDiskButGone(previous, current, directoryExists);

    /// <summary>이전에 Unity 였던 폴더 중 디스크에는 아직 있는데 이번에 Unity 가 아닌 것.</summary>
    public static string[] LostUnity(IEnumerable<string> previousUnity, IEnumerable<string> currentUnity,
        Func<string, bool>? directoryExists = null) => StillOnDiskButGone(previousUnity, currentUnity, directoryExists);

    private static string[] StillOnDiskButGone(IEnumerable<string> previous, IEnumerable<string> current,
        Func<string, bool>? directoryExists)
    {
        directoryExists ??= Directory.Exists;
        var after = current.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return previous.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => !after.Contains(p) && directoryExists(p))
            .ToArray();
    }

    /// <summary><see cref="LostUnity"/> 가 비어 있지 않으면 예외를 던진다.</summary>
    public static void ValidateUnityRetention(IEnumerable<string> previousUnity, IEnumerable<string> currentUnity,
        Func<string, bool>? directoryExists = null)
    {
        var lost = LostUnity(previousUnity, currentUnity, directoryExists);
        if (lost.Length > 0)
            throw new InvalidOperationException("전에 Unity 로 분류됐고 아직 설치돼 있는데 이번에는 Unity 가 아닌 폴더: " + string.Join(", ", lost));
    }

    /// <summary>
    /// 같은 루트의 두 스캔을 비교한다. <see cref="MissingFolders"/> 가 비어 있지 않으면 예외를 던지고,
    /// 아니면 추가·삭제 요약을 돌려준다. 디스크에서 실제로 없어진 폴더는 분류 변경이 아니라 삭제로 센다.
    /// </summary>
    public static string Compare(IEnumerable<string> previous, IEnumerable<string> current,
        Func<string, bool>? directoryExists = null)
    {
        directoryExists ??= Directory.Exists;
        var before = previous.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = current.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = MissingFolders(before, after, directoryExists);
        if (missing.Length > 0)
            throw new InvalidOperationException("디스크에는 있는데 스캔 결과에서 빠진 폴더: " + string.Join(", ", missing));
        var added = after.Except(before).ToArray();
        var removed = before.Except(after).ToArray();
        return $"Previous: {before.Count}; current: {after.Count}; added: {added.Length}; removed on disk: {removed.Length}\r\n" +
            string.Join("\r\n", added.Select(p => "+ " + p).Concat(removed.Select(p => "- " + p)));
    }
}
