using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace steam_game_tool
{
    /// <summary>
    /// Steam 이 분류하는 앱 종류. appcache\appinfo.vdf 의 common.type 값에서 온다.
    /// </summary>
    public enum SteamAppType
    {
        /// <summary>타입을 알 수 없음(카탈로그에 없거나 읽지 못함).</summary>
        Unknown = 0,
        Game,
        Demo,
        Application,
        Tool,
        Music,
        Video,
        Config,
        /// <summary>위 어디에도 속하지 않는 알려진 타입.</summary>
        Other,
    }

    /// <summary>설치된 앱 하나에 대한 Steam 쪽 정보.</summary>
    public sealed class SteamAppEntry
    {
        public required uint AppId { get; init; }
        /// <summary>Steam 스토어 표기 이름.</summary>
        public required string Name { get; init; }
        /// <summary>steamapps\common 아래의 폴더 이름.</summary>
        public required string InstallDir { get; init; }
        public SteamAppType Type { get; set; } = SteamAppType.Unknown;
    }

    /// <summary>
    /// 설치된 Steam 앱의 목록과 종류.
    /// <para>
    /// 두 곳에서 모읍니다.
    /// <list type="bullet">
    /// <item>각 라이브러리의 <c>steamapps\appmanifest_*.acf</c> — appid, 이름, 설치 폴더</item>
    /// <item>Steam 설치 폴더의 <c>appcache\appinfo.vdf</c> — 앱 종류(Game/Music/Tool/…)</item>
    /// </list>
    /// 둘 다 <b>없어도 동작합니다.</b> 읽지 못하면 그만큼 정보가 비어 있을 뿐입니다.
    /// </para>
    /// </summary>
    public sealed class SteamCatalog
    {
        private readonly Dictionary<string, SteamAppEntry> _byInstallDir =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>같은 installdir 을 쓰는 다른 앱들(사운드트랙 DLC 등).</summary>
        private readonly List<SteamAppEntry> _shared = new();

        /// <summary>appinfo.vdf 에서 앱 종류를 하나라도 읽어냈는가.</summary>
        public bool HasTypeInfo { get; private set; }

        /// <summary>appmanifest 를 하나라도 읽어냈는가.</summary>
        public bool HasApps => _byInstallDir.Count > 0;

        public static SteamCatalog Empty { get; } = new();

        /// <summary>설치 폴더 이름으로 앱을 찾는다. 없으면 null.</summary>
        public SteamAppEntry? Find(string installDirName) =>
            _byInstallDir.TryGetValue(installDirName, out var e) ? e : null;

        /// <summary>
        /// 라이브러리들의 appmanifest 와 Steam 의 appinfo 캐시를 읽어 카탈로그를 만든다.
        /// </summary>
        /// <param name="libraryRoots">라이브러리 루트(=steamapps 의 부모) 경로들.</param>
        /// <param name="steamPath">Steam 설치 폴더. appcache\appinfo.vdf 를 여기서 찾는다.</param>
        /// <param name="onWarning">읽기 실패를 알리는 콜백. 호출한 스레드에서 차례로 불린다.</param>
        public static SteamCatalog Build(
            IEnumerable<string> libraryRoots,
            string? steamPath,
            Action<string>? onWarning = null)
        {
            var catalog = new SteamCatalog();

            foreach (var lib in libraryRoots)
            {
                var steamapps = Path.Combine(lib, "steamapps");
                string[] manifests;
                try { manifests = Directory.GetFiles(steamapps, "appmanifest_*.acf"); }
                catch (Exception ex) { onWarning?.Invoke($"{steamapps}: {ex.Message}"); continue; }

                foreach (var file in manifests)
                {
                    try
                    {
                        var text = File.ReadAllText(file);
                        var idText = AcfValue(text, "appid");
                        var name = AcfValue(text, "name");
                        var dir = AcfValue(text, "installdir");
                        if (idText is null || dir is null) continue;
                        if (!uint.TryParse(idText, out var appid)) continue;

                        var entry = new SteamAppEntry
                        {
                            AppId = appid,
                            Name = name ?? dir,
                            InstallDir = dir,
                        };

                        // 사운드트랙 DLC 처럼 여러 앱이 한 폴더를 공유하는 경우가 있다.
                        // 먼저 들어온 것을 유지한다 — 종류를 채운 뒤 게임을 우선하도록 다시 정리한다.
                        if (!catalog._byInstallDir.ContainsKey(dir))
                            catalog._byInstallDir[dir] = entry;
                        else
                            catalog._shared.Add(entry);
                    }
                    catch (Exception ex) { onWarning?.Invoke($"{Path.GetFileName(file)}: {ex.Message}"); }
                }
            }

            if (steamPath is not null && catalog._byInstallDir.Count > 0)
            {
                var appinfo = Path.Combine(steamPath, "appcache", "appinfo.vdf");
                var types = AppInfoCache.TryReadTypes(appinfo, onWarning);
                if (types.Count > 0)
                {
                    catalog.HasTypeInfo = true;
                    void Apply(SteamAppEntry e)
                    {
                        if (types.TryGetValue(e.AppId, out var t)) e.Type = t;
                    }
                    foreach (var entry in catalog._byInstallDir.Values) Apply(entry);
                    foreach (var entry in catalog._shared) Apply(entry);

                    // 한 폴더를 여러 앱이 공유하면 게임을 대표로 삼는다.
                    // 그렇지 않으면 사운드트랙 DLC 가 본편 게임을 가려 버린다.
                    foreach (var candidate in catalog._shared)
                    {
                        var current = catalog._byInstallDir[candidate.InstallDir];
                        if (!IsGameType(current.Type) && IsGameType(candidate.Type))
                            catalog._byInstallDir[candidate.InstallDir] = candidate;
                    }
                }
            }

            return catalog;
        }

        private static bool IsGameType(SteamAppType t) => t is SteamAppType.Game or SteamAppType.Demo;

        /// <summary>ACF(텍스트 VDF)에서 최상위 키의 값을 뽑는다. 중첩 블록은 다루지 않는다.</summary>
        internal static string? AcfValue(string text, string key)
        {
            var needle = '"' + key + '"';
            var i = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i = text.IndexOf('"', i + needle.Length);
            if (i < 0) return null;
            var j = text.IndexOf('"', i + 1);
            return j < 0 ? null : text.Substring(i + 1, j - i - 1);
        }

        internal static SteamAppType MapType(string? raw) => raw?.ToLowerInvariant() switch
        {
            null => SteamAppType.Unknown,
            "game" => SteamAppType.Game,
            "demo" => SteamAppType.Demo,
            "application" => SteamAppType.Application,
            "tool" => SteamAppType.Tool,
            "music" => SteamAppType.Music,
            "video" => SteamAppType.Video,
            "config" => SteamAppType.Config,
            _ => SteamAppType.Other,
        };
    }

    /// <summary>
    /// Steam 의 <c>appcache\appinfo.vdf</c>(바이너리 VDF) 리더.
    /// <para>
    /// 이 파일은 <b>Valve 의 비공개 포맷</b>이라 언제든 바뀔 수 있습니다.
    /// 그래서 이 리더는 실패를 정상 경로로 취급하고 <b>빈 결과를 돌려줍니다</b> —
    /// 앱 종류를 모르면 도구는 종류 없이 동작할 뿐 깨지지 않습니다.
    /// </para>
    /// <para>
    /// 구조: 헤더(매직, 유니버스, [문자열 테이블 오프셋]) 뒤에 앱 섹션이 이어지고
    /// appid 가 0 이면 끝. 각 섹션은 고정 헤더 + 바이너리 KV 트리이며,
    /// 우리가 필요한 값은 <c>appinfo → common → type</c> 하나뿐입니다.
    /// </para>
    /// </summary>
    internal static class AppInfoCache
    {
        private const uint Magic27 = 0x07564427;   // 문자열 키 인라인
        private const uint Magic28 = 0x07564428;   // + binaryDataSha1
        private const uint Magic29 = 0x07564429;   // + 문자열 테이블(키가 인덱스)

        // 바이너리 VDF 노드 종류.
        private const int NodeObject = 0x00;
        private const int NodeString = 0x01;
        private const int NodeInt32 = 0x02;
        private const int NodeFloat = 0x03;
        private const int NodePointer = 0x04;
        private const int NodeWideString = 0x05;
        private const int NodeColor = 0x06;
        private const int NodeUInt64 = 0x07;
        private const int NodeEnd = 0x08;
        private const int NodeInt64 = 0x0A;

        /// <summary>appid → 앱 종류. 읽지 못하면 빈 사전.</summary>
        public static Dictionary<uint, SteamAppType> TryReadTypes(
            string path, Action<string>? onWarning = null)
        {
            try
            {
                if (!File.Exists(path)) return new Dictionary<uint, SteamAppType>();
                using var fs = File.OpenRead(path);
                return ReadTypes(fs);
            }
            catch (Exception ex)
            {
                onWarning?.Invoke($"appinfo.vdf: {ex.Message}");
                return new Dictionary<uint, SteamAppType>();
            }
        }

        internal static Dictionary<uint, SteamAppType> ReadTypes(Stream fs)
        {
            var result = new Dictionary<uint, SteamAppType>();

            var magic = (uint)ReadInt32(fs);
            ReadInt32(fs);                                  // universe
            if (magic != Magic27 && magic != Magic28 && magic != Magic29)
                return result;                              // 모르는 버전 — 조용히 포기

            string[]? strings = null;
            if (magic == Magic29)
            {
                long tableOffset = ReadInt64(fs);
                long afterHeader = fs.Position;

                fs.Position = tableOffset;
                int count = ReadInt32(fs);
                if (count < 0 || count > 5_000_000) return result;
                strings = new string[count];
                for (int i = 0; i < count; i++) strings[i] = ReadCString(fs);

                fs.Position = afterHeader;
            }

            int iCommon = IndexOf(strings, "common");
            int iType = IndexOf(strings, "type");

            // 섹션 고정 헤더: infoState(4) lastUpdated(4) picsToken(8) sha1(20) changeNumber(4)
            int fixedHeader = 4 + 4 + 8 + 20 + 4;
            if (magic != Magic27) fixedHeader += 20;        // binaryDataSha1

            while (fs.Position + 8 <= fs.Length)
            {
                uint appid = (uint)ReadInt32(fs);
                if (appid == 0) break;

                uint size = (uint)ReadInt32(fs);
                long end = fs.Position + size;
                if (end > fs.Length) break;                 // 잘린 파일 — 여기까지만

                fs.Position += fixedHeader;
                try
                {
                    var raw = FindType(fs, strings, iCommon, iType, directlyInCommon: false, depth: 0);
                    if (raw is not null) result[appid] = SteamCatalog.MapType(raw);
                }
                catch (Exception)
                {
                    // 앱 하나가 이상해도 나머지는 계속 읽는다. 섹션 크기를 알고 있으므로 복구가 가능하다.
                }

                fs.Position = end;
            }

            return result;
        }

        /// <summary>
        /// common 객체 <b>바로 아래</b>의 type 문자열을 찾는다.
        /// 루트에는 "appinfo" 래퍼가 한 겹 있어서 common 은 그 안쪽에 있다.
        /// </summary>
        private static string? FindType(
            Stream s, string[]? strings, int iCommon, int iType, bool directlyInCommon, int depth)
        {
            if (depth > 32) throw new InvalidDataException("KV 중첩이 너무 깊습니다.");

            string? found = null;
            while (true)
            {
                int nodeType = s.ReadByte();
                if (nodeType == NodeEnd || nodeType < 0) break;

                bool isCommonKey, isTypeKey;
                if (strings is null)
                {
                    var key = ReadCString(s);
                    isCommonKey = key.Equals("common", StringComparison.OrdinalIgnoreCase);
                    isTypeKey = key.Equals("type", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    int keyIdx = ReadInt32(s);
                    isCommonKey = keyIdx == iCommon;
                    isTypeKey = keyIdx == iType;
                }

                if (nodeType == NodeObject)
                    found ??= FindType(s, strings, iCommon, iType, isCommonKey, depth + 1);
                else if (nodeType == NodeString)
                {
                    var value = ReadCString(s);
                    if (directlyInCommon && isTypeKey) found ??= value;
                }
                else SkipValue(s, nodeType);
            }
            return found;
        }

        private static void SkipValue(Stream s, int nodeType)
        {
            switch (nodeType)
            {
                case NodeString: ReadCString(s); break;
                case NodeWideString: while (ReadInt16(s) != 0) { } break;
                case NodeInt32: case NodeFloat: case NodePointer: case NodeColor:
                    s.Position += 4; break;
                case NodeUInt64: case NodeInt64:
                    s.Position += 8; break;
                default:
                    throw new InvalidDataException($"알 수 없는 KV 노드 0x{nodeType:X2}");
            }
        }

        private static int IndexOf(string[]? strings, string value)
        {
            if (strings is null) return -1;
            for (int i = 0; i < strings.Length; i++)
                if (strings[i] == value) return i;
            return -1;
        }

        private static int ReadInt32(Stream s)
        {
            Span<byte> b = stackalloc byte[4];
            s.ReadExactly(b);
            return BitConverter.ToInt32(b);
        }

        private static short ReadInt16(Stream s)
        {
            Span<byte> b = stackalloc byte[2];
            s.ReadExactly(b);
            return BitConverter.ToInt16(b);
        }

        private static long ReadInt64(Stream s)
        {
            Span<byte> b = stackalloc byte[8];
            s.ReadExactly(b);
            return BitConverter.ToInt64(b);
        }

        private static string ReadCString(Stream s)
        {
            var bytes = new List<byte>(32);
            int b;
            while ((b = s.ReadByte()) > 0) bytes.Add((byte)b);
            if (b < 0) throw new EndOfStreamException("문자열이 끝나기 전에 파일이 끝났습니다.");
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}
