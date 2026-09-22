using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace steam_game_tool;

/// <summary>PE 실행 파일의 대상 CPU. MelonLoader 는 게임과 같은 비트수의 배포본을 써야 한다.</summary>
public enum PeArch { Unknown, X86, X64 }

/// <summary>
/// MelonLoader 배포 zip. 최상위에 version.dll 과 MelonLoader\ 가 있어야 한다.
/// Mono 와 IL2CPP 는 같은 zip 을 쓰고, 갈리는 것은 비트수뿐이다.
/// </summary>
/// <param name="Files">zip 안의 파일 항목(폴더 제외). 경로 구분자는 '/'.</param>
/// <param name="Version">zip 의 MelonLoader 버전. 읽지 못했으면 null.</param>
public sealed record MelonPackage(string ZipPath, PeArch Arch, IReadOnlyList<string> Files, Version? Version = null);

/// <summary>설치하지 않고 건너뛰는 이유. None 이면 설치 대상이다.</summary>
public enum MelonSkip
{
    None,
    NoExecutable,
    UncertainPlayer,
    BepInEx,
    ForeignVersionDll,
    AntiCheat,
    UnknownArch,
    NotAutoTarget,
    RemovedByUser,
    AlreadyInstalled,
    InstalledMismatch,
    NoRelease,
    NoPackage,
    FileConflict,
}

/// <summary>파일을 쓰지 않고 게임 하나를 살핀 결과. 설치할 zip 을 고르기 전 단계다.</summary>
/// <param name="TargetDir">zip 을 풀 폴더 = 대표 플레이어 실행 파일이 있는 폴더. 중첩 게임이면 안쪽 폴더다.</param>
/// <param name="HasInstall">대상 폴더에 MelonLoader\ 가 있다.</param>
/// <param name="Requirement">자동 설치 규칙. null 이면 자동 설치 대상이 아니다(구형 레거시 Mono).</param>
/// <param name="WasRemoved">MelonLoader\ 는 없는데 MelonLoader 가 만드는 폴더만 남아 있다 = 사용자가 지웠다.</param>
public sealed record MelonTarget(SteamGame Game, string? TargetDir, PeArch Arch, MelonSkip Skip,
    bool HasInstall, Version? InstalledVersion, MelonRequirement? Requirement, bool WasRemoved = false);

public sealed record MelonPlan(MelonTarget Target, MelonSkip Skip, MelonPackage? Package = null)
{
    public SteamGame Game => Target.Game;
    public string? TargetDir => Target.TargetDir;
    public PeArch Arch => Target.Arch;

    /// <summary>기존 MelonLoader 를 지우고 이 zip 으로 바꾼다.</summary>
    public bool Replaces => Skip == MelonSkip.None && Target.HasInstall;
}

public sealed record MelonOutcome(MelonPlan Plan, string? Error);

/// <summary>
/// 게임 폴더에 MelonLoader 배포 zip 을 푼다. 이 도구에서 게임 폴더에 파일을 쓰는 유일한 곳이다.
/// <list type="bullet">
/// <item>기존 파일은 덮어쓰지 않는다. 교체할 때만 version.dll·dobby.dll·NOTICE.txt·MelonLoader\ 를 바꾸고, Mods·UserData 등은 남긴다.</item>
/// <item>한 게임에서 실패하면 그 게임에 이번에 만든 것을 지우고, 교체하려고 옮겨 둔 옛 설치를 되돌린다.</item>
/// <item>다 푼 뒤 파일이 모두 있는지 확인한다. 백신이 격리하면 여기서 실패한다.</item>
/// </list>
/// 설치 뒤에도 스캔 결과는 달라지지 않는다 — <see cref="UnityInspector"/> 가 MelonLoader 폴더를 무시한다.
/// </summary>
public static class MelonLoaderInstaller
{
    /// <summary>있으면 건너뛰는 안티치트 폴더. 온라인 게임에 로더를 넣어 제재받는 일을 막는다.</summary>
    private static readonly string[] AntiCheatFolders = ["EasyAntiCheat", "BattlEye"];

    /// <summary>
    /// 교체할 때 옛 설치에서 바꾸는 최상위 파일. MelonLoader\ 폴더도 함께 바꾼다.
    /// 0.6.x zip 은 루트에 dobby.dll·NOTICE.txt 를 두고 0.7.x 는 두지 않으므로, 둘 다 MelonLoader 것으로 보고 치운다.
    /// </summary>
    private static readonly string[] ReplacedRootFiles = ["version.dll", "dobby.dll", "NOTICE.txt"];

