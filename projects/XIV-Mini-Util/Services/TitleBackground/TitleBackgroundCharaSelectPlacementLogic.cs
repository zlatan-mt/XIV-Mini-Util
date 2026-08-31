// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectPlacementLogic.cs
// Description: TitleEdit-informed Character Select placement path の純粋ロジック。
//              「今キャラを配置してよいか」の fail-closed ゲート、配置を書くトリガ判定、
//              source-backed な scene-local LocationModel-lite の組み立て、
//              one-click evidence capture の妥当性・安定サンプル判定を game 参照なしで行う。
// Reason: skill §3「純粋ロジックは static + LogicTests」。座標規約を自前発明せず、配置座標は
//         one-click 実機 run で read-only 採取された source-backed 値だけを使う（未採取なら書かない）。
//         Position==(0,0,0) は正常値として許可する（PR #7 根本修正 点5）。
using System.Numerics;
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundCharaSelectEngineDecision
{
    // 配置を書いてよい。
    Apply,
    // 今回は書かない（前提未成立）。恒久停止ではない。
    Skip,
    // 恒久停止（login した／セッション終了）。以後この generation では書かない。
    Stop,
}

internal readonly record struct TitleBackgroundCharaSelectEngineGate(
    TitleBackgroundCharaSelectEngineDecision Decision,
    string Reason);

internal readonly record struct TitleBackgroundCharaSelectPlacementPromotionDecision(
    bool Eligible,
    string Reason);

// CharaSelectService の canonical actor context に、Title Background の同一 frame gate を重ねたもの。
// Actor.CharacterAddress はこの context を受け渡す bounded operation 内だけで使い、保存しない。
internal readonly record struct TitleBackgroundResolvedActorContext(
    CharaSelectResolvedActorContext Actor,
    bool PlacementPathActive,
    bool PreLogin,
    bool ServiceReady,
    bool HookProbeMode,
    bool CharaSelectSessionActive,
    int ActiveSceneGeneration,
    int RuntimeSceneGeneration,
    bool IsCharaSelectMap,
    string CandidateId,
    bool CandidateMatches)
{
    public bool SceneGenerationMatches => ActiveSceneGeneration > 0
        && RuntimeSceneGeneration > 0
        && ActiveSceneGeneration == RuntimeSceneGeneration;

    public bool Valid => PlacementPathActive
        && PreLogin
        && ServiceReady
        && !HookProbeMode
        && CharaSelectSessionActive
        && SceneGenerationMatches
        && IsCharaSelectMap
        && CandidateMatches
        && Actor.Valid;
}

// pointer を含まない resolver attempt / success proof。
internal readonly record struct TitleBackgroundActorResolveProofSnapshot(
    bool Valid,
    string Source,
    int SceneGeneration,
    bool CurrentCharacterAvailable,
    bool EntryAvailable,
    bool SelectedContentAvailable,
    bool MappingAvailable,
    bool MappingHit,
    bool ClientObjectIndexValid,
    bool ObjectResolved,
    bool IdentityMatched,
    bool DrawReady,
    bool CandidateMatches,
    int RetryCount,
    string Status)
{
    public static TitleBackgroundActorResolveProofSnapshot NotRun => new(
        false, "None", 0, false, false, false, false, false, false, false, false, false, false, 0, "not-evaluated");
}

// resolver-valid actor -> stable capture -> authorized write/readback が同一 identity/generation/candidate で
// 成立した瞬間に freeze する pointer-free proof。
internal readonly record struct TitleBackgroundConfirmedPlacementProof(
    bool Valid,
    TitleBackgroundActorResolveProofSnapshot ResolveProof,
    bool ResolverAuthorized,
    bool IdentityMatched,
    int SceneGeneration,
    string CandidateId,
    bool CandidateMatches,
    bool StableCapture,
    int StableSamples,
    bool ZeroPositionAccepted,
    Vector3 Position,
    float Rotation,
    bool WriteReadbackConfirmed)
{
    public static TitleBackgroundConfirmedPlacementProof None => new(
        false,
        TitleBackgroundActorResolveProofSnapshot.NotRun,
        false,
        false,
        0,
        string.Empty,
        false,
        false,
        0,
        false,
        default,
        0f,
        false);
}

