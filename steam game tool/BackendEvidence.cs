namespace steam_game_tool;

/// <summary>한 데이터 폴더의 관측 결과. Layer는 위치이며 런처/본체 역할을 보증하지 않는다.</summary>
public sealed record BackendEvidence(string? DataPath, string Layer, UnityBackend Backend, string Rule)
{
    public string PlayerPath { get; init; } = ".";
    public string? ExecutablePath { get; init; }
    public long ExecutableSize { get; init; }
    public bool IsBundled { get; init; }
    public bool HasManaged { get; init; }
    public string? MonoBleedingEdgePath { get; init; }
    public string? MonoRuntimePath { get; init; }
    public bool HasMonoBleedingEdge => MonoBleedingEdgePath is not null;
    public string? Runtime => Backend == UnityBackend.Il2Cpp ? "il2cpp" :
        HasMonoBleedingEdge ? "monobleedingedge" : MonoRuntimePath is not null ? "mono" : null;
    public string RuntimeKind => HasMonoBleedingEdge ? "monobleedingedge-runtime" :
        MonoRuntimePath is not null ? "mono-runtime" : "unknown";
}
