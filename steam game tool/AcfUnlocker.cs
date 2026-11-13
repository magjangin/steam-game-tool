using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace steam_game_tool;

/// <summary>appmanifest_*.acf 파일 하나를 살핀 결과. 파일을 고치기 전 단계다.</summary>
/// <param name="AcfPath">appmanifest_*.acf 전체 경로.</param>
/// <param name="AppId">추출한 AppID. 읽지 못했으면 null.</param>
/// <param name="Name">게임 이름(name 키). 없으면 설치 폴더명 또는 파일명.</param>
/// <param name="WasReadOnly">파일에 읽기 전용(ReadOnly) 속성이 걸려 있는가.</param>
/// <param name="AutoUpdateBehavior">현재 AutoUpdateBehavior 값(예: "0", "1"). 없으면 null.</param>
/// <param name="HasCorruptedLauncherPath">LauncherPath 값에 역슬래시가 빠져 경로가 깨졌는가(예: "H:steamsteam.exe").</param>
/// <param name="BackupFiles">옆에 남아 있는 .acf.bak* 백업 파일 경로들(오래된 순).</param>
public sealed record AcfTarget(
    string AcfPath,
    uint? AppId,
    string Name,
    bool WasReadOnly,
    string? AutoUpdateBehavior,
    bool HasCorruptedLauncherPath,
    IReadOnlyList<string> BackupFiles)
{
    /// <summary>AutoUpdateBehavior 가 "0"(자동 업데이트 허용)이 아닌 값으로 잠겨 있는가.</summary>
    public bool IsAutoUpdateLocked => AutoUpdateBehavior is not null && AutoUpdateBehavior != "0";

    /// <summary>잠금 해제 또는 복원·정리가 필요한 대상인가.</summary>
    public bool NeedsUnlock =>
        WasReadOnly || IsAutoUpdateLocked || HasCorruptedLauncherPath || BackupFiles.Count > 0;
}

/// <summary>ACF 잠금 해제 한 건의 처리 결과.</summary>
public sealed record AcfOutcome(
    AcfTarget Target,
    bool RestoredFromBackup,
    int RemovedBackupCount,
    string? Error);

