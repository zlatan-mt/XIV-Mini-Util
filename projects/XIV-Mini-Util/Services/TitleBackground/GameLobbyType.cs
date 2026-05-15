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
        // Phase 1 keeps native camera hooks closed while the lobby camera adapter boundary is built.
        return false;
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
        return false;
    }

    public static bool ShouldCollectAutomaticProbeCounters(
        TitleBackgroundRuntimeMode mode,
        bool overrideEnabled,
        TitleBackgroundResolverMode createSceneResolverMode,
        TitleBackgroundResolverMode lobbyUpdateResolverMode)
    {
        return overrideEnabled
            && mode == TitleBackgroundRuntimeMode.HookProbe
            && createSceneResolverMode == TitleBackgroundResolverMode.ManualDirectTextProbe
            && lobbyUpdateResolverMode == TitleBackgroundResolverMode.ManualDirectTextProbe;
    }
}

internal readonly record struct TitleBackgroundProbeReportInput(
    bool ProbeActive,
    bool OverrideEnabled,
    TitleBackgroundRuntimeMode RuntimeMode,
    TitleBackgroundResolverMode CreateSceneResolverMode,
    TitleBackgroundResolverMode LobbyUpdateResolverMode,
    bool AutomaticCountersEnabled,
    bool HooksEnabled,
    bool RuntimeError,
    string ResolverError,
    string LastError,
    int CreateSceneCallCount,
    int LobbyUpdateCallCount,
    int LoadLobbySceneCallCount,
    string LastCreateScenePath,
    uint LastCreateSceneTerritoryId,
    uint LastCreateSceneLayerFilterKey,
    GameLobbyType LastLobbyUpdateMapId,
    int LastLobbyUpdateTime,
    GameLobbyType LastLoadLobbySceneMapId);

internal static class TitleBackgroundProbeReportHelper
{
    public static string GetModeStatus(TitleBackgroundProbeReportInput input)
    {
        var manualDirect = input.CreateSceneResolverMode == TitleBackgroundResolverMode.ManualDirectTextProbe
            && input.LobbyUpdateResolverMode == TitleBackgroundResolverMode.ManualDirectTextProbe;
        return input.OverrideEnabled
            && input.RuntimeMode == TitleBackgroundRuntimeMode.HookProbe
            && manualDirect
            ? "ready"
            : "attention";
    }

    public static string GetOverallStatus(TitleBackgroundProbeReportInput input)
    {
        if (input.RuntimeError || HasText(input.ResolverError) || HasText(input.LastError))
        {
            return "failure";
        }

        if (!input.HooksEnabled)
        {
            return "attention";
        }

        if (input.CreateSceneCallCount > 0 && input.LobbyUpdateCallCount > 0 && input.LoadLobbySceneCallCount > 0)
        {
            return "observed";
        }

        if (input.CreateSceneCallCount > 0 || input.LobbyUpdateCallCount > 0 || input.LoadLobbySceneCallCount > 0)
        {
            return "partial";
        }

        return "waiting";
    }

    public static IReadOnlyList<string> GetAttentionItems(TitleBackgroundProbeReportInput input)
    {
        var items = new List<string>();
        if (GetModeStatus(input) != "ready")
        {
            items.Add("HookProbe + ManualDirectTextProbe の設定ではありません。");
        }

        if (!input.HooksEnabled)
        {
            items.Add("native hooks が有効ではありません。");
        }

        if (input.RuntimeError)
        {
            items.Add("runtime error が発生しています。");
        }

        if (HasText(input.ResolverError))
        {
            items.Add($"resolver error: {input.ResolverError}");
        }

        if (HasText(input.LastError))
        {
            items.Add($"probe error: {input.LastError}");
        }

        if (input.CreateSceneCallCount == 0)
        {
            items.Add("CreateScene detour はまだ未観測です。");
        }

        if (input.LobbyUpdateCallCount == 0)
        {
            items.Add("LobbyUpdate detour はまだ未観測です。");
        }

        if (input.LoadLobbySceneCallCount == 0)
        {
            items.Add("LoadLobbyScene detour はまだ未観測です。");
        }

        return items;
    }

    public static string GetNextCheck(TitleBackgroundProbeReportInput input)
    {
        var status = GetOverallStatus(input);
        return status switch
        {
            "observed" => "3 hooks observed. 次は CharaSelectOnly の同一 scene smoke test を確認できます。",
            "partial" => "未観測 hook が残っています。ログイン後の /xmutbgprobe report で callCount を再確認してください。",
            "waiting" => "まだ detour が観測されていません。設定UIで HookProbe にしてキャラ選択遷移後に再確認してください。",
            "failure" => "error を先に解消してください。",
            _ => "HookProbe + ManualDirectTextProbe と hooksEnabled を先に確認してください。",
        };
    }

    private static bool HasText(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);
    }
}
