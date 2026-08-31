// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundProbeSession.cs
// Description: TitleBackground create-scene / lobby probe のセッション状態を保持する
// Reason: 診断セッション状態を TitleScreenBackgroundService の本体ロジックから分離するため
namespace XivMiniUtil.Services.TitleBackground;

internal sealed class TitleBackgroundProbeSession
{
    public TitleBackgroundProbeSession(TitleBackgroundProbeSettingsSnapshot originalSettings)
    {
        OriginalSettings = originalSettings;
    }

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public TitleBackgroundProbeSettingsSnapshot OriginalSettings { get; }
    public bool HookEnabledAtStart { get; set; }
    public bool HookEnabledAtEnd { get; set; }
    public bool RuntimeErrorOccurred { get; set; }
    public string LastError { get; set; } = string.Empty;
    public int CreateSceneCallCount { get; set; }
    public int CreateSceneCharaSelectCallCount { get; set; }
    public int LobbyUpdateCallCount { get; set; }
    public int LoadLobbySceneCallCount { get; set; }
    public GameLobbyType LastCreateSceneLobbyType { get; set; } = GameLobbyType.None;
    public string LastCreateScenePath { get; set; } = string.Empty;
    public uint LastCreateSceneTerritoryId { get; set; }
    public uint LastCreateSceneLayerFilterKey { get; set; }
    public List<string> CreateSceneHistory { get; } = [];
    public GameLobbyType LastLobbyUpdateMapId { get; set; } = GameLobbyType.None;
    public int LastLobbyUpdateTime { get; set; }
    public GameLobbyType LastLoadLobbySceneMapId { get; set; } = GameLobbyType.None;
}
