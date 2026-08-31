// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundHookLifecycleRuntimeState.cs
// Description: native hookインスタンスとservice状態機械（hook装着可否・dispose状態）のライフサイクル状態を保持する
// Reason: 巨大サービスから同一ライフサイクルの可変状態を責務単位で分離するため
using Dalamud.Hooking;

namespace XivMiniUtil.Services.TitleBackground;

// native hookインスタンスとservice状態機械（hook装着可否・dispose状態）のライフサイクル状態。装着・解除・Disposeのロジックはservice側に残る。
internal sealed class TitleBackgroundHookLifecycleRuntimeState
{
    public Hook<CreateSceneDelegate>? CreateSceneHook { get; set; }

    public Hook<LobbyUpdateDelegate>? LobbyUpdateHook { get; set; }

    public Hook<LoadLobbySceneDelegate>? LoadLobbySceneHook { get; set; }

    public Hook<LobbySceneLoadedDelegate>? LobbySceneLoadedHook { get; set; }

    public Hook<LobbyCameraFixOnDelegate>? CameraFixOnHook { get; set; }

    public Hook<CalculateLobbyCameraLookAtYDelegate>? CalculateLobbyCameraLookAtYHook { get; set; }

    public Hook<SetCameraCurveMidPointDelegate>? SetCameraCurveMidPointHook { get; set; }

    public Hook<CalculateCameraCurveLowAndHighPointDelegate>? CalculateCameraCurveLowAndHighPointHook { get; set; }

    public TitleBackgroundServiceState State { get; set; } = TitleBackgroundServiceState.Disabled;

    public string StateReason { get; set; } = "無効";

    public bool Disposed { get; set; }
}
