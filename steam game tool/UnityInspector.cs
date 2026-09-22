using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace steam_game_tool;

/// <summary>설치 폴더 하나를 훑어 얻은 Unity 관측 결과.</summary>
public sealed record UnityInspection(
    UnityBackend Backend,
    bool HasManaged,
    bool HasMonoBleedingEdge,
    string? Error,
    List<BackendEvidence> Evidence,
    int? DataDirectoryCount);

/// <summary>
/// 게임 설치 폴더 안을 훑어 Unity 데이터 폴더·런타임 마커를 찾고 플레이어별 백엔드를 판별한다.
/// 스캔 대상 폴더를 고르는 일은 <see cref="SteamLibraries"/> 가 맡는다.
/// </summary>
public static class UnityInspector
{
    /// <summary>
    /// 게임 폴더 아래를 훑을 최대 재귀 깊이.
    /// 실측(게임 885개) 결과 *_Data 폴더는 게임 폴더 기준 상대 깊이 1~6 에 분포했다.
    /// 여유를 둬서 8 로 잡았다.
    /// 무제한 순회는 대형 게임에서 스캔 시간을 지배하므로 이 상한이 성능의 핵심이다.
    /// 값을 줄이면 판별 결과가 달라질 수 있으니 반드시 실제 라이브러리로 대조할 것.
    /// </summary>
    private const int MaxScanDepth = 8;

    /// <summary>모드로더·서브툴·백업·macOS 번들처럼 판별에서 제외할 폴더 이름 조각.</summary>
    private static readonly string[] IgnoredKeywords =
        ["MelonLoader", "BepInEx", "Voice Editor", "Modded", "BackUpThisFolder", "__MACOSX"];

    /// <summary>Managed 폴더 안에 있으면 Mono 로 보는 대표 어셈블리.</summary>
    private static readonly string[] MonoAssemblies =
        ["Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll", "UnityEngine.dll", "UnityEngine.CoreModule.dll"];

    private static bool IsIgnored(string name) =>
        name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ||
        IgnoredKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 검사 범위의 플레이어별 백엔드를 판별하고 합집합을 반환한다.
    /// 서브 툴(Voice Editor 등)이나 모드 폴더(Modded, MelonLoader 등)의 산출물로 인한 오염을 방지하기 위해
    /// 메인 플레이어와 루트 *_Data 디렉터리를 기준으로 판별한다.
    /// </summary>
    public static UnityInspection Inspect(string installDir)
    {
        var walk = Walk(installDir);
        var backend = UnityBackend.Unknown;
        var evidence = new List<BackendEvidence>();
        int? dataDirectoryCount = null;
        string? error = walk.DataError?.Message;

        // 데이터 폴더 순회가 중단됐으면 부분 결과를 쓰지 않는다. 보조 마커는 그대로 살린다.
        if (walk.DataError is null)
        {
            try
            {
                walk.DataDirs.Sort(StringComparer.OrdinalIgnoreCase);
                dataDirectoryCount = walk.DataDirs.Count;

                foreach (var dataDir in walk.DataDirs)
                    backend |= AddPlayerEvidence(installDir, dataDir, evidence);

                foreach (var runtimePath in walk.RuntimePaths)
                    backend |= AddRuntimeOnlyEvidence(installDir, runtimePath, evidence);
            }
            catch (Exception ex) { error = ex.Message; }
        }

        return new UnityInspection(backend, walk.HasManaged, walk.RuntimePaths.Count > 0,
            error, evidence, dataDirectoryCount);
    }

    private sealed record InstallWalk(
        bool HasManaged, List<string> RuntimePaths, List<string> DataDirs, Exception? DataError);

