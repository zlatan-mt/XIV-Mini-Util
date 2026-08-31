// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundV2Logic.cs
// Description: Title Background V2 (Il Mheg proof) の純粋ロジック。framing 適用可否の判定と、
//              top-down を解消する固定 sane pose の決定を game 参照なしで行う。
// Reason: skill §3「純粋ロジックは static + LogicTests」。座標規約を自前発明せず、V2 は world 座標から
//         camera focus を推測しない。yaw は 0（エンジンの自然な character-follow に任せる）、pitch/distance は
//         top-down 既定 (DirV=0/Distance=3.3) を置き換える控えめな固定値。初回実機レポートの数値で微調整する。
namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundV2FramingDecision
{
    Apply,
    Skip,
    Stop,
}

internal readonly record struct TitleBackgroundV2FramingGate(
    TitleBackgroundV2FramingDecision Decision,
    string Reason);

internal readonly record struct TitleBackgroundV2FramingPose(
    float Yaw,
    float Pitch,
    float Distance,
    float FovY);

internal static class TitleBackgroundV2Logic
{
    // Il Mheg (custom:n4f4) 用の固定 framing 定数。world 座標由来でも TitleEdit 由来でもない。
    // DirH=0（正面。焦点はエンジンの自然な配置キャラ追従に任せる）、DirV は既定 0（top-down 気味）を
    // 控えめな見下ろしへ、Distance は既定 3.3 よりやや寄せてポートレート寄りにする。
    public const float DefaultYaw = 0f;
    public const float DefaultPitch = 0.15f;
    public const float DefaultDistance = 2.6f;

    // legacy camera-maintain 経路（_savedViewPoseMaintain / Phase2G generated curve override /
    // saved-view per-frame reassert / curve-based camera ownership / 毎フレーム DrawObject placement）を
    // arm してよいか。新 CharaSelect エンジン（V2 framing または TitleEdit-informed placement）が
    // active 中は必ず false。これが legacy と新エンジンの制御排他の単一 source of truth。
    public static bool IsLegacyCameraMaintenanceAllowed(bool newCharaSelectEngineActive)
    {
        return !newCharaSelectEngineActive;
    }

    // V2 の scene-ready one-shot framing を今書いてよいかの判定。
    // legacy の各種ゲートと同じ観点（pre-login / service ready / hook-probe 除外 / session active /
    // scene generation 一致 / CharaSelect map）を standalone で評価する。
    // 注意: legacy saved-view / facing の run 抑止（IsSavedViewSuppressedByAutomaticRun）は V2 framing の
    // 停止条件にしない。one-click 実機確認 run 中でも V2 framing は適用されなければ意味がないため。
    public static TitleBackgroundV2FramingGate ShouldApplyFraming(
        bool v2Active,
        bool serviceReady,
        bool hookProbeMode,
        bool loggedIn,
        bool charaSelectSessionActive,
        int activeSceneGeneration,
        int runtimeSceneGeneration,
        bool boundedWindowOpen,
        GameLobbyType currentMap)
    {
        if (!v2Active)
        {
            return new(TitleBackgroundV2FramingDecision.Skip, "v2-inactive");
        }

        // login 後は即時停止（post-login へ native 書込をリークさせない）。
        if (loggedIn)
        {
            return new(TitleBackgroundV2FramingDecision.Stop, "logged-in");
        }

        if (!charaSelectSessionActive)
        {
            return new(TitleBackgroundV2FramingDecision.Stop, "session-inactive");
        }

        if (!serviceReady)
        {
            return new(TitleBackgroundV2FramingDecision.Skip, "service-not-ready");
        }

        if (hookProbeMode)
        {
            return new(TitleBackgroundV2FramingDecision.Skip, "hook-probe");
        }

        if (activeSceneGeneration <= 0
            || runtimeSceneGeneration <= 0
            || activeSceneGeneration != runtimeSceneGeneration)
        {
            return new(TitleBackgroundV2FramingDecision.Skip, "scene-generation-mismatch");
        }

        if (!IsCharaSelectMap(currentMap))
        {
            return new(TitleBackgroundV2FramingDecision.Skip, "not-chara-select");
        }

        if (!boundedWindowOpen)
        {
            return new(TitleBackgroundV2FramingDecision.Skip, "bounded-window-closed");
        }

        return new(TitleBackgroundV2FramingDecision.Apply, "ready");
    }

    // 適用する pose を決定する。Task A は Il Mheg のみで framing mode は診断的な意味しか持たないため、
    // 固定 sane 定数 + 設定 FovY を返す。world 座標・保存 view・TitleEdit preset は参照しない。
    public static TitleBackgroundV2FramingPose ResolveFramingPose(
        string candidateId,
        TitleBackgroundCharaSelectCameraFramingMode framingMode,
        float configuredFovY)
    {
        var fovY = float.IsFinite(configuredFovY) && configuredFovY > 0f
            ? Math.Clamp(configuredFovY, TitleBackgroundPreset.MinFovY, TitleBackgroundPreset.MaxFovY)
            : TitleBackgroundPreset.DefaultFovY;

        var pitch = DefaultPitch;
        var distance = DefaultDistance;

        // framing mode は Il Mheg では控えめな相対調整のみ（top-down を作らない範囲）。
        switch (framingMode)
        {
            case TitleBackgroundCharaSelectCameraFramingMode.LowerCamera:
                pitch = DefaultPitch + 0.05f;
                break;
            case TitleBackgroundCharaSelectCameraFramingMode.CloserCharacter:
                distance = DefaultDistance - 0.3f;
                break;
            case TitleBackgroundCharaSelectCameraFramingMode.CenterCharacter:
                pitch = DefaultPitch - 0.03f;
                break;
            default:
                break;
        }

        return new TitleBackgroundV2FramingPose(
            DefaultYaw,
            Math.Clamp(pitch, -1.4f, 1.4f),
            Math.Clamp(distance, 1.5f, 5.0f),
            fovY);
    }

    public static bool IsCharaSelectMap(GameLobbyType map)
    {
        return map == GameLobbyType.CharaSelect;
    }
}
