using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace steam_game_tool;

public static class GameExport
{
    public static string ToText(IEnumerable<SteamGame> games, string header)
    {
        var rows = games.OrderBy(g => g.NameKey, System.StringComparer.OrdinalIgnoreCase).ToList();
        return header + "\r\n\r\n" + string.Join("", rows.Select((g, i) => $"{i + 1}. {g.NameKey}\r\n"))
            + $"\r\nTotal: {rows.Count}\r\n";
    }

    public static string ToJson(IEnumerable<SteamGame> games) => JsonSerializer.Serialize(new
    {
        schemaVersion = 4,
        games = games.OrderBy(g => g.NameKey, System.StringComparer.Ordinal).Select(g => new
        {
            appId = g.AppId,
            name = g.DisplayName,
            nameKey = g.NameKey,
            rawName = g.StoreName ?? g.Name,
            installDir = g.InstallDir,
            backend = g.Backend.ToString(),
            classifiedBackend = g.ClassifiedBackend.ToString(),
            mainPlayerPath = g.MainPlayer?.PlayerPath,
            mainPlayerSelection = g.Selection.Reason,
            selectionConfidence = g.Selection.Confidence,
            runtimeUnresolved = g.IsRuntimeUnresolved,
            backendUnresolved = g.IsBackendUnresolved,
            hasNestedMatch = g.HasNestedMatch,
            hasNestedUnity = g.HasNestedUnity,
            appType = g.AppType.ToString(),
            hasManaged = g.HasManaged,
            hasMonoBleedingEdge = g.HasMonoBleedingEdge,
            backendSource = g.BackendSource,
            backendDataPath = g.BackendDataPath,
            dataDirectoryCount = g.DataDirectoryCount,
            backendEvidence = g.BackendEvidence.Select(e => new
            {
                dataPath = e.DataPath, layer = e.Layer, backend = e.Backend.ToString(), rule = e.Rule,
                playerPath = e.PlayerPath, hasManaged = e.HasManaged,
                monoBleedingEdgePath = e.MonoBleedingEdgePath, monoRuntimePath = e.MonoRuntimePath,
                runtimeKind = e.RuntimeKind,
                runtime = e.Runtime, isBundled = e.IsBundled,
                executablePath = e.ExecutablePath, executableSize = e.ExecutableSize,
            }),
        }),
    }, new JsonSerializerOptions { WriteIndented = true });
}
