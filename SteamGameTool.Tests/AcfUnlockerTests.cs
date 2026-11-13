using System.Text;
using steam_game_tool;

namespace SteamGameTool.Tests;

public class AcfUnlockerTests
{
    [Fact]
    public void 읽기전용과_AutoUpdateBehavior_잠금을_해제하고_탭_포맷을_유지한다()
    {
        using var lib = new FakeSteamLibrary();
        var steamapps = Path.Combine(lib.LibraryRoot, "steamapps");
        var acfPath = Path.Combine(steamapps, "appmanifest_367520.acf");
        const string lockedAcf =
            "\"AppState\"\n" +
            "{\n" +
            "\t\"appid\"\t\t\"367520\"\n" +
            "\t\"Universe\"\t\t\"1\"\n" +
            "\t\"LauncherPath\"\t\t\"H:\\\\steam\\\\steam.exe\"\n" +
            "\t\"name\"\t\t\"Hollow Knight\"\n" +
            "\t\"StateFlags\"\t\t\"4\"\n" +
            "\t\"installdir\"\t\t\"Hollow Knight\"\n" +
            "\t\"buildid\"\t\t\"123456\"\n" +
            "\t\"AutoUpdateBehavior\"\t\t\"1\"\n" +
            "}\n";
        File.WriteAllText(acfPath, lockedAcf, new UTF8Encoding(false));
        File.SetAttributes(acfPath, File.GetAttributes(acfPath) | FileAttributes.ReadOnly);

        var targets = AcfUnlocker.InspectAll([lib.LibraryRoot]);
        var target = Assert.Single(targets);
        Assert.True(target.WasReadOnly);
        Assert.True(target.IsAutoUpdateLocked);
        Assert.False(target.HasCorruptedLauncherPath);
        Assert.True(target.NeedsUnlock);

        var outcomes = AcfUnlocker.UnlockAll(targets);
        var outcome = Assert.Single(outcomes);
        Assert.Null(outcome.Error);

        Assert.Equal(0, (int)(File.GetAttributes(acfPath) & FileAttributes.ReadOnly));
        var unlockedText = File.ReadAllText(acfPath, Encoding.UTF8);
        Assert.Contains("\t\"AutoUpdateBehavior\"\t\t\"0\"\n", unlockedText);
        Assert.Contains("\t\"LauncherPath\"\t\t\"H:\\\\steam\\\\steam.exe\"\n", unlockedText);
    }

    [Fact]
    public void 백업파일이_있으면_가장_오래된_원본으로_포맷과_LauncherPath를_복원하고_bak을_지운다()
    {
        using var lib = new FakeSteamLibrary();
        var steamapps = Path.Combine(lib.LibraryRoot, "steamapps");
        var acfPath = Path.Combine(steamapps, "appmanifest_1003590.acf");

        const string originalBak =
            "\"AppState\"\n" +
            "{\n" +
            "\t\"appid\"\t\t\"1003590\"\n" +
            "\t\"LauncherPath\"\t\t\"H:\\\\steam\\\\steam.exe\"\n" +
            "\t\"name\"\t\t\"Tetris Effect: Connected\"\n" +
            "\t\"StateFlags\"\t\t\"4\"\n" +
            "\t\"installdir\"\t\t\"Tetris Effect Connected\"\n" +
            "\t\"buildid\"\t\t\"24070382\"\n" +
            "\t\"AutoUpdateBehavior\"\t\t\"0\"\n" +
            "}\n";

        const string corruptedAcf =
            "\"AppState\"\r\n" +
            "{\r\n" +
            "\t\"appid\" \"1003590\"\r\n" +
            "\t\"LauncherPath\" \"H:steamsteam.exe\"\r\n" +
            "\t\"name\" \"Tetris Effect: Connected\"\r\n" +
            "\t\"StateFlags\" \"4\"\r\n" +
            "\t\"installdir\" \"Tetris Effect Connected\"\r\n" +
            "\t\"buildid\" \"24070382\"\r\n" +
            "\t\"AutoUpdateBehavior\" \"1\"\r\n" +
            "}\r\n";

        var bak1 = acfPath + ".bak_20261015_223629";
        var bak2 = acfPath + ".bak_20261015_223709";
        File.WriteAllText(bak1, originalBak, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(bak1, new DateTime(2026, 7, 16, 1, 16, 0, DateTimeKind.Utc));
        File.WriteAllText(bak2, corruptedAcf, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(bak2, new DateTime(2026, 10, 15, 13, 36, 0, DateTimeKind.Utc));
        File.WriteAllText(acfPath, corruptedAcf, new UTF8Encoding(false));

        var targets = AcfUnlocker.InspectAll([Path.Combine(steamapps, "common")]);
        var target = Assert.Single(targets);
        Assert.True(target.IsAutoUpdateLocked);
        Assert.True(target.HasCorruptedLauncherPath);
        Assert.Equal(2, target.BackupFiles.Count);

        var outcomes = AcfUnlocker.UnlockAll(targets);
        var outcome = Assert.Single(outcomes);
        Assert.Null(outcome.Error);
        Assert.True(outcome.RestoredFromBackup);
        Assert.Equal(2, outcome.RemovedBackupCount);

        Assert.Equal(originalBak, File.ReadAllText(acfPath, Encoding.UTF8));
        Assert.Empty(Directory.GetFiles(steamapps, "*.bak*"));
    }

    [Fact]
    public void 이미_정상인_ACF는_수정하지_않고_건너뛴다()
    {
        using var lib = new FakeSteamLibrary();
        var steamapps = Path.Combine(lib.LibraryRoot, "steamapps");
        var acfPath = Path.Combine(steamapps, "appmanifest_10.acf");
        const string cleanAcf =
            "\"AppState\"\n" +
            "{\n" +
            "\t\"appid\"\t\t\"10\"\n" +
            "\t\"LauncherPath\"\t\t\"H:\\\\steam\\\\steam.exe\"\n" +
            "\t\"name\"\t\t\"Clean Game\"\n" +
            "\t\"StateFlags\"\t\t\"4\"\n" +
            "\t\"installdir\"\t\t\"CleanGame\"\n" +
            "\t\"AutoUpdateBehavior\"\t\t\"0\"\n" +
            "}\n";
        File.WriteAllText(acfPath, cleanAcf, new UTF8Encoding(false));

        var targets = AcfUnlocker.InspectAll([lib.LibraryRoot]);
        var target = Assert.Single(targets);
        Assert.False(target.NeedsUnlock);
        Assert.Empty(AcfUnlocker.UnlockAll(targets));
    }
}