internal readonly record struct TitleBackgroundPlacementWriteAuthorization(
    bool Allowed,
    string Reason,
    bool AuthorizedByResolver,
    bool IdentityMatched);

// 配置を書く理由（bounded トリガ）。診断へ出す。
internal enum TitleBackgroundCharaSelectPlacementTrigger
{
    None,
    // capture が今 run で完了した最初の適用。
    CaptureComplete,
    // 新しい scene generation。
    SceneGeneration,
    // actor 再生成 / 選択キャラ変更。
    SelectionChange,
}

// source-backed な scene-local 配置情報。TitleEdit の LocationModel を Mini Util 命名で最小化したもの。
// preset 座標は取り込まない（値は実機 run の read-only capture 由来）。
internal readonly record struct TitleBackgroundCharaSelectLocationModel(
    string TerritoryPath,
    uint LayoutTerritoryTypeId,
    uint LayoutLayerFilterKey,
    Vector3 Position,
    float Rotation)
{
    // Position==(0,0,0) は許可する（原点が地面の候補もある）。要求は finite であること。
    public bool HasSourceBackedPosition =>
        !string.IsNullOrWhiteSpace(TerritoryPath)
        && float.IsFinite(Position.X) && float.IsFinite(Position.Y) && float.IsFinite(Position.Z)
        && float.IsFinite(Rotation);
}

internal static class TitleBackgroundCharaSelectPlacementLogic
{
    // 安定サンプル判定（点1: N=5 / position epsilon=0.01 / rotation epsilon=0.01rad）。
    public const int CaptureStableSampleTarget = 5;
    public const float CapturePositionEpsilon = 0.01f;
    public const float CaptureRotationEpsilon = 0.01f;
    public const int PlacementWriteRetryBudget = 3;

    // bounded timeout（点1: timeout 時は fail-closed）。one-click run 中に安定 5 連続へ到達しなければ諦める。
    // Framework tick ベースでカウントするため、十分な猶予（≈ 8 秒 @ 60fps）を取る。
    public const int CaptureFrameBudget = 480;

    // legacy camera-maintain / per-frame DrawObject placement / FixOn override / Phase2G curve override
    // を arm してよいか。新 placement path が active なら false（V2 と同じ排他規約。Phase 0 §5）。
    public static bool IsLegacyOwnershipAllowed(bool newCharaSelectEngineActive)
    {
        return !newCharaSelectEngineActive;
    }

    // 新 placement path が有効か。既存フラグ（override + placement-enabled）の合成のみ。
    public static bool IsPlacementPathActive(bool overrideEnabled, bool placementEnabled)
    {
        return overrideEnabled && placementEnabled;
    }

