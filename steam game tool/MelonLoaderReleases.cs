using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace steam_game_tool;

/// <summary>GitHub 릴리스의 Windows 배포 zip 하나.</summary>
/// <param name="Sha512Url">같은 릴리스의 <c>MelonLoader.x64.sha512</c> 등. v0.7.1 이후에는 없다.</param>
public sealed record MelonAsset(string Name, string Url, long Size, string? Sha512Url);

/// <summary>MelonLoader GitHub 릴리스 하나. <see cref="Zips"/> 에는 x64·x86 배포 zip 만 담는다.</summary>
public sealed record MelonRelease(string Tag, Version Version, bool Prerelease, IReadOnlyDictionary<PeArch, MelonAsset> Zips);

/// <summary>
/// 게임이 받아야 할 MelonLoader 버전.
/// <see cref="Exact"/> 가 있으면 그 버전만, 없으면 <see cref="Minimum"/> 이상인 정식 릴리스 중 최신을 쓴다.
/// </summary>
public sealed record MelonRequirement(string Label, Version? Exact, Version? Minimum)
{
    /// <summary>
    /// 최소 버전 규칙은 <b>정식 릴리스</b>만 만족한다. CI 빌드는 번호가 더 높아도(0.8.0-ci.2548) 교체 대상이다.
    /// 사용자가 모든 게임을 정식판으로 맞추기로 했다(2026-09-21, 「5개 모두 0.7.3」).
    /// </summary>
    public bool IsSatisfiedBy(Version? installed) =>
        installed is not null && (Exact is not null
            ? MelonVersions.SameRelease(installed, Exact)
            : !MelonVersions.IsCiBuild(installed) && MelonVersions.AtLeast(installed, Minimum!));

    public MelonRelease? Pick(IEnumerable<MelonRelease> releases) => Exact is not null
        ? releases.Where(r => MelonVersions.SameRelease(r.Version, Exact)).OrderBy(r => r.Prerelease).FirstOrDefault()
        : releases.Where(r => !r.Prerelease && MelonVersions.AtLeast(r.Version, Minimum!)).MaxBy(r => r.Version);

    public string Describe() => Exact is not null ? MelonVersions.Format(Exact) : $"{MelonVersions.Format(Minimum!)} 이상 중 최신";
}

public static class MelonVersions
{
    /// <summary>
    /// 구형 레거시 Mono(Cuphead 등)는 자동 설치하지 않는다 (사용자 결정, 2026-09-21).
    /// 이 분류에는 LiEat 처럼 Unity 가 런처뿐이고 본편은 WOLF RPG 인 게임도 섞여 있다.
    /// 「zip 직접 선택…」으로는 여전히 설치할 수 있다.
    /// </summary>
    public const string LegacyLabel = "구형 레거시 Mono (Cuphead 등)";

    /// <summary>GUNVOLT RECORDS Cychronicle(Unity 2021.3, MonoBleedingEdge)은 0.7.3 이상이어야 한다.</summary>
    public static readonly MelonRequirement Modern = new("MonoBleedingEdge·IL2CPP (GUNVOLT RECORDS Cychronicle 등)", null, new Version(0, 7, 3));

    /// <summary>자동 설치 규칙. 구형 레거시 Mono 는 null — 자동 설치 대상이 아니다.</summary>
    public static MelonRequirement? For(SteamGame game) => game.HasLegacyMono ? null : Modern;

