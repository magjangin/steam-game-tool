using steam_game_tool;

namespace SteamGameTool.Tests;

/// <summary>
/// 임시 폴더에 가짜 steamapps\common 을 만들어 주는 테스트 픽스처.
/// 실제 파일시스템을 쓰므로 SteamScanner 를 손대지 않고 그대로 검증할 수 있다.
/// </summary>
public sealed class FakeLibrary : IDisposable
{
    private readonly string _root;

    /// <summary>SteamScanner.Scan 에 넘길 common 폴더 경로.</summary>
    public string CommonDir { get; }

    public FakeLibrary()
    {
        _root = Path.Combine(Path.GetTempPath(), "sgt-tests", Guid.NewGuid().ToString("N"));
        CommonDir = Path.Combine(_root, "steamapps", "common");
        Directory.CreateDirectory(CommonDir);
    }

    /// <summary>
    /// 게임 폴더를 만들고 그 아래에 항목들을 생성한다.
    /// '/' 로 끝나는 항목은 폴더, 나머지는 빈 파일로 만든다.
    /// </summary>
    /// <example>
    /// AddGame("Foo", "foo_Data/Managed/Assembly-CSharp.dll", "MonoBleedingEdge/")
    /// </example>
    public string AddGame(string name, params string[] entries)
    {
        var gameDir = Path.Combine(CommonDir, name);
        Directory.CreateDirectory(gameDir);

        foreach (var entry in entries)
        {
            var isDirectory = entry.EndsWith('/');
            var relative = entry.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar);
            var full = Path.Combine(gameDir, relative);

            if (isDirectory)
            {
                Directory.CreateDirectory(full);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, []);
            }
        }

        return gameDir;
    }

    /// <summary>이 라이브러리만 스캔한다.</summary>
    public ScanResult Scan() => SteamScanner.Scan([CommonDir]);

    /// <summary>게임 하나만 만들고 바로 스캔해 그 결과를 돌려주는 단축 헬퍼.</summary>
    public SteamGame ScanSingle(string name, params string[] entries)
    {
        AddGame(name, entries);
        return Scan().Games.Single(g => g.Name == name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* 임시 폴더 정리 실패는 테스트 결과와 무관 */ }
    }
}
