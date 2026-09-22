using System.Collections.Generic;
using System.Linq;

namespace steam_game_tool;

public sealed record ExportList(Marker Marker, string FileName, string Header);

public static class ExportLists
{
    public static readonly ExportList[] All =
    [
        new(Marker.Any, "list_unity_games.txt", "Unity games classified by the selected player (Mono + IL2CPP):"),
        new(Marker.UnityMono, "list_unity_mono_games.txt", "Unity Mono games classified by the selected player:"),
        new(Marker.UnityIl2Cpp, "list_unity_il2cpp_games.txt", "Unity IL2CPP games classified by the selected player:"),
        new(Marker.MonoLegacy, "list_legacy_mono_games.txt", "Unity Mono games with a resolved Mono runtime, excluding MonoBleedingEdge:"),
        new(Marker.MonoBleedingEdge, "list_monobleedingedge_games.txt", "Unity Mono games with MonoBleedingEdge on the selected player:"),
        new(Marker.NestedUnity, "list_nested_unity_games.txt", "Games with Unity player data below the install root (structural nesting):"),
        new(Marker.PlayerUnresolved, "list_player_unresolved.txt", "Scanned entries whose player classification is unresolved:"),
        new(Marker.NonUnity, "list_non_unity_games.txt", "Known Steam entries with no detected Unity evidence (not an engine identification):"),
        new(Marker.ShadowedName, "list_shadowed_name_games.txt", "Unity-observed entries with a same-name subfolder (original name-shadowing signal):"),
        new(Marker.Nested, "list_samename_game_folders.txt", "All entries with a same-name subfolder, including entries with no Unity evidence:"),
        new(Marker.RuntimeUnresolved, "list_runtime_unresolved.txt", "Mono games whose selected player runtime is unresolved:"),
        new(Marker.BackendUnresolved, "list_backend_unresolved.txt", "Scanned entries with unresolved main-player backend (including bundled-only and ambiguous evidence):"),
        new(Marker.Scanned, "list_scanned_games.txt", "All scanned entries in the selected scope, including unresolved entries:"),
        new(Marker.NestedNonUnity, "list_samename_no_unity_detected_games.txt", "Same-name subfolders with no detected Unity backend:"),
        new(Marker.Managed, "list_managed_games.txt", "Entries with Managed folders:"),
    ];

    public static ExportList For(Marker marker) => All.Single(e => e.Marker == marker);

    public static string Generate(ScanResult result, Marker marker, bool gamesOnly)
    {
        Validate(result, gamesOnly);
        return GameExport.ToText(SteamScanner.Filter(result, marker, gamesOnly), For(marker).Header);
    }

    /// <summary>
    /// 여러 목록을 같은 스캔 결과·범위로 한 번에 저장한다. 기존 파일명을 그대로 쓴다.
    /// 검증과 텍스트 생성을 모두 끝낸 뒤에 쓰므로 검증 실패 시 일부 파일만 남지 않는다.
    /// </summary>
    public static IReadOnlyList<string> WriteAll(ScanResult result, IEnumerable<Marker> markers, bool gamesOnly, string folder)
    {
        Validate(result, gamesOnly);
        var outputs = markers.Distinct()
            .Select(m => (Path: System.IO.Path.Combine(folder, For(m).FileName),
                          Text: GameExport.ToText(SteamScanner.Filter(result, m, gamesOnly), For(m).Header)))
            .ToArray();
        foreach (var (path, text) in outputs)
            System.IO.File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        return outputs.Select(o => o.Path).ToArray();
    }

    private static void Validate(ScanResult result, bool gamesOnly)
    {
        var scope = SteamScanner.Filter(result, Marker.Scanned, gamesOnly).ToArray();
        GameClassification.Validate(scope);
        // 분류 속성만이 아니라 실제 필터 결과로 분할을 확인한다.
        var mono = SteamScanner.Filter(result, Marker.UnityMono, gamesOnly).ToHashSet();
        var il2cpp = SteamScanner.Filter(result, Marker.UnityIl2Cpp, gamesOnly).ToHashSet();
        var unresolved = SteamScanner.Filter(result, Marker.BackendUnresolved, gamesOnly).ToHashSet();
        var legacy = SteamScanner.Filter(result, Marker.MonoLegacy, gamesOnly).ToHashSet();
        var modern = SteamScanner.Filter(result, Marker.MonoBleedingEdge, gamesOnly).ToHashSet();
        var runtime = SteamScanner.Filter(result, Marker.RuntimeUnresolved, gamesOnly).ToHashSet();
        RequirePartition(scope, mono, il2cpp, unresolved);
        RequirePartition(mono, legacy, modern, runtime);
        var unity = SteamScanner.Filter(result, Marker.Any, gamesOnly).ToHashSet();
        var player = SteamScanner.Filter(result, Marker.PlayerUnresolved, gamesOnly).ToHashSet();
        RequirePartition(scope, unity, SteamScanner.Filter(result, Marker.NonUnity, gamesOnly).ToHashSet(), player);
        if (!SteamScanner.Filter(result, Marker.NestedUnity, gamesOnly).All(g => unity.Contains(g) || player.Contains(g)))
            throw new System.InvalidOperationException("목록 검증 실패: 중첩 목록의 게임이 Unity·플레이어 미확정 목록 어디에도 없습니다.");
    }

    private static void RequirePartition(IEnumerable<SteamGame> scope, params HashSet<SteamGame>[] sets)
    {
        var union = new HashSet<SteamGame>();
        foreach (var set in sets)
        {
            if (union.Overlaps(set)) throw new System.InvalidOperationException("목록 검증 실패: 한 게임이 서로 겹치면 안 되는 목록 두 곳에 들어 있습니다.");
            union.UnionWith(set);
        }
        if (!union.SetEquals(scope)) throw new System.InvalidOperationException("목록 검증 실패: 어느 목록에도 들어가지 않은 게임이 있습니다.");
    }
}