    /// <summary>"v0.7.3", "0.7.1+0a69…", "0.8.0-ci.2548" 같은 문자열에서 숫자 부분만 읽는다.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().TrimStart('v', 'V');
        var end = s.IndexOfAny(['+', '-', ' ']);
        if (end >= 0) s = s[..end];
        return Version.TryParse(s, out var v) && v != new Version(0, 0, 0, 0) ? v : null;
    }

    /// <summary>MelonLoader.dll 의 ProductVersion, 비어 있으면 FileVersion.</summary>
    public static Version? FromFile(string dllPath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(dllPath);
            return Parse(info.ProductVersion) ?? Parse(info.FileVersion);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>대상 폴더에 설치된 MelonLoader 버전. 폴더가 없거나 읽지 못하면 null.</summary>
    public static Version? Installed(string targetDir)
    {
        var root = Path.Combine(targetDir, "MelonLoader");
        if (!Directory.Exists(root)) return null;
        // 0.5 는 MelonLoader\ 바로 아래, 0.6 이후는 net35\·net6\ 아래에 있다.
        foreach (var candidate in new[] { "MelonLoader.dll", @"net35\MelonLoader.dll", @"net6\MelonLoader.dll" })
        {
            var path = Path.Combine(root, candidate);
            if (File.Exists(path)) return FromFile(path);
        }
        try
        {
            var any = Directory.EnumerateFiles(root, "MelonLoader.dll", SearchOption.AllDirectories).FirstOrDefault();
            return any is null ? null : FromFile(any);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>주·부·빌드 번호가 같은가. nightly(0.7.1.xxxx)도 같은 릴리스로 본다.</summary>
    public static bool SameRelease(Version a, Version b) =>
        a.Major == b.Major && a.Minor == b.Minor && Math.Max(a.Build, 0) == Math.Max(b.Build, 0);

    /// <summary>
    /// CI(nightly) 빌드인가. 정식 릴리스의 DLL 은 네 번째 자리가 0 이고(0.7.3.0), CI 빌드는 빌드 번호다
    /// (0.7.3.2497 = MelonLoader 로그의 "v0.7.3-ci.2497 Open-Beta").
    /// </summary>
    public static bool IsCiBuild(Version v) => v.Revision > 0;

    /// <summary>
    /// installed 가 minimum 이상인가. CI 빌드는 같은 번호 정식 릴리스의 시험판이라 그보다 낮다:
    /// 0.7.3-ci.2497 &lt; 0.7.3 &lt; 0.8.0-ci.2548. Version 을 그대로 비교하면 0.7.3.2497 이 0.7.3 보다 높게 나와서,
    /// 정식 0.7.3 보다 먼저 나온 CI 빌드(Halchemist 등 46개, 2026-09-21)가 교체되지 않았다.
    /// </summary>
    public static bool AtLeast(Version installed, Version minimum)
    {
        var release = new Version(installed.Major, installed.Minor, Math.Max(installed.Build, 0));
        var required = new Version(minimum.Major, minimum.Minor, Math.Max(minimum.Build, 0));
        if (release != required) return release > required;
        if (IsCiBuild(installed) != IsCiBuild(minimum)) return !IsCiBuild(installed);
        return Math.Max(installed.Revision, 0) >= Math.Max(minimum.Revision, 0);
    }

    /// <summary>네 자리까지 같은가. 없는 자리는 0 으로 본다.</summary>
    public static bool Same(Version a, Version b) =>
        SameRelease(a, b) && Math.Max(a.Revision, 0) == Math.Max(b.Revision, 0);

    /// <summary>CI 빌드는 MelonLoader 로그와 같은 "0.7.3-ci.2497" 로 쓴다. 정식 릴리스와 헷갈리지 않게 한다.</summary>
    public static string Format(Version? v) => v is null ? "버전 모름" :
        IsCiBuild(v) ? $"{v.ToString(3)}-ci.{v.Revision}" : v.Build >= 0 ? v.ToString(3) : v.ToString(2);

    /// <summary>GitHub releases API 응답을 읽는다. 태그를 버전으로 읽지 못한 릴리스는 뺀다.</summary>
    public static List<MelonRelease> ParseReleases(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var releases = new List<MelonRelease>();
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            var version = Parse(tag);
            if (version is null) continue;

            var assets = release.GetProperty("assets").EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? "",
                              Url: a.GetProperty("browser_download_url").GetString() ?? "",
                              Size: a.GetProperty("size").GetInt64()))
                .ToList();
            string? Sha(string zipName) =>
                assets.FirstOrDefault(a => a.Name.Equals(Path.ChangeExtension(zipName, ".sha512"), StringComparison.OrdinalIgnoreCase)).Url;

            var zips = new Dictionary<PeArch, MelonAsset>();
            foreach (var (arch, name) in new[] { (PeArch.X64, "MelonLoader.x64.zip"), (PeArch.X86, "MelonLoader.x86.zip") })
            {
                var asset = assets.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (asset.Url is { Length: > 0 }) zips[arch] = new MelonAsset(asset.Name, asset.Url, asset.Size, Sha(asset.Name));
            }

            var prerelease = release.TryGetProperty("prerelease", out var p) && p.GetBoolean();
            releases.Add(new MelonRelease(tag, version, prerelease, zips));
        }
        return releases;
    }
}

