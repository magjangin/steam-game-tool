using System.Security.Cryptography;
using System.Text;
using steam_game_tool;

namespace SteamGameTool.Tests;

/// <summary>
/// 이전 스캔 대비 누락 검사. 검사에 걸려도 스캔은 계속되고, 걸린 폴더는 해결될 때까지 매번 다시 알린다.
/// 기준선은 임시 폴더에 둔다 — 실제 %LOCALAPPDATA% 의 기준선은 건드리지 않는다.
/// </summary>
public class ScanInventoryTests
{
    [Fact]
    public void LostUnityIsReportedOnEveryScanUntilTheLeftoverFolderIsDeleted()
    {
        using var lib = new FakeLibrary();
        var baselines = Directory.CreateTempSubdirectory("sgt-baseline-").FullName;
        try
        {
            var game = lib.AddGame("Foo", "Foo_Data/Managed/Assembly-CSharp.dll", "Foo.exe");
            Assert.True(ScanInventory.CheckPersistent(lib.Scan(), baselines).IsClean);

            // Steam 에서 게임을 지웠는데 MelonLoader·Mods 때문에 폴더가 남은 상태.
            Directory.Delete(Path.Combine(game, "Foo_Data"), recursive: true);
            File.Delete(Path.Combine(game, "Foo.exe"));
            Directory.CreateDirectory(Path.Combine(game, "MelonLoader"));
            Directory.CreateDirectory(Path.Combine(game, "Mods"));

            for (var i = 0; i < 2; i++)
            {
                var check = ScanInventory.CheckPersistent(lib.Scan(), baselines);
                Assert.Equal([game], check.LostUnity);
                Assert.Empty(check.MissingFolders);
            }

            Directory.Delete(game, recursive: true);
            Assert.True(ScanInventory.CheckPersistent(lib.Scan(), baselines).IsClean);
        }
        finally
        {
            Directory.Delete(baselines, recursive: true);
        }
    }

    [Fact]
    public void UnreadableBaselineIsReplacedInsteadOfFailingTheScan()
    {
        using var lib = new FakeLibrary();
        var baselines = Directory.CreateTempSubdirectory("sgt-baseline-").FullName;
        try
        {
            lib.AddGame("Foo", "Foo_Data/Managed/Assembly-CSharp.dll");
            var result = lib.Scan();
            ScanInventory.CheckPersistent(result, baselines);
            var file = Assert.Single(Directory.GetFiles(baselines));
            File.WriteAllText(file, "{ not json");

            Assert.True(ScanInventory.CheckPersistent(result, baselines).IsClean);
            Assert.Contains("Foo", File.ReadAllText(file));
        }
        finally
        {
            Directory.Delete(baselines, recursive: true);
        }
    }

    [Fact]
    public void BaselineFileNameStaysCompatibleWithExistingBaselines()
    {
        using var lib = new FakeLibrary();
        var baselines = Directory.CreateTempSubdirectory("sgt-baseline-").FullName;
        try
        {
            var result = lib.Scan();
            ScanInventory.CheckPersistent(result, baselines);

            // 예전 ValidatePersistent 와 같은 계산: 정렬한 루트를 줄 바꿈으로 잇고 대문자로 바꾼 SHA-256.
            var roots = string.Join("\n", result.Roots.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)).ToUpperInvariant();
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(roots))) + ".json";
            Assert.Equal(expected, Path.GetFileName(Assert.Single(Directory.GetFiles(baselines))));
        }
        finally
        {
            Directory.Delete(baselines, recursive: true);
        }
    }

    [Fact]
    public void FoldersActuallyDeletedFromDiskAreNotProblems()
    {
        var previous = new ScanInventory.Baseline(["kept", "deleted"], ["kept", "deleted"]);
        var current = new ScanInventory.Baseline([], []);
        var check = ScanInventory.Check(previous, current, p => p == "kept");
        Assert.Equal(["kept"], check.MissingFolders);
        Assert.Equal(["kept"], check.LostUnity);
    }

    [Theory]
    [InlineData(@"D:\Games, Old\steamapps\common", new[] { @"D:\Games, Old\steamapps\common" })]
    [InlineData(@"C:\a; D:\b;;  ", new[] { @"C:\a", @"D:\b" })]
    [InlineData("\"C:\\quoted path\"\r\nD:\\b\nc:\\A;C:\\a", new[] { @"C:\quoted path", @"D:\b", @"c:\A" })]
    [InlineData("   ", new string[0])]
    public void RootListSplitsOnlyOnSemicolonsAndLineBreaks(string text, string[] expected)
    {
        Assert.Equal(expected, SteamLibraries.SplitRootList(text));
    }
}