/// <summary>
/// Steam 라이브러리의 <c>appmanifest_*.acf</c> 잠금 해제 및 복원·정리.
/// <list type="bullet">
/// <item>파일에 걸린 읽기 전용(<see cref="FileAttributes.ReadOnly"/>) 속성을 푼다.</item>
/// <item><c>AutoUpdateBehavior</c> 를 <c>"0"</c>(자동 업데이트 허용)으로 되돌린다.</item>
/// <item>외부 도구가 만든 <c>appmanifest_*.acf.bak*</c> 백업이 있으면 가장 오래된 원본 백업으로 포맷과 <c>LauncherPath</c> 를 복원하고 백업 파일을 정리한다.</item>
/// <item>백업이 없어도 <c>LauncherPath</c> 의 역슬래시가 깨져 있으면 정상 Steam 실행 파일 경로로 복구한다.</item>
/// </list>
/// </summary>
public static class AcfUnlocker
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>지정한 루트(비어 있으면 자동 감지한 모든 Steam 라이브러리)의 모든 <c>appmanifest_*.acf</c> 를 살핀다.</summary>
    public static List<AcfTarget> InspectAll(IEnumerable<string>? roots = null)
    {
        var steamAppsDirs = SteamLibraries.FindSteamAppsFolders(roots);
        var targets = new List<AcfTarget>();

        foreach (var steamapps in steamAppsDirs)
        {
            string[] files;
            try
            {
                // Windows 8.3 짧은 이름 매칭으로 .acf.bak_* 가 딸려 오지 않도록 확장자를 한 번 더 확인한다.
                files = Directory.GetFiles(steamapps, "appmanifest_*.acf")
                    .Where(f => f.EndsWith(".acf", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    targets.Add(Inspect(file));
                }
                catch
                {
                    // 읽기조차 실패한 파일은 건너뛴다.
                }
            }
        }

        return targets
            .OrderBy(t => t.AppId ?? uint.MaxValue)
            .ThenBy(t => t.AcfPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary><c>appmanifest_*.acf</c> 파일 하나의 잠금·손상 상태와 백업 파일 목록을 살핀다. 파일을 고치지 않는다.</summary>
    public static AcfTarget Inspect(string acfPath)
    {
        var fullPath = Path.GetFullPath(acfPath);
        var attrs = File.GetAttributes(fullPath);
        var wasReadOnly = (attrs & FileAttributes.ReadOnly) != 0;

        var text = File.ReadAllText(fullPath, Encoding.UTF8);
        var idText = SteamCatalog.AcfValue(text, "appid");
        uint? appId = uint.TryParse(idText, out var parsedId) ? parsedId : null;

        var name = SteamCatalog.AcfValue(text, "name")
                   ?? SteamCatalog.AcfValue(text, "installdir")
                   ?? Path.GetFileName(fullPath);
        var autoUpdate = SteamCatalog.AcfValue(text, "AutoUpdateBehavior");
        var launcherPath = SteamCatalog.AcfValue(text, "LauncherPath");
        var hasCorruptedLauncherPath = IsCorruptedLauncherPath(launcherPath);

        var dir = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);
        var backups = new List<string>();
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try
            {
                backups = Directory.GetFiles(dir, fileName + ".bak*")
                    .OrderBy(File.GetLastWriteTimeUtc)
                    .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { }
        }

        return new AcfTarget(
            fullPath,
            appId,
            name,
            wasReadOnly,
            autoUpdate,
            hasCorruptedLauncherPath,
            backups);
    }

    /// <summary>잠금 해제가 필요한 대상(<see cref="AcfTarget.NeedsUnlock"/>)을 차례로 처리한다.</summary>
    public static List<AcfOutcome> UnlockAll(
        IReadOnlyList<AcfTarget> targets,
        Action<int, int, AcfTarget>? progress = null)
    {
        var work = targets.Where(t => t.NeedsUnlock).ToList();
        var outcomes = new List<AcfOutcome>(work.Count);

        for (var i = 0; i < work.Count; i++)
        {
            var target = work[i];
            progress?.Invoke(i + 1, work.Count, target);
            try
            {
                outcomes.Add(Unlock(target));
            }
            catch (UnauthorizedAccessException)
            {
                outcomes.Add(new AcfOutcome(target, false, 0, "쓰기 권한이 없습니다"));
            }
            catch (Exception ex)
            {
                outcomes.Add(new AcfOutcome(target, false, 0, ex.Message));
            }
        }

        return outcomes;
    }

    /// <summary>단일 <see cref="AcfTarget"/> 의 잠금을 풀고 원본 포맷 복원 및 <c>.bak*</c> 정리를 수행한다.</summary>
    public static AcfOutcome Unlock(AcfTarget target)
    {
        ClearReadOnlyIfSet(target.AcfPath);
        foreach (var bak in target.BackupFiles)
            ClearReadOnlyIfSet(bak);

        var currentText = File.ReadAllText(target.AcfPath, Encoding.UTF8);
        var workingText = currentText;
        string? oldestBakText = null;
        var restoredFromBackup = false;

        if (target.BackupFiles.Count > 0 && File.Exists(target.BackupFiles[0]))
        {
            oldestBakText = File.ReadAllText(target.BackupFiles[0], Encoding.UTF8);
            if (CanRestoreFromBackup(currentText, oldestBakText))
            {
                workingText = oldestBakText;
                restoredFromBackup = true;
            }
        }

        workingText = EnsureUnlockedText(workingText, target.AcfPath, oldestBakText);

        if (!string.Equals(workingText, currentText, StringComparison.Ordinal))
        {
            var tmpPath = target.AcfPath + ".tmp";
            File.WriteAllText(tmpPath, workingText, Utf8NoBom);
            File.Move(tmpPath, target.AcfPath, overwrite: true);
        }

        var removedBackups = 0;
        foreach (var bak in target.BackupFiles)
        {
            try
            {
                if (File.Exists(bak))
                {
                    File.Delete(bak);
                    removedBackups++;
                }
            }
            catch { }
        }

        return new AcfOutcome(target, restoredFromBackup, removedBackups, null);
    }

    /// <summary>백업 파일이 같은 앱·같은 빌드 상태의 유효한 AppState 인지 확인한다.</summary>
    private static bool CanRestoreFromBackup(string currentText, string backupText)
    {
        if (!backupText.Contains("\"AppState\"", StringComparison.OrdinalIgnoreCase))
            return false;

        var curId = SteamCatalog.AcfValue(currentText, "appid");
        var bakId = SteamCatalog.AcfValue(backupText, "appid");
        if (curId is null || !string.Equals(curId, bakId, StringComparison.Ordinal))
            return false;

        var curBuild = SteamCatalog.AcfValue(currentText, "buildid");
        var bakBuild = SteamCatalog.AcfValue(backupText, "buildid");
        var curState = SteamCatalog.AcfValue(currentText, "StateFlags");
        var bakState = SteamCatalog.AcfValue(backupText, "StateFlags");

        return string.Equals(curBuild, bakBuild, StringComparison.Ordinal) &&
               string.Equals(curState, bakState, StringComparison.Ordinal);
    }

    /// <summary>
    /// VDF 전체 포맷(탭 들여쓰기·줄 바꿈)을 건드리지 않고
    /// <c>AutoUpdateBehavior</c> 를 <c>"0"</c> 으로 바꾸며 깨진 <c>LauncherPath</c> 가 있으면 복구한다.
    /// </summary>
    internal static string EnsureUnlockedText(string text, string acfPath, string? backupText = null)
    {
        var updated = Regex.Replace(
            text,
            @"(""AutoUpdateBehavior""\s+"")[^""]*("")",
            "${1}0${2}",
            RegexOptions.IgnoreCase);

        var launcherPath = SteamCatalog.AcfValue(updated, "LauncherPath");
        if (IsCorruptedLauncherPath(launcherPath))
        {
            var validLauncher = TryResolveEscapedLauncherPath(acfPath, backupText);
            if (validLauncher is not null)
            {
                updated = Regex.Replace(
                    updated,
                    @"(""LauncherPath""\s+"")[^""]*("")",
                    m => m.Groups[1].Value + validLauncher + m.Groups[2].Value,
                    RegexOptions.IgnoreCase);
            }
        }

        return updated;
    }

    private static bool IsCorruptedLauncherPath(string? launcherPath) =>
        !string.IsNullOrEmpty(launcherPath) &&
        !launcherPath.Contains(@"\\") &&
        !launcherPath.Contains('/');

    private static string? TryResolveEscapedLauncherPath(string acfPath, string? backupText)
    {
        if (backupText is not null)
        {
            var fromBak = SteamCatalog.AcfValue(backupText, "LauncherPath");
            if (!string.IsNullOrEmpty(fromBak) && !IsCorruptedLauncherPath(fromBak))
                return fromBak;
        }

        try
        {
            var steamapps = Path.GetDirectoryName(Path.GetFullPath(acfPath));
            var libRoot = steamapps is null ? null : Directory.GetParent(steamapps)?.FullName;
            if (!string.IsNullOrEmpty(libRoot))
            {
                var candidate = Path.Combine(libRoot, "steam.exe");
                if (File.Exists(candidate))
                    return candidate.Replace(@"\", @"\\");
            }
        }
        catch { }

        if (SteamLibraries.FindSteamPath() is { } steamPath)
        {
            var candidate = Path.Combine(steamPath, "steam.exe");
            return candidate.Replace(@"\", @"\\");
        }

        return null;
    }

    private static void ClearReadOnlyIfSet(string path)
    {
        if (!File.Exists(path)) return;
        var attrs = File.GetAttributes(path);
        if ((attrs & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
    }

    /// <summary>대상 하나가 왜 잠금 해제 대상인지 짧게 설명한다.</summary>
    public static string DescribeReason(AcfTarget target)
    {
        var reasons = new List<string>();
        if (target.WasReadOnly) reasons.Add("읽기 전용 해제");
        if (target.IsAutoUpdateLocked) reasons.Add($"AutoUpdateBehavior {target.AutoUpdateBehavior} → 0");
        if (target.HasCorruptedLauncherPath) reasons.Add("깨진 LauncherPath 복원");
        if (target.BackupFiles.Count > 0) reasons.Add($"백업(.bak) {target.BackupFiles.Count}개 복원·정리");
        return reasons.Count > 0 ? string.Join(", ", reasons) : "정상";
    }

    /// <summary>확인 창에 보여 줄 안내문.</summary>
    public static string Summarize(IReadOnlyList<AcfTarget> targets)
    {
        var ready = targets.Where(t => t.NeedsUnlock).ToList();
        var cleanCount = targets.Count - ready.Count;
        var readOnlyCount = ready.Count(t => t.WasReadOnly);
        var autoLockCount = ready.Count(t => t.IsAutoUpdateLocked);
        var corruptCount = ready.Count(t => t.HasCorruptedLauncherPath);
        var backupFileCount = ready.Sum(t => t.BackupFiles.Count);

        var text = new StringBuilder();
        text.AppendLine($"검사한 Steam 매니페스트(appmanifest_*.acf): {targets.Count}개");
        text.AppendLine($"잠금 해제·복원 대상: {ready.Count}개 (정상 {cleanCount}개)");

        if (ready.Count > 0)
        {
            var details = new List<string>();
            if (readOnlyCount > 0) details.Add($"읽기 전용 {readOnlyCount}개");
            if (autoLockCount > 0) details.Add($"AutoUpdateBehavior 잠금 {autoLockCount}개");
            if (corruptCount > 0) details.Add($"깨진 LauncherPath {corruptCount}개");
            if (backupFileCount > 0) details.Add($".bak 백업 파일 {backupFileCount}개");
            text.AppendLine("항목별 내역: " + string.Join(" · ", details));
            text.AppendLine();
            text.AppendLine("수행할 작업");
            text.AppendLine("  1. appmanifest_*.acf 의 읽기 전용(ReadOnly) 속성을 해제합니다.");
            text.AppendLine("  2. AutoUpdateBehavior 를 \"0\"(항상 자동 업데이트 허용)으로 되돌립니다.");
            text.AppendLine("  3. 남아 있는 .acf.bak* 백업 파일이 있으면 원본 포맷과 LauncherPath 를 복원한 뒤 .bak* 파일을 정리합니다.");
            text.AppendLine();
            text.AppendLine("대상 게임 목록");
            foreach (var t in ready)
            {
                var idLabel = t.AppId is { } id ? $"AppID {id}" : Path.GetFileName(t.AcfPath);
                text.AppendLine($"  {t.Name} ({idLabel}) — {DescribeReason(t)}");
            }
        }
        else
        {
            text.AppendLine();
            text.AppendLine("모든 appmanifest_*.acf 파일이 이미 잠금 해제(AutoUpdateBehavior = 0, 읽기 전용 아님, .bak 없음) 상태입니다.");
        }

        return text.ToString();
    }
}