/// <summary>
/// GitHub 에서 MelonLoader 릴리스 목록과 zip 을 받는다. 이 도구에서 네트워크를 쓰는 유일한 곳이다.
/// 받은 zip 은 <see cref="CacheDirectory"/> 에 두고 다음 설치 때 다시 쓴다.
/// </summary>
public static class MelonReleaseClient
{
    private const string ReleasesUrl = "https://api.github.com/repos/LavaGang/MelonLoader/releases?per_page=100";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub API 는 User-Agent 가 없으면 거부한다.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamGameTool/1.0");
        return client;
    }

    /// <summary>
    /// 릴리스의 .sha512 에셋에서 16진수 128자를 읽는다. 릴리스마다 인코딩이 다르다
    /// (v0.7.0 은 ASCII, v0.6.1 은 BOM 있는 UTF-16 LE — 2026-09-21 확인). 못 읽으면 null.
    /// </summary>
    internal static string? ReadSha512(byte[] bytes)
    {
        var text = bytes is [0xFF, 0xFE, ..] ? System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
            : bytes is [0xFE, 0xFF, ..] ? System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2)
            : System.Text.Encoding.UTF8.GetString(bytes).Replace("\0", "");
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\b[0-9A-Fa-f]{128}\b");
        return match.Success ? match.Value : null;
    }

    public static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamGameTool", "MelonLoader");

    public static async Task<List<MelonRelease>> FetchAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return MelonVersions.ParseReleases(await response.Content.ReadAsStringAsync());
    }

    /// <summary>캐시에 크기가 맞는 zip 이 있으면 그것을, 없으면 받아서 경로를 돌려준다.</summary>
    public static async Task<string> GetZipAsync(MelonRelease release, PeArch arch)
    {
        var asset = release.Zips[arch];
        var directory = Path.Combine(CacheDirectory, release.Tag);
        var path = Path.Combine(directory, asset.Name);
        if (File.Exists(path) && new FileInfo(path).Length == asset.Size) return path;

        Directory.CreateDirectory(directory);
        var partial = path + ".part";
        try
        {
            using (var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var destination = File.Create(partial);
                await source.CopyToAsync(destination);
            }

            var length = new FileInfo(partial).Length;
            if (length != asset.Size)
                throw new InvalidDataException($"{release.Tag} {asset.Name} 크기가 맞지 않습니다 ({length} / {asset.Size} 바이트).");

            if (asset.Sha512Url is not null)
            {
                var expected = ReadSha512(await Http.GetByteArrayAsync(asset.Sha512Url))
                    ?? throw new InvalidDataException($"{release.Tag} 의 {Path.GetFileName(asset.Sha512Url)} 에서 SHA-512 값을 읽지 못했습니다.");
                string actual;
                await using (var stream = File.OpenRead(partial))
                    actual = Convert.ToHexString(await SHA512.HashDataAsync(stream));
                if (!expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{release.Tag} {asset.Name} 의 SHA-512 가 릴리스에 적힌 값과 다릅니다.");
            }

            File.Move(partial, path, overwrite: true);
            return path;
        }
        finally
        {
            try { File.Delete(partial); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
