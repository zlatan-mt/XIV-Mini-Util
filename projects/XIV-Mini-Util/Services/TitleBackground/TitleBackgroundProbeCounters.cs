// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundProbeCounters.cs
// Description: TitleBackground probe の自動集計カウンターを保持する
// Reason: probe 状態を TitleScreenBackgroundService の本体ロジックから分離するため
namespace XivMiniUtil.Services.TitleBackground;

internal sealed class TitleBackgroundProbeCounters
{
    public int CreateSceneCallCount { get; set; }
    public string LastCreateScenePath { get; set; } = string.Empty;
    public uint LastCreateSceneTerritoryId { get; set; }
    public uint LastCreateSceneLayerFilterKey { get; set; }
    public int LobbyUpdateCallCount { get; set; }
    public GameLobbyType LastLobbyUpdateMapId { get; set; } = GameLobbyType.None;
    public int LastLobbyUpdateTime { get; set; }
    public int LoadLobbySceneCallCount { get; set; }
    public GameLobbyType LastLoadLobbySceneMapId { get; set; } = GameLobbyType.None;
}