    /// <summary>설치가 끝나면 만들어 두는 폴더. 모드를 바로 넣을 수 있게 한다.</summary>
    private static readonly string[] ModFolders = ["Mods", "Plugins", "UserData"];

    /// <summary>PE 헤더의 Machine 필드를 읽는다. 스트림은 탐색 가능해야 한다.</summary>
    public static PeArch ReadArch(Stream stream)
    {
        Span<byte> dos = stackalloc byte[0x40];
        if (stream.ReadAtLeast(dos, dos.Length, throwOnEndOfStream: false) < dos.Length ||
            dos[0] != 'M' || dos[1] != 'Z')
            return PeArch.Unknown;

        var peOffset = BitConverter.ToInt32(dos[0x3C..]);
        if (peOffset <= 0 || peOffset > stream.Length - 6) return PeArch.Unknown;

        stream.Position = peOffset;
        Span<byte> pe = stackalloc byte[6];
        if (stream.ReadAtLeast(pe, pe.Length, throwOnEndOfStream: false) < pe.Length ||
            pe[0] != 'P' || pe[1] != 'E' || pe[2] != 0 || pe[3] != 0)
            return PeArch.Unknown;

        return BitConverter.ToUInt16(pe[4..]) switch
        {
            0x014C => PeArch.X86,
            0x8664 => PeArch.X64,
            _ => PeArch.Unknown,
        };
    }

    public static PeArch ReadArch(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadArch(stream);
    }