    // 今キャラ配置を書いてよいかの fail-closed 判定。
    // login / セッション非 active は Stop。service 未 ready / hook-probe / generation 不一致 /
    // 非 CharaSelect / 候補不一致 / source-backed 座標なし / キャラ未解決 は Skip。全成立で Apply。
    public static TitleBackgroundCharaSelectEngineGate ResolveGate(
        bool placementPathActive,
        bool serviceReady,
        bool hookProbeMode,
        bool loggedIn,
        bool charaSelectSessionActive,
        int activeSceneGeneration,
        int runtimeSceneGeneration,
        bool isCharaSelectMap,
        bool candidateMatches,
        bool hasSourceBackedPosition,
        bool characterResolved,
        bool captureProofValid = true)
    {
        if (!placementPathActive)
        {
            return new(TitleBackgroundCharaSelectEngineDecision.Skip, "placement-path-inactive");
        }

        // login 後は即時恒久停止（post-login へ native 書込をリークさせない。skill §1.4）。
        if (loggedIn)
        {
            return new(TitleBackgroundCharaSelectEngineDecision.Stop, "logged-in");
        }

        if (!charaSelectSessionActive)
        {
            return new(TitleBackgroundCharaSelectEngineDecision.Stop, "session-inactive");
        }

        if (!serviceReady)
        {
            return new(TitleBackgroundCharaSelectEngineDecision.Skip, "service-not-ready");
        }

        if (hookProbeMode)
        {
            return new(TitleBackgroundCharaSelectEngineDecision.Skip, "hook-probe");
        }

        if (activeSceneGeneration <= 0
            || runtimeSceneGeneration <= 0
            || activeSceneGeneration != runtimeSceneGeneration)
        {
            return new(TitleBackgroundCharaSelectEngineDecision.Skip, "scene-generation-mismatch");
        }

        if (!isCharaSelectMap)
        {
            return new(TitleBackgroundCharaSelectEngineDecision.Skip, "not-chara-select");
        }

        if (!candidateMatches)
        {
            return new(TitleBackgroundCharaSelectEngineDecision.Skip, "candidate-mismatch");
        }

        if (!hasSourceBackedPosition)
        {
            return new(TitleBackgroundCharaSelectEngineDecision.Skip, "no-source-backed-position");
        }

        if (!characterResolved)
        {
            return new(TitleBackgroundCharaSelectEngineDecision.Skip, "character-unresolved");
        }

        if (!captureProofValid)
        {
            return new(TitleBackgroundCharaSelectEngineDecision.Skip, "capture-proof-not-ready");
        }

        return new(TitleBackgroundCharaSelectEngineDecision.Apply, "ready");
    }

    // 実配置を書くのは以下の遷移時のみ（点7）:
    //  - capture がこの run で完了した最初の適用（captureJustCompleted）
    //  - 新しい scene generation
    //  - actor 再生成 / 選択キャラ変更（pointer 変化 または selectionChangePending）
    // 同一 (generation, actor) へ連続 write しない。
    public static bool ShouldWritePlacement(
        int gateSceneGeneration,
        int lastAppliedSceneGeneration,
        CharaSelectActorIdentityKey currentActorKey,
        CharaSelectActorIdentityKey lastAppliedActorKey,
        bool captureJustCompleted = false,
        bool selectionChangePending = false)
    {
        if (!currentActorKey.Valid)
        {
            return false;
        }

        var sameTarget = gateSceneGeneration == lastAppliedSceneGeneration
            && currentActorKey == lastAppliedActorKey;
        if (sameTarget && !captureJustCompleted && !selectionChangePending)
        {
            return false;
        }

        return captureJustCompleted
            || selectionChangePending
            || gateSceneGeneration != lastAppliedSceneGeneration
            || currentActorKey != lastAppliedActorKey;
    }

    // 実際に書いたときの trigger ラベル（診断用）。ShouldWritePlacement が true を返した前提で呼ぶ。
    public static TitleBackgroundCharaSelectPlacementTrigger ResolvePlacementTrigger(
        int gateSceneGeneration,
        int lastAppliedSceneGeneration,
        CharaSelectActorIdentityKey currentActorKey,
        CharaSelectActorIdentityKey lastAppliedActorKey,
        bool captureJustCompleted,
        bool selectionChangePending)
    {
        if (captureJustCompleted)
        {
            return TitleBackgroundCharaSelectPlacementTrigger.CaptureComplete;
        }

        if (gateSceneGeneration != lastAppliedSceneGeneration)
        {
            return TitleBackgroundCharaSelectPlacementTrigger.SceneGeneration;
        }

        if (selectionChangePending || currentActorKey != lastAppliedActorKey)
        {
            return TitleBackgroundCharaSelectPlacementTrigger.SelectionChange;
        }

        return TitleBackgroundCharaSelectPlacementTrigger.None;
    }

    public static TitleBackgroundPlacementWriteAuthorization EvaluateWriteAuthorization(
        bool resolvedActorContextValid,
        bool captureProofValid,
        bool sameGeneration,
        bool sameActorIdentity,
        bool sameCandidate)
    {
        if (!resolvedActorContextValid)
        {
            return new(false, "resolver-invalid", false, sameActorIdentity);
        }

        if (!captureProofValid)
        {
            return new(false, "capture-proof-invalid", true, sameActorIdentity);
        }

        if (!sameGeneration)
        {
            return new(false, "capture-generation-mismatch", true, sameActorIdentity);
        }

        if (!sameActorIdentity)
        {
            return new(false, "capture-identity-mismatch", true, false);
        }

        if (!sameCandidate)
        {
            return new(false, "capture-candidate-mismatch", true, true);
        }

        return new(true, "authorized", true, true);
    }

