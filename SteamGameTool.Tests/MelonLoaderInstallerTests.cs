using System.IO.Compression;
using steam_game_tool;

namespace SteamGameTool.Tests;

public class MelonLoaderInstallerTests : IDisposable
{
    private const ushort X86 = 0x014C;
    private const ushort X64 = 0x8664;

    private readonly FakeLibrary _lib = new();
    private readonly string _zipDir = Path.Combine(Path.GetTempPath(), "sgt-tests", "zip-" + Guid.NewGuid().ToString("N"));

    public MelonLoaderInstallerTests() => Directory.CreateDirectory(_zipDir);

    public void Dispose()
    {
        _lib.Dispose();
        try { Directory.Delete(_zipDir, recursive: true); }
        catch { /* 임시 폴더 정리 실패는 테스트 결과와 무관 */ }
    }

    /// <summary>Machine 필드만 채운 최소 PE 헤더.</summary>
    private static byte[] Pe(ushort machine)
    {
        var bytes = new byte[0x48];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x40).CopyTo(bytes, 0x3C);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(0x40));
        BitConverter.GetBytes(machine).CopyTo(bytes, 0x44);
        return bytes;
    }

    /// <summary>실제 배포본과 같은 모양의 zip: 최상위 version.dll + MelonLoader\ + 기타 파일.</summary>
    private string Zip(ushort machine, params string[] extraFiles)
    {
        var path = Path.Combine(_zipDir, $"MelonLoader.{machine:X}.{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var s = zip.CreateEntry("version.dll").Open()) s.Write(Pe(machine));
        zip.CreateEntry("MelonLoader/");
        using (var s = zip.CreateEntry("MelonLoader/net6/MelonLoader.dll").Open()) s.Write([1, 2, 3]);
        foreach (var extra in extraFiles) zip.CreateEntry(extra).Open().Dispose();
        return path;
    }

    private SteamGame Game(string name, ushort machine, params string[] entries)
    {
        var dir = _lib.AddGame(name, entries);
        foreach (var exe in entries.Where(e => e.EndsWith(".exe")))
            File.WriteAllBytes(Path.Combine(dir, exe.Replace('/', Path.DirectorySeparatorChar)), Pe(machine));
        return _lib.Scan().Games.Single(g => g.Name == name);
    }

    [Fact]
    public void PackageArchitectureComesFromVersionDll()
    {
        Assert.Equal(PeArch.X64, MelonLoaderInstaller.OpenPackage(Zip(X64)).Arch);
        Assert.Equal(PeArch.X86, MelonLoaderInstaller.OpenPackage(Zip(X86)).Arch);
    }

    [Fact]
    public void RejectsZipWithoutMelonLoaderLayoutAndDuplicateArchitectures()
    {
        var other = Path.Combine(_zipDir, "other.zip");
        using (var zip = ZipFile.Open(other, ZipArchiveMode.Create)) zip.CreateEntry("readme.txt").Open().Dispose();
        Assert.Throws<InvalidDataException>(() => MelonLoaderInstaller.OpenPackage(other));
        Assert.Throws<InvalidDataException>(() => MelonLoaderInstaller.OpenPackages([Zip(X64), Zip(X64)]));
    }

    [Fact]
    public void InstallsNextToInnerExecutableOfNestedGameWithoutChangingClassification()
    {
        var game = Game("Nested", X64, "Nested/Nested_Data/Managed/Assembly-CSharp.dll", "Nested/Nested.exe");
        var plan = MelonLoaderInstaller.Plan(game, [MelonLoaderInstaller.OpenPackage(Zip(X64))]);

        Assert.Equal(MelonSkip.None, plan.Skip);
        Assert.Equal(Path.Combine(game.InstallDir, "Nested"), plan.TargetDir);

        MelonLoaderInstaller.InstallAll([plan]);
        Assert.True(File.Exists(Path.Combine(game.InstallDir, "Nested", "version.dll")));
        Assert.True(File.Exists(Path.Combine(game.InstallDir, "Nested", "MelonLoader", "net6", "MelonLoader.dll")));
        Assert.False(File.Exists(Path.Combine(game.InstallDir, "version.dll")));

        var rescanned = _lib.Scan().Games.Single(g => g.Name == "Nested");
        Assert.Equal(game.ClassifiedBackend, rescanned.ClassifiedBackend);
        Assert.Equal(game.MainPlayer!.DataPath, rescanned.MainPlayer!.DataPath);
        Assert.Equal(MelonSkip.AlreadyInstalled,
            MelonLoaderInstaller.Plan(rescanned, [MelonLoaderInstaller.OpenPackage(Zip(X64))]).Skip);
    }

    [Fact]
    public void PicksPackageMatchingExecutableArchitecture()
    {
        var packages = MelonLoaderInstaller.OpenPackages([Zip(X64), Zip(X86)]);
        var game = Game("Old", X86, "Old_Data/Managed/Assembly-CSharp.dll", "Old.exe");
        var plan = MelonLoaderInstaller.Plan(game, packages);
        Assert.Equal(PeArch.X86, plan.Package!.Arch);

        var onlyX64 = MelonLoaderInstaller.Plan(game, [MelonLoaderInstaller.OpenPackage(Zip(X64))]);
        Assert.Equal(MelonSkip.NoPackage, onlyX64.Skip);
        Assert.Equal(PeArch.X86, onlyX64.Arch);
    }

    [Theory]
    [InlineData(MelonSkip.AlreadyInstalled, "MelonLoader/")]
    [InlineData(MelonSkip.BepInEx, "BepInEx/")]
    [InlineData(MelonSkip.BepInEx, "doorstop_config.ini")]
    [InlineData(MelonSkip.ForeignVersionDll, "version.dll")]
    [InlineData(MelonSkip.AntiCheat, "EasyAntiCheat/")]
    public void SkipsGamesThatAlreadyHaveSomethingInPlace(MelonSkip expected, string existing)
    {
        var game = Game("Skip", X64, "Skip_Data/Managed/Assembly-CSharp.dll", "Skip.exe", existing);
        var plan = MelonLoaderInstaller.Plan(game, [MelonLoaderInstaller.OpenPackage(Zip(X64))]);
        Assert.Equal(expected, plan.Skip);
        Assert.Empty(MelonLoaderInstaller.InstallAll([plan]));
    }

    [Fact]
    public void SkipsWhenExecutableIsMissingOrNotPe()
    {
        var packages = new[] { MelonLoaderInstaller.OpenPackage(Zip(X64)) };
        var noExe = Game("NoExe", X64, "NoExe_Data/Managed/Assembly-CSharp.dll");
        Assert.Equal(MelonSkip.NoExecutable, MelonLoaderInstaller.Plan(noExe, packages).Skip);

        var emptyExe = _lib.ScanSingle("Empty", "Empty_Data/Managed/Assembly-CSharp.dll", "Empty.exe");
        Assert.Equal(MelonSkip.UnknownArch, MelonLoaderInstaller.Plan(emptyExe, packages).Skip);
    }

    [Fact]
    public void SkipsLowConfidencePlayerSelection()
    {
        var game = Game("Bundle", X64, "_anniversary/Edna_Data/Managed/Assembly-CSharp.dll", "_anniversary/Edna.exe");
        Assert.Equal("low", game.Selection.Confidence);

        var plan = MelonLoaderInstaller.Plan(game, [MelonLoaderInstaller.OpenPackage(Zip(X64))]);
        Assert.Equal(MelonSkip.UncertainPlayer, plan.Skip);
        Assert.Empty(MelonLoaderInstaller.InstallAll([plan]));
        Assert.False(File.Exists(Path.Combine(game.InstallDir, "_anniversary", "version.dll")));
    }

    [Fact]
    public void NeverOverwritesExistingFileNamedLikeZipEntry()
    {
        var game = Game("Conflict", X64, "Conflict_Data/Managed/Assembly-CSharp.dll", "Conflict.exe", "NOTICE.txt");
        File.WriteAllText(Path.Combine(game.InstallDir, "NOTICE.txt"), "mine");
        var plan = MelonLoaderInstaller.Plan(game, [MelonLoaderInstaller.OpenPackage(Zip(X64, "NOTICE.txt"))]);

        Assert.Equal(MelonSkip.FileConflict, plan.Skip);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(game.InstallDir, "NOTICE.txt")));
    }

    // ── 자동 설치 · 교체 ──────────────────────────────────────────────

    /// <summary>
    /// 버전 정보가 있는 진짜 DLL. 테스트 어셈블리 자신(1.0.0)을 "설치된 MelonLoader.dll"로 쓴다.
    /// 1.0.0 은 Modern(0.7.3 이상)을 만족한다. 규칙과 다른 설치는 <see cref="Strict"/> 규칙으로 흉내 낸다.
    /// </summary>
    private static readonly string VersionedDll = typeof(MelonLoaderInstallerTests).Assembly.Location;

    /// <summary>옛 MelonLoader 설치 흉내: version.dll + MelonLoader\net35\MelonLoader.dll + 표식 파일 + 사용자 모드.</summary>
    private static void FakeInstall(string targetDir)
    {
        Directory.CreateDirectory(Path.Combine(targetDir, "MelonLoader", "net35"));
        File.Copy(VersionedDll, Path.Combine(targetDir, "MelonLoader", "net35", "MelonLoader.dll"));
        File.WriteAllText(Path.Combine(targetDir, "MelonLoader", "old-marker.txt"), "old");
        File.WriteAllText(Path.Combine(targetDir, "version.dll"), "old proxy");
        Directory.CreateDirectory(Path.Combine(targetDir, "Mods"));
        File.WriteAllText(Path.Combine(targetDir, "Mods", "MyMod.dll"), "mine");
    }

    private static MelonRelease Release(string tag, params PeArch[] archs) =>
        new(tag, MelonVersions.Parse(tag)!, false,
            archs.ToDictionary(a => a, a => new MelonAsset($"MelonLoader.{a}.zip", "https://example/" + tag, 1, null)));

    /// <summary>1.0.0(테스트 DLL)이 만족하지 못하는 시험용 규칙. 「다른 버전이 설치됨」과 교체를 흉내 낸다.</summary>
    private static readonly MelonRequirement Strict = new("시험 규칙", null, new Version(2, 0, 0));
    private static readonly Version StrictVersion = new(2, 0, 0);

    private static readonly MelonRelease[] Releases =
        [Release("v2.0.0", PeArch.X64, PeArch.X86), Release("v0.7.3", PeArch.X64, PeArch.X86), Release("v0.7.1", PeArch.X64, PeArch.X86)];

    private SteamGame Legacy(string name) => Game(name, X64, $"{name}_Data/Managed/Assembly-CSharp.dll", $"{name}_Data/Mono/", $"{name}.exe");
    private SteamGame Modern(string name) => Game(name, X64, $"{name}_Data/Managed/Assembly-CSharp.dll", "MonoBleedingEdge/", $"{name}.exe");

    /// <summary>MelonLoader(1.0.0)가 설치된 MBE 게임을 <see cref="Strict"/> 규칙으로 살핀 결과 = 규칙과 다른 설치.</summary>
    private MelonTarget Mismatched(string name)
    {
        var game = Modern(name);
        FakeInstall(game.InstallDir);
        return MelonLoaderInstaller.Inspect(game) with { Requirement = Strict };
    }

    [Fact]
    public void ReadsPackageVersionFromMelonLoaderDllInsideZip()
    {
        var path = Path.Combine(_zipDir, "real-dll.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var s = zip.CreateEntry("version.dll").Open()) s.Write(Pe(X64));
            zip.CreateEntryFromFile(VersionedDll, "MelonLoader/net35/MelonLoader.dll");
        }
        Assert.Equal(MelonVersions.FromFile(VersionedDll), MelonLoaderInstaller.OpenPackage(path).Version);
        Assert.NotNull(MelonLoaderInstaller.OpenPackage(path).Version);
    }

    [Fact]
    public void AutoInstallCoversOnlyCychronicleLikeGamesAndSkipsLegacy()
    {
        var cup = Legacy("Cup");
        var gv = Modern("Gv");
        var il = Game("Il", X64, "Il_Data/il2cpp_data/Metadata/global-metadata.dat", "GameAssembly.dll", "UnityPlayer.dll", "Il.exe");
        var targets = new[] { cup, gv, il }.Select(MelonLoaderInstaller.Inspect).ToList();

        // 0.7.3 이상 중 최신 정식 릴리스(여기서는 v2.0.0)만 받는다. 구형 레거시 몫은 받지 않는다.
        var downloads = MelonLoaderInstaller.RequiredDownloads(targets, Releases, replace: true);
        Assert.Equal(["v2.0.0 X64"], downloads.Select(d => $"{d.Release.Tag} {d.Arch}"));

        var plans = MelonLoaderInstaller.PlanAuto(targets, Releases,
            [MelonLoaderInstaller.OpenPackage(Zip(X64), StrictVersion)], replace: true);
        Assert.Equal(MelonSkip.NotAutoTarget, plans.Single(p => p.Game == cup).Skip);
        Assert.Equal(StrictVersion, plans.Single(p => p.Game == gv).Package!.Version);
        Assert.Equal(StrictVersion, plans.Single(p => p.Game == il).Package!.Version);
        Assert.Equal(MelonSkip.None, plans.Single(p => p.Game == il).Skip);
    }

    [Fact]
    public void AutoInstallLeavesExistingLegacyInstallAlone()
    {
        var cup = Legacy("Cup");
        FakeInstall(cup.InstallDir);
        var target = MelonLoaderInstaller.Inspect(cup);
        Assert.Null(target.Requirement);

        Assert.Empty(MelonLoaderInstaller.RequiredDownloads([target], Releases, replace: true));
        var plan = MelonLoaderInstaller.PlanAuto([target], Releases, [], replace: true).Single();
        Assert.Equal(MelonSkip.NotAutoTarget, plan.Skip);
        Assert.Empty(MelonLoaderInstaller.InstallAll([plan]));
        Assert.Equal("old", File.ReadAllText(Path.Combine(cup.InstallDir, "MelonLoader", "old-marker.txt")));
    }

    [Theory]
    [InlineData("UserLibs")]
    [InlineData("UserData", "Mods", "Plugins")]
    public void AutoInstallSkipsGamesWhereUserRemovedMelonLoader(params string[] leftovers)
    {
        var gv = Modern("Gv");
        foreach (var dir in leftovers) Directory.CreateDirectory(Path.Combine(gv.InstallDir, dir));
        var target = MelonLoaderInstaller.Inspect(gv);
        Assert.True(target.WasRemoved);

        Assert.Empty(MelonLoaderInstaller.RequiredDownloads([target], Releases, replace: true));
        var plan = MelonLoaderInstaller.PlanAuto([target], Releases,
            [MelonLoaderInstaller.OpenPackage(Zip(X64), StrictVersion)], replace: true).Single();
        Assert.Equal(MelonSkip.RemovedByUser, plan.Skip);
        Assert.Empty(MelonLoaderInstaller.InstallAll([plan]));
        Assert.False(File.Exists(Path.Combine(gv.InstallDir, "version.dll")));

        // 직접 고른 zip 으로는 설치할 수 있다.
        var manual = MelonLoaderInstaller.Plan(gv, [MelonLoaderInstaller.OpenPackage(Zip(X64), StrictVersion)]);
        Assert.Equal(MelonSkip.None, manual.Skip);
    }

    [Fact]
    public void GameOwnModsFolderAloneIsNotTreatedAsRemovedMelonLoader()
    {
        var gv = Modern("Gv");
        Directory.CreateDirectory(Path.Combine(gv.InstallDir, "Mods"));
        Directory.CreateDirectory(Path.Combine(gv.InstallDir, "UserData"));
        Assert.False(MelonLoaderInstaller.Inspect(gv).WasRemoved);
    }

    [Fact]
    public void InstalledGameIsNotTreatedAsRemoved()
    {
        var gv = Modern("Gv");
        FakeInstall(gv.InstallDir);
        Directory.CreateDirectory(Path.Combine(gv.InstallDir, "UserLibs"));
        Assert.False(MelonLoaderInstaller.Inspect(gv).WasRemoved);
    }

    [Fact]
    public void ManualZipStillInstallsOnLegacyGames()
    {
        var cup = Legacy("Cup");
        var plan = MelonLoaderInstaller.Plan(cup, [MelonLoaderInstaller.OpenPackage(Zip(X64), new Version(0, 7, 1))]);
        Assert.Equal(MelonSkip.None, plan.Skip);
        Assert.Null(Assert.Single(MelonLoaderInstaller.InstallAll([plan])).Error);
        Assert.True(File.Exists(Path.Combine(cup.InstallDir, "version.dll")));
    }

    [Fact]
    public void InstalledVersionIsKeptWhenItSatisfiesTheRule()
    {
        var gv = Modern("Gv");
        FakeInstall(gv.InstallDir);
        var target = MelonLoaderInstaller.Inspect(gv);
        Assert.True(target.HasInstall);
        Assert.NotNull(target.InstalledVersion);

        // 1.0.0 은 0.7.3 이상이므로 교체를 켜도 받지도, 바꾸지도 않는다.
        Assert.Empty(MelonLoaderInstaller.RequiredDownloads([target], Releases, replace: true));
        var plan = Assert.Single(MelonLoaderInstaller.PlanAuto([target], Releases, [], replace: true));
        Assert.Equal(MelonSkip.AlreadyInstalled, plan.Skip);
    }

    [Fact]
    public void MismatchedInstallIsReportedAndOnlyReplacedWhenAsked()
    {
        var target = Mismatched("Gv");
        var package = MelonLoaderInstaller.OpenPackage(Zip(X64), StrictVersion);

        Assert.Empty(MelonLoaderInstaller.RequiredDownloads([target], Releases, replace: false));
        Assert.Single(MelonLoaderInstaller.RequiredDownloads([target], Releases, replace: true));
        Assert.Equal(MelonSkip.InstalledMismatch, MelonLoaderInstaller.PlanAuto([target], Releases, [package], replace: false).Single().Skip);

        var plan = MelonLoaderInstaller.PlanAuto([target], Releases, [package], replace: true).Single();
        Assert.Equal(MelonSkip.None, plan.Skip);
        Assert.True(plan.Replaces);

        var outcome = Assert.Single(MelonLoaderInstaller.InstallAll([plan]));
        Assert.Null(outcome.Error);
        var dir = target.Game.InstallDir;
        Assert.Equal(Pe(X64), File.ReadAllBytes(Path.Combine(dir, "version.dll")));
        Assert.False(File.Exists(Path.Combine(dir, "MelonLoader", "old-marker.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "MelonLoader", "net6", "MelonLoader.dll")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(dir, "Mods", "MyMod.dll")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir, "*.replaced-*"));
    }

    [Fact]
    public void FailedReplacementRestoresPreviousInstall()
    {
        var target = Mismatched("Gv");
        var package = MelonLoaderInstaller.OpenPackage(Zip(X64, "zz/late.txt"), StrictVersion);
        var plan = MelonLoaderInstaller.PlanAuto([target], Releases, [package], replace: true).Single();
        Assert.True(plan.Replaces);
        var late = Path.Combine(target.Game.InstallDir, "zz", "late.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(late)!);
        File.WriteAllText(late, "mine");

        var outcome = Assert.Single(MelonLoaderInstaller.InstallAll([plan]));
        Assert.NotNull(outcome.Error);
        var dir = target.Game.InstallDir;
        Assert.Equal("old proxy", File.ReadAllText(Path.Combine(dir, "version.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(dir, "MelonLoader", "old-marker.txt")));
        Assert.False(Directory.Exists(Path.Combine(dir, "MelonLoader", "net6")));
        Assert.Equal("mine", File.ReadAllText(late));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir, "*.replaced-*"));
    }

    [Fact]
    public void ReplaceNeverTouchesVersionDllWithoutMelonLoaderFolder()
    {
        var gv = Game("Gv", X64, "Gv_Data/Managed/Assembly-CSharp.dll", "MonoBleedingEdge/", "Gv.exe", "version.dll");
        var plan = MelonLoaderInstaller.PlanAuto([MelonLoaderInstaller.Inspect(gv)], Releases,
            [MelonLoaderInstaller.OpenPackage(Zip(X64), StrictVersion)], replace: true).Single();
        Assert.Equal(MelonSkip.ForeignVersionDll, plan.Skip);
    }

    [Fact]
    public void ReplacingOld06StyleInstallClearsItsRootNoticeAndDobby()
    {
        var target = Mismatched("Gv");
        var dir = target.Game.InstallDir;
        File.WriteAllText(Path.Combine(dir, "NOTICE.txt"), "0.6 notice");
        File.WriteAllText(Path.Combine(dir, "dobby.dll"), "0.6 dobby");

        // 0.6.x zip 처럼 NOTICE.txt 가 들어 있어도, 교체로 치울 파일이라 「같은 이름 있음」이 아니다.
        var withNotice = MelonLoaderInstaller.OpenPackage(Zip(X64, "NOTICE.txt"), StrictVersion);
        Assert.Equal(MelonSkip.None, MelonLoaderInstaller.PlanAuto([target], Releases, [withNotice], replace: true).Single().Skip);

        // 0.7.x zip 에는 없다. 교체하면 옛 NOTICE.txt·dobby.dll 이 남지 않는다.
        var plan = MelonLoaderInstaller.PlanAuto([target], Releases,
            [MelonLoaderInstaller.OpenPackage(Zip(X64), StrictVersion)], replace: true).Single();
        Assert.Null(Assert.Single(MelonLoaderInstaller.InstallAll([plan])).Error);
        Assert.False(File.Exists(Path.Combine(dir, "NOTICE.txt")));
        Assert.False(File.Exists(Path.Combine(dir, "dobby.dll")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir, "*.replaced-*"));
    }

    [Fact]
    public void ManualZipReplacesOnlyDifferentVersionWhenAsked()
    {
        var gv = Modern("Gv");
        FakeInstall(gv.InstallDir);
        var installed = MelonVersions.FromFile(VersionedDll)!;
        var same = MelonLoaderInstaller.OpenPackage(Zip(X64), installed);
        var other = MelonLoaderInstaller.OpenPackage(Zip(X64), new Version(0, 7, 3));

        Assert.Equal(MelonSkip.AlreadyInstalled, MelonLoaderInstaller.Plan(gv, [same], replace: true).Skip);
        Assert.Equal(MelonSkip.InstalledMismatch, MelonLoaderInstaller.Plan(gv, [other], replace: false).Skip);
        Assert.True(MelonLoaderInstaller.Plan(gv, [other], replace: true).Replaces);
    }

    [Fact]
    public void CreatesModFoldersAfterSuccessfulInstall()
    {
        var gv = Modern("Gv");
        var plan = MelonLoaderInstaller.Plan(gv, [MelonLoaderInstaller.OpenPackage(Zip(X64))]);
        Assert.Null(Assert.Single(MelonLoaderInstaller.InstallAll([plan])).Error);
        foreach (var folder in new[] { "Mods", "Plugins", "UserData" })
            Assert.True(Directory.Exists(Path.Combine(gv.InstallDir, folder)), folder);
    }

    [Fact]
    public void FailedInstallRemovesOnlyWhatItCreated()
    {
        // 계획 뒤에 같은 이름의 파일이 생겨 설치 도중 실패하는 경우.
        var game = Game("Race", X64, "Race_Data/Managed/Assembly-CSharp.dll", "Race.exe");
        var plan = MelonLoaderInstaller.Plan(game, [MelonLoaderInstaller.OpenPackage(Zip(X64, "zz/late.txt"))]);
        Assert.Equal(MelonSkip.None, plan.Skip);
        var late = Path.Combine(game.InstallDir, "zz", "late.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(late)!);
        File.WriteAllText(late, "mine");

        var outcome = Assert.Single(MelonLoaderInstaller.InstallAll([plan]));
        Assert.NotNull(outcome.Error);
        Assert.False(File.Exists(Path.Combine(game.InstallDir, "version.dll")));
        Assert.False(Directory.Exists(Path.Combine(game.InstallDir, "MelonLoader")));
        Assert.Equal("mine", File.ReadAllText(late));
        Assert.True(File.Exists(Path.Combine(game.InstallDir, "Race.exe")));
    }
}
