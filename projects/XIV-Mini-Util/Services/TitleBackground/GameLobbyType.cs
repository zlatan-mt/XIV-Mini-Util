// Path: projects/XIV-Mini-Util/Services/TitleBackground/GameLobbyType.cs
// Description: タイトル/キャラ選択ロビー種別の純粋な判定ロジック
// Reason: native hook 本体から遷移判定を切り離してゲーム不要テストで検証するため
namespace XivMiniUtil.Services.TitleBackground;

internal enum GameLobbyType : short
{
    None = -1,
    Title = 0,
    CharaSelect = 1,
    Aetherial = 2,
    LaNoscea = 3,
    BlackShroud = 4,
    Thanalan = 5,
    Residence = 6,
}

internal static class GameLobbyTypeHelper
{
    public static bool IsTitleCharaSelectTransition(GameLobbyType current, GameLobbyType next)
    {
        return (current == GameLobbyType.Title && next == GameLobbyType.CharaSelect)
            || (current == GameLobbyType.CharaSelect && next == GameLobbyType.Title);
    }

    public static GameLobbyType GetCurrentMapForTransition(GameLobbyType current, GameLobbyType next, bool overrideEnabled)
    {
        return overrideEnabled && IsTitleCharaSelectTransition(current, next)
            ? GameLobbyType.None
            : current;
    }
}

internal static class TitleBackgroundRuntimeModeHelper
{
    public static bool IsTitleOverrideImplemented(TitleBackgroundRuntimeMode mode)
    {
        return mode == TitleBackgroundRuntimeMode.CharaSelectOnly;
    }

    public static bool ShouldCreateSceneHooks(TitleBackgroundRuntimeMode mode, bool overrideEnabled)
    {
        return overrideEnabled && mode is TitleBackgroundRuntimeMode.CharaSelectOnly or TitleBackgroundRuntimeMode.HookProbe;
    }

    public static bool ShouldAllowDirectTextHookTargets(TitleBackgroundRuntimeMode mode, bool overrideEnabled)
    {
        return mode == TitleBackgroundRuntimeMode.HookProbe
            || (overrideEnabled && mode == TitleBackgroundRuntimeMode.CharaSelectOnly);
    }

    public static bool ShouldCreateCameraHook(TitleBackgroundRuntimeMode mode, bool overrideEnabled, bool cameraOverrideEnabled)
    {
        return mode == TitleBackgroundRuntimeMode.CharaSelectOnly
            && ShouldCreateSceneHooks(mode, overrideEnabled)
            && cameraOverrideEnabled;
    }

    public static bool ShouldValidateSceneOverrideConfiguration(TitleBackgroundRuntimeMode mode)
    {
        return mode == TitleBackgroundRuntimeMode.CharaSelectOnly;
    }

    public static bool AreSceneHooksReady(bool createSceneReady, bool lobbyUpdateReady, bool loadLobbySceneReady)
    {
        return createSceneReady && lobbyUpdateReady && loadLobbySceneReady;
    }

    public static bool AreNativeSceneAddressesReady(bool createSceneReady, bool lobbyUpdateReady, bool loadLobbySceneReady, bool currentMapReady)
    {
        return createSceneReady && lobbyUpdateReady && loadLobbySceneReady && currentMapReady;
    }

    public static bool AreNativeProbeAddressesReady(bool createSceneReady, bool lobbyUpdateReady, bool loadLobbySceneReady)
    {
        return createSceneReady && lobbyUpdateReady && loadLobbySceneReady;
    }

    public static bool IsRuntimeModeSelectable(TitleBackgroundRuntimeMode mode)
    {
        return mode != TitleBackgroundRuntimeMode.TitleAndCharaSelect;
    }

    public static bool IsFocusUsed(bool cameraOverrideEnabled)
    {
        return cameraOverrideEnabled;
    }
}