    // one-click evidence capture の妥当性（点5）。(0,0,0) は拒否しない。
    // identity 一致 / mapping 一致 / object resolved /
    // sceneGeneration 一致 / finite transform を要求する。DrawObject/ready は
    // TitleEdit の actor transform 経路が draw-ready 前にも動くため診断専用。
    // 安定サンプルは別途 EvaluateCaptureSampleStreak。
    public static string EvaluateCaptureValidity(
        bool mappingHit,
        bool objectResolved,
        bool drawReady,
        int activeSceneGeneration,
        int runtimeSceneGeneration,
        Vector3 position,
        float rotation)
    {
        if (!mappingHit)
        {
            return "mapping-miss";
        }

        if (!objectResolved)
        {
            return "object-unresolved";
        }

        _ = drawReady;

        if (activeSceneGeneration <= 0
            || runtimeSceneGeneration <= 0
            || activeSceneGeneration != runtimeSceneGeneration)
        {
            return "scene-generation-mismatch";
        }

        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)
            || !float.IsFinite(rotation))
        {
            return "non-finite-transform";
        }

        return "ok";
    }

    // 連続フレーム間で transform が安定しているか（過渡フレーム棄却）。
    // 有効フレームなら streak をインクリメントした値、そうでなければ 0 を返す。
    public static int EvaluateCaptureSampleStreak(
        bool hasPreviousSample,
        Vector3 previousPosition,
        float previousRotation,
        Vector3 currentPosition,
        float currentRotation,
        int currentStreak)
    {
        if (!float.IsFinite(currentPosition.X) || !float.IsFinite(currentPosition.Y)
            || !float.IsFinite(currentPosition.Z) || !float.IsFinite(currentRotation))
        {
            return 0;
        }

        if (!hasPreviousSample)
        {
            return 1;
        }

        var dx = Math.Abs(currentPosition.X - previousPosition.X);
        var dy = Math.Abs(currentPosition.Y - previousPosition.Y);
        var dz = Math.Abs(currentPosition.Z - previousPosition.Z);
        var dr = Math.Abs(NormalizeAngleDelta(currentRotation - previousRotation));

        var stable = dx < CapturePositionEpsilon
            && dy < CapturePositionEpsilon
            && dz < CapturePositionEpsilon
            && dr < CaptureRotationEpsilon;
        return stable ? currentStreak + 1 : 1;
    }

    public static bool IsCaptureStreakSatisfied(int streak) => streak >= CaptureStableSampleTarget;

    public static bool IsCaptureBudgetExceeded(int framesElapsed) => framesElapsed >= CaptureFrameBudget;

    public static bool IsPositionReadbackWithinEpsilon(Vector3 expected, Vector3 actual)
    {
        return IsFiniteTransform(actual, 0f)
            && Math.Abs(expected.X - actual.X) <= CapturePositionEpsilon
            && Math.Abs(expected.Y - actual.Y) <= CapturePositionEpsilon
            && Math.Abs(expected.Z - actual.Z) <= CapturePositionEpsilon;
    }

    public static bool IsRotationReadbackWithinEpsilon(float expected, float actual)
    {
        return float.IsFinite(actual)
            && float.IsFinite(expected)
            && Math.Abs(NormalizeAngleDelta(actual - expected)) <= CaptureRotationEpsilon;
    }

    // Proof 成功後の通常設定への昇格判定。ここでは config を変更しない。
    // partial / capture 未成立 / setter-readback 未確認 / login stop 未成立のいずれかなら
    // candidate を返さず、既存の保存値を保護する。
    public static TitleBackgroundCharaSelectPlacementPromotionDecision EvaluatePromotion(
        TitleBackgroundCharaSelectPlacementProofSnapshot proof,
        bool partial,
        TitleBackgroundQuickCheckLevel quickCheckLevel,
        bool candidateMatches,
        bool targetFinite,
        string postLoginLeakStatus)
    {
        if (partial)
        {
            return new(false, "partial-run");
        }

        if (!string.Equals(proof.EngineOwner, "placement-proof", StringComparison.Ordinal)
            || !proof.PlacementProofArmed)
        {
            return new(false, "proof-owner-not-active");
        }

        if (!candidateMatches || !proof.CandidateMatches)
        {
            return new(false, "candidate-mismatch");
        }

        if (proof.SceneGenerationObserved <= 0)
        {
            return new(false, "scene-generation-not-observed");
        }

        if (!proof.CharaSelectSessionObserved || !proof.AttachedToActiveScene)
        {
            return new(false, "scene-lifecycle-not-observed");
        }

        if (!string.Equals(proof.CharacterResolveStatus, "resolved", StringComparison.Ordinal)
            || !proof.PreLoginPlacementObserved
            || !proof.MappingHit
            || !proof.ClientObjectIndexValid
            || !proof.ObjectResolved)
        {
            return new(false, "character-unresolved");
        }

        if (!proof.PositionCaptured
            || proof.CaptureStableSamples < CaptureStableSampleTarget
            || !targetFinite)
        {
            return new(false, "stable-capture-not-complete");
        }

        if (proof.ApplyCount <= 0 || !proof.WriteConfirmed)
        {
            return new(false, "placement-readback-not-confirmed");
        }

        if (!proof.LegacyOwnershipInactive)
        {
            return new(false, "legacy-ownership-active");
        }

        if (!proof.LogoutTransitionObserved || !proof.LoginStopped)
        {
            return new(false, "login-stop-not-observed");
        }

        if (!string.Equals(postLoginLeakStatus, "none", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, "post-login-leak");
        }

        if (quickCheckLevel != TitleBackgroundQuickCheckLevel.OK)
        {
            return new(false, $"quickcheck-{quickCheckLevel.ToString().ToLowerInvariant()}");
        }

        return new(true, "eligible");
    }

    // config の保存値から source-backed な LocationModel-lite を組み立てる。
    // capture フラグが false / 座標非有限 / territoryPath 空なら HasSourceBackedPosition=false になり
    // ResolveGate が Skip("no-source-backed-position") を返す。
    public static TitleBackgroundCharaSelectLocationModel BuildLocationModel(
        string territoryPath,
        uint layoutTerritoryTypeId,
        uint layoutLayerFilterKey,
        bool positionCaptured,
        float posX,
        float posY,
        float posZ,
        float rotation)
    {
        if (!positionCaptured)
        {
            return new TitleBackgroundCharaSelectLocationModel(
                territoryPath ?? string.Empty,
                layoutTerritoryTypeId,
                layoutLayerFilterKey,
                new Vector3(float.NaN, float.NaN, float.NaN),
                float.NaN);
        }

        return new TitleBackgroundCharaSelectLocationModel(
            territoryPath ?? string.Empty,
            layoutTerritoryTypeId,
            layoutLayerFilterKey,
            new Vector3(
                TitleBackgroundPreset.SanitizeCoordinate(posX),
                TitleBackgroundPreset.SanitizeCoordinate(posY),
                TitleBackgroundPreset.SanitizeCoordinate(posZ)),
            TitleBackgroundPreset.SanitizeCoordinate(rotation));
    }

    public static bool IsCharaSelectMap(GameLobbyType map)
    {
        return map == GameLobbyType.CharaSelect;
    }

    private static float NormalizeAngleDelta(float delta)
    {
        const float twoPi = (float)(2 * Math.PI);
        delta %= twoPi;
        if (delta > Math.PI)
        {
            delta -= twoPi;
        }
        else if (delta < -Math.PI)
        {
            delta += twoPi;
        }

        return delta;
    }

    private static bool IsFiniteTransform(Vector3 position, float rotation)
    {
        return float.IsFinite(position.X)
            && float.IsFinite(position.Y)
            && float.IsFinite(position.Z)
            && (rotation == 0f || float.IsFinite(rotation));
    }
}