    /// <summary>
    /// zip 이 MelonLoader 배포본인지 확인하고, version.dll 로 비트수를 읽는다. 파일 이름에 기대지 않는다.
    /// <paramref name="version"/> 을 주지 않으면 zip 안의 MelonLoader.dll 에서 읽는다.
    /// </summary>
    public static MelonPackage OpenPackage(string zipPath, Version? version = null)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => EntryName(e.FullName)).ToList();
        var proxy = zip.Entries.FirstOrDefault(e => EntryName(e.FullName).Equals("version.dll", StringComparison.OrdinalIgnoreCase));
        if (proxy is null || !names.Any(n => n.StartsWith("MelonLoader/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                $"MelonLoader 배포 zip 이 아닙니다 — 최상위에 version.dll 과 MelonLoader 폴더가 있어야 합니다: {Path.GetFileName(zipPath)}");

        // 압축 해제 스트림은 탐색할 수 없으므로 메모리로 옮겨 읽는다. version.dll 은 작다.
        using var buffer = new MemoryStream();
        using (var entry = proxy.Open()) entry.CopyTo(buffer);
        buffer.Position = 0;

        var arch = ReadArch(buffer);
        if (arch == PeArch.Unknown)
            throw new InvalidDataException($"version.dll 의 비트수를 읽지 못했습니다: {Path.GetFileName(zipPath)}");

        return new MelonPackage(zipPath, arch, names.Where(n => n.Length > 0 && !n.EndsWith('/')).ToList(),
            version ?? ReadZipVersion(zip));
    }

    /// <summary>zip 안 MelonLoader.dll 의 버전. FileVersionInfo 는 파일만 읽으므로 임시 파일로 꺼낸다.</summary>
    private static Version? ReadZipVersion(ZipArchive zip)
    {
        var entry = zip.Entries
            .Where(e => EntryName(e.FullName).StartsWith("MelonLoader/", StringComparison.OrdinalIgnoreCase) &&
                        e.Name.Equals("MelonLoader.dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName.Contains("net35", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
        if (entry is null) return null;

        var temp = Path.Combine(Path.GetTempPath(), $"sgt-melonloader-{Guid.NewGuid():N}.dll");
        try
        {
            entry.ExtractToFile(temp);
            return MelonVersions.FromFile(temp);
        }
        catch (IOException) { return null; }
        catch (InvalidDataException) { return null; }
        finally
        {
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>고른 zip 들을 연다. 같은 비트수가 둘이면 어느 것을 쓸지 모르므로 거부한다.</summary>
    public static List<MelonPackage> OpenPackages(IEnumerable<string> zipPaths)
    {
        var packages = zipPaths.Select(p => OpenPackage(p)).ToList();
        var duplicate = packages.GroupBy(p => p.Arch).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"{Describe(duplicate.Key)} zip 을 두 개 골랐습니다 — 비트수마다 하나씩만 고르세요.");
        return packages;
    }

    /// <summary>게임 하나의 설치 위치·비트수·기존 설치와, zip 과 무관하게 건너뛸 이유를 정한다. 파일을 쓰지 않는다.</summary>
    public static MelonTarget Inspect(SteamGame game)
    {
        var requirement = MelonVersions.For(game);
        var executable = game.MainPlayer?.ExecutablePath;
        if (executable is null) return new(game, null, PeArch.Unknown, MelonSkip.NoExecutable, false, null, requirement);

        var exePath = Path.Combine(game.InstallDir, executable);
        var target = Path.GetDirectoryName(exePath)!;
        bool HasDir(string dir, string name) => Directory.Exists(Path.Combine(dir, name));
        bool HasFile(string name) => File.Exists(Path.Combine(target, name));

        var hasInstall = HasDir(target, "MelonLoader");
        var installed = hasInstall ? MelonVersions.Installed(target) : null;
        // UserLibs 는 MelonLoader 가 처음 실행될 때, Mods·Plugins·UserData 는 설치할 때 생긴다. 로더를 지워도 남는다.
        // 사용자는 모드가 안 되는 게임(Skul, Paper Animal Adventure, Menherarium 등)에서 일부러 지웠다(2026-09-21).
        var wasRemoved = !hasInstall &&
                         (HasDir(target, "UserLibs") || HasDir(target, "UserData") && HasDir(target, "Mods") && HasDir(target, "Plugins"));
        MelonTarget Result(MelonSkip skip, PeArch arch = PeArch.Unknown) =>
            new(game, target, arch, skip, hasInstall, installed, requirement, wasRemoved);

        // 신뢰도 low 는 크기·경로 순서로 고른 후보이거나 번들 빌드다. 실측에서 DLC·업데이트 데이터 폴더가
        // 대표로 잡힌 경우가 있었다(The Long Dark → tld_dlc\wintermute). 판별은 그대로 두고 설치만 건너뛴다.
        if (game.Selection.Confidence == "low") return Result(MelonSkip.UncertainPlayer);
        if (HasDir(target, "BepInEx") || HasFile("doorstop_config.ini")) return Result(MelonSkip.BepInEx);
        // MelonLoader\ 없이 version.dll 만 있으면 다른 모드의 프록시일 수 있다. 교체도 하지 않는다.
        if (!hasInstall && HasFile("version.dll")) return Result(MelonSkip.ForeignVersionDll);
        if (AntiCheatFolders.Any(a => HasDir(target, a) || HasDir(game.InstallDir, a))) return Result(MelonSkip.AntiCheat);

        PeArch arch;
        try { arch = ReadArch(exePath); }
        catch (IOException) { arch = PeArch.Unknown; }
        catch (UnauthorizedAccessException) { arch = PeArch.Unknown; }
        if (arch == PeArch.Unknown) return Result(MelonSkip.UnknownArch);

        return Result(MelonSkip.None, arch);
    }

    public static List<MelonPlan> Plan(IEnumerable<SteamGame> games, IReadOnlyCollection<MelonPackage> packages, bool replace = false) =>
        games.Select(g => Plan(g, packages, replace)).ToList();

    /// <summary>직접 고른 zip: 비트수만 맞춰 모든 게임에 같은 zip 을 쓴다. 버전 규칙은 쓰지 않는다.</summary>
    public static MelonPlan Plan(SteamGame game, IReadOnlyCollection<MelonPackage> packages, bool replace = false)
    {
        var target = Inspect(game);
        var package = packages.FirstOrDefault(p => p.Arch == target.Arch);
        var installedIsFine = target.InstalledVersion is not null && package?.Version is not null &&
                              MelonVersions.Same(target.InstalledVersion, package.Version);
        return Finish(target, package, installedIsFine, replace, MelonSkip.NoPackage);
    }

    /// <summary>자동 설치에서 받아야 할 (릴리스, 비트수). 이미 규칙에 맞게 설치된 게임은 받지 않는다.</summary>
    public static List<(MelonRelease Release, PeArch Arch)> RequiredDownloads(
        IEnumerable<MelonTarget> targets, IReadOnlyList<MelonRelease> releases, bool replace) =>
        targets
            .Where(t => t.Skip == MelonSkip.None && t.Requirement is not null && !t.WasRemoved &&
                        (!t.HasInstall || replace && !t.Requirement.IsSatisfiedBy(t.InstalledVersion)))
            .Select(t => (Release: t.Requirement!.Pick(releases), t.Arch))
            .Where(x => x.Release is not null && x.Release.Zips.ContainsKey(x.Arch))
            .Select(x => (x.Release!, x.Arch))
            .Distinct()
            .ToList();

    /// <summary>자동 설치: 게임마다 버전 규칙(<see cref="MelonVersions.For"/>)에 맞는 릴리스의 zip 을 쓴다.</summary>
    public static List<MelonPlan> PlanAuto(IEnumerable<MelonTarget> targets, IReadOnlyList<MelonRelease> releases,
        IReadOnlyCollection<MelonPackage> packages, bool replace) =>
        targets.Select(t =>
        {
            if (t.Skip == MelonSkip.None && t.Requirement is null) return new MelonPlan(t, MelonSkip.NotAutoTarget);
            // 사용자가 지운 로더를 일괄 설치가 되살리지 않게 한다. 「zip 직접 선택…」으로는 설치할 수 있다.
            if (t.Skip == MelonSkip.None && t.WasRemoved) return new MelonPlan(t, MelonSkip.RemovedByUser);
            var release = t.Requirement?.Pick(releases);
            var package = release is null ? null : packages.FirstOrDefault(p =>
                p.Arch == t.Arch && p.Version is not null && MelonVersions.Same(p.Version, release.Version));
            return Finish(t, package, t.Requirement?.IsSatisfiedBy(t.InstalledVersion) == true, replace,
                release is null ? MelonSkip.NoRelease : MelonSkip.NoPackage);
        }).ToList();

    /// <param name="installedIsFine">이미 설치된 버전을 그대로 둬도 되는가.</param>
    private static MelonPlan Finish(MelonTarget target, MelonPackage? package, bool installedIsFine, bool replace, MelonSkip missing)
    {
        if (target.Skip != MelonSkip.None) return new(target, target.Skip);
        if (target.HasInstall)
        {
            // 버전을 모르는 설치는 교체를 켰을 때만 바꾼다.
            if (installedIsFine || (!replace && target.InstalledVersion is null)) return new(target, MelonSkip.AlreadyInstalled, package);
            if (!replace) return new(target, MelonSkip.InstalledMismatch, package);
        }
        if (package is null) return new(target, missing);

        var conflict = package.Files
            .Where(f => !(target.HasInstall && IsReplaced(f)))
            .Any(f => File.Exists(Path.Combine(target.TargetDir!, f)) || Directory.Exists(Path.Combine(target.TargetDir!, f)));
        return new(target, conflict ? MelonSkip.FileConflict : MelonSkip.None, package);
    }

    private static bool IsReplaced(string entry) =>
        ReplacedRootFiles.Contains(entry, StringComparer.OrdinalIgnoreCase) ||
        entry.StartsWith("MelonLoader/", StringComparison.OrdinalIgnoreCase);

    /// <summary>설치 대상(Skip == None)만 차례로 설치한다. 한 게임의 실패가 나머지를 멈추지 않는다.</summary>
    public static List<MelonOutcome> InstallAll(IReadOnlyList<MelonPlan> plans, Action<int, int, SteamGame>? progress = null)
    {
        var targets = plans.Where(p => p.Skip == MelonSkip.None).ToList();
        var outcomes = new List<MelonOutcome>(targets.Count);
        for (var i = 0; i < targets.Count; i++)
        {
            progress?.Invoke(i + 1, targets.Count, targets[i].Game);
            try
            {
                Install(targets[i]);
                outcomes.Add(new(targets[i], null));
            }
            catch (UnauthorizedAccessException) { outcomes.Add(new(targets[i], "쓰기 권한이 없습니다")); }
            catch (Exception ex) { outcomes.Add(new(targets[i], ex.Message)); }
        }
        return outcomes;
    }

    /// <summary>zip 을 대상 폴더에 푼다. 실패하면 이번에 만든 것을 지우고 옛 설치를 되돌린 뒤 예외를 다시 던진다.</summary>
    public static void Install(MelonPlan plan)
    {
        if (plan.Skip != MelonSkip.None || plan.TargetDir is null || plan.Package is null)
            throw new InvalidOperationException("설치 대상이 아닌 계획입니다.");

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.TargetDir)) + Path.DirectorySeparatorChar;
        var createdFiles = new List<string>();
        var createdDirs = new List<string>();
        var moved = new List<(string From, string To)>();

        void EnsureDirectory(string dir)
        {
            dir = Path.TrimEndingDirectorySeparator(dir);
            if (Directory.Exists(dir)) return;
            EnsureDirectory(Path.GetDirectoryName(dir)!);
            Directory.CreateDirectory(dir);
            createdDirs.Add(dir);
        }

        try
        {
            if (plan.Replaces)
            {
                // 지우지 않고 옆으로 옮겨 둔다. 이름에 "MelonLoader" 가 들어가 스캐너도 무시한다.
                var suffix = ".replaced-" + Guid.NewGuid().ToString("N")[..8];
                foreach (var name in ReplacedRootFiles.Append("MelonLoader"))
                {
                    var path = Path.Combine(root, name);
                    if (File.Exists(path)) File.Move(path, path + suffix);
                    else if (Directory.Exists(path)) Directory.Move(path, path + suffix);
                    else continue;
                    moved.Add((path, path + suffix));
                }
            }

            using (var zip = ZipFile.OpenRead(plan.Package.ZipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    var name = EntryName(entry.FullName);
                    if (name.Length == 0) continue;

                    var destination = Path.GetFullPath(Path.Combine(root, name));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("zip 항목이 설치 폴더 밖을 가리킵니다: " + entry.FullName);

                    if (name.EndsWith('/'))
                    {
                        EnsureDirectory(destination);
                        continue;
                    }

                    EnsureDirectory(Path.GetDirectoryName(destination)!);
                    // CreateNew 가 성공해야만 "이번에 만든 파일"로 기록한다. 원래 있던 파일은 되돌리기에서도 지우지 않는다.
                    using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                    createdFiles.Add(destination);
                    using var input = entry.Open();
                    input.CopyTo(output);
                }
            }

            var missing = plan.Package.Files.Where(f => !File.Exists(Path.Combine(root, f))).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    $"설치 검증 실패: 파일 {missing.Count}개가 없습니다(예: {Path.GetFileName(missing[0])}). " +
                    "백신(Windows Defender 등)이 격리했을 가능성이 높습니다 — 게임 폴더를 백신 예외에 추가한 뒤 다시 시도하세요.");
        }
        catch (Exception ex)
        {
            foreach (var file in Enumerable.Reverse(createdFiles))
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            // 비재귀 삭제라 비어 있는 폴더만 지워진다.
            foreach (var dir in Enumerable.Reverse(createdDirs))
            {
                try { Directory.Delete(dir); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            var stuck = new List<string>();
            foreach (var (from, to) in Enumerable.Reverse(moved))
            {
                try
                {
                    if (Directory.Exists(to)) Directory.Move(to, from);
                    else File.Move(to, from);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { stuck.Add(to); }
            }
            if (stuck.Count > 0)
                throw new IOException($"{ex.Message} — 기존 설치를 되돌리지 못했습니다: {string.Join(", ", stuck)}", ex);
            throw;
        }

        // 여기부터는 설치가 끝난 뒤의 정리다. 실패해도 설치 결과는 그대로 둔다.
        foreach (var (_, to) in moved)
        {
            try
            {
                if (Directory.Exists(to)) Directory.Delete(to, recursive: true);
                else File.Delete(to);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var folder in ModFolders)
        {
            try { Directory.CreateDirectory(Path.Combine(root, folder)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string Describe(PeArch arch) => arch switch
    {
        PeArch.X86 => "x86",
        PeArch.X64 => "x64",
        _ => "?",
    };

    public static string Describe(MelonPlan plan) => plan.Skip switch
    {
        MelonSkip.NoExecutable => "대표 실행 파일을 찾지 못함",
        MelonSkip.UncertainPlayer => "대표 실행 파일이 확실하지 않음 — 직접 설치 권장",
        MelonSkip.BepInEx => "BepInEx 설치됨",
        MelonSkip.ForeignVersionDll => "MelonLoader 가 아닌 version.dll 있음",
        MelonSkip.AntiCheat => "안티치트 폴더 있음 (EasyAntiCheat·BattlEye)",
        MelonSkip.UnknownArch => "실행 파일 비트수를 읽지 못함",
        MelonSkip.NotAutoTarget => "구형 레거시 Mono — 자동 설치 대상 아님 (「zip 직접 선택…」으로는 설치 가능)",
        MelonSkip.RemovedByUser => "MelonLoader 를 지운 흔적 있음 (Mods·UserData 등만 남음) — 자동 설치 안 함 (「zip 직접 선택…」으로는 설치 가능)",
        MelonSkip.AlreadyInstalled => "이미 MelonLoader 있음",
        MelonSkip.InstalledMismatch => "다른 버전이 설치됨 — 「다른 버전이 설치된 게임도 교체」를 켜면 바꿉니다",
        MelonSkip.NoRelease => "규칙에 맞는 릴리스가 GitHub 에 없음",
        MelonSkip.NoPackage => $"맞는 {Describe(plan.Arch)} zip 없음",
        MelonSkip.FileConflict => "zip 안의 파일과 같은 이름이 이미 있음",
        _ => "설치",
    };

    private static string VersionOf(MelonPackage? package) => MelonVersions.Format(package?.Version);

    /// <summary>설치 확인 창에 보여 줄 안내문. <paramref name="releases"/> 가 있으면 자동 설치다.</summary>
    public static string Summarize(IReadOnlyList<MelonPlan> plans, IReadOnlyCollection<MelonPackage> packages,
        int uncheckedCount, IReadOnlyList<MelonRelease>? releases = null)
    {
        var ready = plans.Where(p => p.Skip == MelonSkip.None).ToList();
        var skipped = plans.Where(p => p.Skip != MelonSkip.None).ToList();
        var replacing = ready.Count(p => p.Replaces);
        var text = new StringBuilder();

        var groups = string.Join(" · ", ready.GroupBy(p => (Version: VersionOf(p.Package), p.Arch))
            .OrderByDescending(g => g.Count()).Select(g => $"{g.Key.Version} {Describe(g.Key.Arch)} {g.Count()}"));
        text.AppendLine($"설치 대상 {ready.Count}개" + (ready.Count > 0 ? $" ({groups})" : ""));
        if (replacing > 0) text.AppendLine($"그중 교체 {replacing}개 — version.dll·dobby.dll·NOTICE.txt·MelonLoader 폴더만 바꾸고 Mods·UserData 등은 그대로 둡니다.");
        text.AppendLine("각 게임의 실행 파일 옆에 풉니다. 그 밖의 기존 파일은 덮어쓰지 않습니다.");
        text.AppendLine();

        if (releases is not null)
        {
            text.AppendLine("버전 규칙");
            var pick = MelonVersions.Modern.Pick(releases);
            text.AppendLine($"  {MelonVersions.Modern.Label}: {MelonVersions.Modern.Describe()} → {(pick is null ? "해당 릴리스 없음" : pick.Tag)}");
            text.AppendLine($"  {MelonVersions.LegacyLabel}: 자동 설치 안 함");
            text.AppendLine("  MelonLoader 를 지운 게임 (Mods·UserData 등만 남음): 자동 설치 안 함");
            text.AppendLine();
        }

        text.AppendLine(releases is null ? "사용할 zip (직접 고름 — 버전 규칙 미적용)" : "사용할 zip");
        foreach (var package in packages.OrderByDescending(p => p.Version).ThenByDescending(p => p.Arch))
            text.AppendLine($"  {VersionOf(package)} {Describe(package.Arch)}: {package.ZipPath}");
        if (packages.Count == 0) text.AppendLine("  (없음)");

        if (skipped.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"건너뜀 {skipped.Count}개");
            foreach (var group in skipped.GroupBy(Describe).OrderByDescending(g => g.Count()))
            {
                text.AppendLine($"  [{group.Key}] {group.Count()}개");
                foreach (var plan in group)
                {
                    var detail = plan.Skip switch
                    {
                        MelonSkip.AlreadyInstalled => $" ({MelonVersions.Format(plan.Target.InstalledVersion)})",
                        MelonSkip.InstalledMismatch => $" ({MelonVersions.Format(plan.Target.InstalledVersion)} → " +
                            (plan.Package is null ? plan.Target.Requirement?.Describe() ?? "?" : VersionOf(plan.Package)) + ")",
                        _ => "",
                    };
                    text.AppendLine($"    {plan.Game.DisplayName}{detail}");
                }
            }
        }

        if (uncheckedCount > 0)
        {
            text.AppendLine();
            text.AppendLine($"체크 해제로 제외 {uncheckedCount}개");
        }

        if (ready.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("설치 대상 목록");
            foreach (var plan in ready)
            {
                var sub = Path.GetRelativePath(plan.Game.InstallDir, plan.TargetDir!);
                var change = plan.Replaces ? $"  [교체 {MelonVersions.Format(plan.Target.InstalledVersion)} → {VersionOf(plan.Package)}]" : "";
                text.AppendLine($"  {plan.Game.DisplayName} ({VersionOf(plan.Package)} {Describe(plan.Arch)})" +
                                (sub == "." ? "" : $"  →  {sub}") + change);
            }
        }

        return text.ToString();
    }

    private static string EntryName(string fullName) => fullName.Replace('\\', '/');
}