    /// <summary>
    /// 설치 폴더 아래를 <b>한 번만</b> 훑어 Managed·MonoBleedingEdge·*_Data 를 함께 모은다.
    /// <para>
    /// 예전에는 보조 마커용과 데이터 폴더용으로 같은 트리를 두 번 순회했다.
    /// 두 순회의 가지치기 규칙이 서로 달랐으므로 그 차이를 <c>dataEligible</c> 로 들고 다닌다.
    /// </para>
    /// <list type="bullet">
    /// <item>보조 마커 — 제외 폴더가 아니면 어디든 내려가고, 읽지 못한 폴더는 그것만 건너뛴다.</item>
    /// <item>데이터 폴더 — Managed·MonoBleedingEdge·Mono 아래로는 내려가지 않고,
    /// il2cpp_data 와 재분석 지점(정션·심볼릭 링크)은 통째로 건너뛰며, 오류가 나면 전체가 무효가 된다.</item>
    /// </list>
    /// </summary>
    private static InstallWalk Walk(string installDir)
    {
        var hasManaged = false;
        var runtimePaths = new List<string>();
        var dataDirs = new List<string>();
        Exception? dataError = null;

        var stack = new Stack<(string Dir, int Depth, bool DataEligible)>();
        stack.Push((installDir, 0, true));

        while (stack.Count > 0)
        {
            var (current, depth, dataEligible) = stack.Pop();

            string[] subDirs;
            try { subDirs = Directory.GetDirectories(current); }
            catch (Exception ex)
            {
                if (dataEligible) dataError ??= ex;
                continue;
            }

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (IsIgnored(name)) continue;

                var isRuntime = string.Equals(name, "MonoBleedingEdge", StringComparison.OrdinalIgnoreCase);
                var isManaged = string.Equals(name, "Managed", StringComparison.OrdinalIgnoreCase);
                if (isRuntime) runtimePaths.Add(sub);
                else if (isManaged) hasManaged = true;

                var childDataEligible = false;
                if (dataEligible)
                {
                    try
                    {
                        if (name.Equals("il2cpp_data", StringComparison.OrdinalIgnoreCase) ||
                            (File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                        {
                            childDataEligible = false;
                        }
                        else
                        {
                            if (name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)) dataDirs.Add(sub);
                            childDataEligible = !isManaged && !isRuntime &&
                                !name.Equals("Mono", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch (Exception ex) { dataError ??= ex; }
                }

                if (depth + 1 < MaxScanDepth)
                    stack.Push((sub, depth + 1, childDataEligible));
            }
        }

        return new InstallWalk(hasManaged, runtimePaths, dataDirs, dataError);
    }

    /// <summary>*_Data 폴더 하나를 한 플레이어로 보고 증거를 만든다.</summary>
    private static UnityBackend AddPlayerEvidence(string installDir, string dataDir, List<BackendEvidence> evidence)
    {
        var playerDir = Path.GetDirectoryName(dataDir)!;
        var managedDir = Path.Combine(dataDir, "Managed");
        var hasManagedDir = Directory.Exists(managedDir);

        var metadata = File.Exists(Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat"));
        var nativePair = File.Exists(Path.Combine(playerDir, "GameAssembly.dll")) &&
                         File.Exists(Path.Combine(playerDir, "UnityPlayer.dll"));
        var assemblies = MonoAssemblies.Any(n => File.Exists(Path.Combine(managedDir, n)));

        // FirstOrDefault(Directory.Exists) 를 통과했으므로 null 이 아니면 곧 존재한다는 뜻이다.
        var mbePath = new[] { Path.Combine(playerDir, "MonoBleedingEdge"), Path.Combine(dataDir, "MonoBleedingEdge") }
            .FirstOrDefault(Directory.Exists);
        var monoPath = new[] { Path.Combine(dataDir, "Mono"), Path.Combine(playerDir, "Mono") }
            .FirstOrDefault(Directory.Exists);
        var hasRuntime = mbePath is not null || monoPath is not null;

        // IL2CPP priority is local to one player, never across different players.
        var detected = metadata || nativePair ? UnityBackend.Il2Cpp :
            assemblies || (hasRuntime && hasManagedDir) ? UnityBackend.Mono : UnityBackend.Unknown;

        var relative = Path.GetRelativePath(installDir, dataDir);
        // "<플레이어>_Data" 옆의 "<플레이어>.exe". 존재 여부와 크기를 한 번에 읽는다.
        var executable = new FileInfo(Path.Combine(playerDir, Path.GetFileName(dataDir)[..^5] + ".exe"));

        evidence.Add(new BackendEvidence(relative,
            Path.GetDirectoryName(relative) is "" or null ? "outer" : "inner",
            detected, metadata ? "global-metadata.dat" : nativePair ? "native-player-pair" :
            assemblies ? "managed-assembly" : detected == UnityBackend.Mono ? "runtime-and-managed" : "unknown")
        {
            PlayerPath = Path.GetRelativePath(installDir, playerDir),
            IsBundled = IsBundledPath(relative),
            ExecutablePath = executable.Exists ? Path.GetRelativePath(installDir, executable.FullName) : null,
            ExecutableSize = executable.Exists ? executable.Length : 0,
            HasManaged = hasManagedDir,
            MonoBleedingEdgePath = mbePath is null ? null : Path.GetRelativePath(installDir, mbePath),
            MonoRuntimePath = monoPath is null ? null : Path.GetRelativePath(installDir, monoPath),
        });

        return detected;
    }

    /// <summary>*_Data 없이 MonoBleedingEdge 만 있는 위치를 증거로 남긴다. 이미 다룬 플레이어면 건너뛴다.</summary>
    private static UnityBackend AddRuntimeOnlyEvidence(string installDir, string runtimePath, List<BackendEvidence> evidence)
    {
        var playerPath = Path.GetRelativePath(installDir, Path.GetDirectoryName(runtimePath)!);
        var relativeRuntime = Path.GetRelativePath(installDir, runtimePath);

        if (evidence.Any(e => string.Equals(e.PlayerPath, playerPath, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(e.MonoBleedingEdgePath, relativeRuntime, StringComparison.OrdinalIgnoreCase)))
            return UnityBackend.Unknown;

        evidence.Add(new BackendEvidence(null, playerPath == "." ? "outer" : "inner",
            UnityBackend.Mono, "monobleedingedge-runtime")
        {
            PlayerPath = playerPath,
            IsBundled = IsBundledPath(playerPath),
            MonoBleedingEdgePath = relativeRuntime,
        });

        return UnityBackend.Mono;
    }

    /// <summary>같은 폴더에 함께 담겨 배포된 별도 빌드(_anniversary 등)인가.</summary>
    private static bool IsBundledPath(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar)
            .Any(p => p.Equals("_anniversary", StringComparison.OrdinalIgnoreCase));
}
