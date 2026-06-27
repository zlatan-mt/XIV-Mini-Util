// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectAnchor.cs
// Description: Character Select の陸上アンカー（固定立ち位置）解決ロジック
// Reason: カメラ非干渉を保ったまま、湖上ではなく候補固有の陸上地点へキャラを配置する判定を実機なしでテスト可能にするため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

public enum TitleBackgroundCharaSelectAnchorAxis
{
    X,
    Y,
    Z,
    Rotation,
}

// capture ボタンの有効/無効と、無効時にユーザーへ出す理由。
// UI が押下可否と文言を一貫して出すための純粋判定。
internal enum TitleBackgroundAnchorCaptureAvailability
{
    Available,
    NotCharaSelect,
    LoggedIn,
}

internal static class TitleBackgroundAnchorCaptureGate
{
    public static TitleBackgroundAnchorCaptureAvailability Evaluate(bool isLoggedIn, bool isCharaSelect)
    {
        if (isLoggedIn)
        {
            return TitleBackgroundAnchorCaptureAvailability.LoggedIn;
        }

        if (!isCharaSelect)
        {
            return TitleBackgroundAnchorCaptureAvailability.NotCharaSelect;
        }

        return TitleBackgroundAnchorCaptureAvailability.Available;
    }

    public static bool IsCaptureEnabled(TitleBackgroundAnchorCaptureAvailability availability)
    {
        return availability == TitleBackgroundAnchorCaptureAvailability.Available;
    }
}

// layer 番号順送り（layer 一覧が取得できない場合の必須フォールバック）。
// 既知の有効値リストが無いので ±1 で素朴に増減し、0 を下限にする。
internal static class TitleBackgroundLayerStepLogic
{
    public static uint Step(uint current, int direction)
    {
        if (direction < 0)
        {
            return current == 0 ? 0 : current - 1;
        }

        if (direction > 0)
        {
            return current >= uint.MaxValue ? current : current + 1;
        }

        return current;
    }
}

// 候補固有の陸上アンカー。位置はゲーム内 capture で確定し、nudge で微調整する。
// Rotation は将来のために保持するが、現状の placement では position のみを書き込む
// （毎フレームの rotation 書き込みはエンジンと競合してガクつく恐れがあるため意図的に未適用）。
internal readonly record struct TitleBackgroundCharaSelectAnchor(
    bool Enabled,
    string CandidateId,
    Vector3 Position,
    float Rotation)
{
    public static TitleBackgroundCharaSelectAnchor None { get; } =
        new(false, string.Empty, Vector3.Zero, 0f);

    public bool HasUsableAnchor =>
        Enabled
        && TitleBackgroundCameraMath.IsFiniteVector(Position)
        && float.IsFinite(Rotation);
}

internal readonly record struct TitleBackgroundCharaSelectPlacementResolution(
    Vector3 Target,
    string Source,
    bool UsedAnchor);

// FixOn detour から呼ぶ焦点 override の純粋判定。
// camera 位置と fovY はゲーム値を尊重し、focus（注視点）だけを差し替える。
internal readonly record struct TitleBackgroundFixOnFocusResolution(
    bool ShouldOverride,
    Vector3 Focus,
    string Source);

// アンカー取得元のフレーム種別。診断で world 座標と lobby 座標を混同しないために使う。
internal static class TitleBackgroundCharaSelectAnchorFrame
{
    // ログイン中の LocalPlayer.Position（ワールド座標）。lobby 空間とは別フレームの可能性がある。
    public const string World = "world";
    // CharaSelect のロビー空間から直接取得した座標。
    public const string LobbyNative = "lobby-native";
    // CharaSelect 中に native draw 位置を読んだ値。placement 強制配置中は fallback の再保存になる。
    public const string CharaSelectFallback = "chara-select-fallback";
    public const string Unknown = "unknown";

    public static bool IsPlacementSupported(string? frame)
    {
        return string.Equals(frame, LobbyNative, StringComparison.Ordinal)
            || string.Equals(frame, CharaSelectFallback, StringComparison.Ordinal);
    }

    // 地面 provenance が明確な frame か。placement-supported より厳しい。
    // CharaSelectFallback は camera-focus で水上へ強制配置された座標の再保存の可能性があり、
    // World（実験経路）/ Unknown も出所が確定していないため、地面確認済みとしては扱わない。
    // 現状、明確な地面 provenance を持つのは lobby 空間から直接取得した LobbyNative のみ。
    public static bool HasGroundProvenance(string? frame)
    {
        return string.Equals(frame, LobbyNative, StringComparison.Ordinal);
    }
}

// 「今の見え方を保存」した CharaSelect カメラ（scene-local 絶対値）。TitleEdit の CameraPos/FixOnPos/FovY 相当。
internal readonly record struct TitleBackgroundCharaSelectView(
    bool Enabled,
    string CandidateId,
    Vector3 Camera,
    Vector3 Focus,
    float FovY)
{
    public static TitleBackgroundCharaSelectView None { get; } =
        new(false, string.Empty, Vector3.Zero, Vector3.Zero, TitleBackgroundPreset.DefaultFovY);

    public bool HasUsableView =>
        Enabled
        && TitleBackgroundCameraMath.IsFiniteVector(Camera)
        && TitleBackgroundCameraMath.IsFiniteVector(Focus)
        && float.IsFinite(FovY)
        && FovY > 0f;
}

internal readonly record struct TitleBackgroundFixOnViewResolution(
    bool ShouldOverride,
    Vector3 Camera,
    Vector3 Focus,
    float FovY,
    string Source);

// FixOn detour から呼ぶ「見え方」上書きの純粋判定。TitleEdit と同様に camera/focus/fov を
// まとめて絶対値で差し替える（観測値からの相対ではない）。候補は非空・完全一致のみ。
internal static class TitleBackgroundFixOnViewOverrideLogic
{
    public const string ViewSource = "view";
    public const string PassthroughSource = "passthrough";

    public static TitleBackgroundFixOnViewResolution Resolve(
        bool featureEnabled,
        TitleBackgroundCharaSelectView view,
        string? activeCandidateId,
        Vector3 observedCamera,
        Vector3 observedFocus,
        float observedFovY)
    {
        if (featureEnabled
            && view.HasUsableView
            && MatchesCandidateStrict(view.CandidateId, activeCandidateId))
        {
            return new TitleBackgroundFixOnViewResolution(
                true,
                view.Camera,
                view.Focus,
                view.FovY,
                ViewSource);
        }

        return new TitleBackgroundFixOnViewResolution(
            false,
            observedCamera,
            observedFocus,
            observedFovY,
            PassthroughSource);
    }

    // 安全側に倒し、空 CandidateId のワイルドカード一致は許さない（別背景に適用させない）。
    private static bool MatchesCandidateStrict(string viewCandidateId, string? activeCandidateId)
    {
        var normalized =
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(viewCandidateId);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        return string.Equals(
            normalized,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(activeCandidateId),
            StringComparison.Ordinal);
    }
}

internal static class TitleBackgroundFixOnFocusOverrideLogic
{
    public const string AnchorSource = "anchor";
    public const string PassthroughSource = "passthrough";

    // 診断用: 焦点 override の適用可否理由を1語で返す純粋関数。
    // 適用条件（feature ON / passive OFF / 実行コンテキスト ready）と完全に一致し、
    // "ready" を返すときだけ detour が override を適用する。
    public const string GateReady = "ready";
    public const string GateFeatureOff = "feature-off";
    public const string GatePassivePrecedence = "passive-precedence";

    public static string DescribeGateReason(
        bool passiveObservationEnabled,
        bool focusAnchorOverrideEnabled,
        bool contextReady,
        string contextReason)
    {
        if (!focusAnchorOverrideEnabled)
        {
            return GateFeatureOff;
        }

        if (passiveObservationEnabled)
        {
            return GatePassivePrecedence;
        }

        return contextReady ? GateReady : contextReason;
    }

    // passive 観測（上書きしない）を最優先する。passive ON のときは UI に「override なし」と
    // 表示されている以上、focus override は絶対に行わない。
    public static bool ShouldConsiderFocusOverride(
        bool passiveObservationEnabled,
        bool focusAnchorOverrideEnabled)
    {
        return focusAnchorOverrideEnabled && !passiveObservationEnabled;
    }

    // FixOn はシーン読み込みの最中に1回発火し、その時点で CurrentLobbyMap は None へ戻り得る。
    // よって CurrentLobbyMap には依存せず、LoadLobbyScene 時点で original 呼び出し前に確定する
    // セッション状態（session active / scene generation の一致 / CharaSelect セッション）でゲートする。
    // bridgeActive は呼び出し側で composition bridge 設定をまとめて評価して渡す。
    public static bool IsExecutionContextReady(
        bool isLoggedIn,
        bool serviceReady,
        bool bridgeActive,
        bool sessionActive,
        int activeSceneGeneration,
        int currentSceneGeneration,
        bool charaSelectSessionLobby)
    {
        return !isLoggedIn
            && serviceReady
            && bridgeActive
            && sessionActive
            && activeSceneGeneration > 0
            && currentSceneGeneration == activeSceneGeneration
            && charaSelectSessionLobby;
    }

    // 専用フラグ ON かつアンカーが使え、かつ現在候補に「非空・完全一致」するときだけ、
    // ゲームが渡してきた焦点を保存済み陸上アンカー由来の座標へ差し替える。
    // それ以外は observedFocus をそのまま通す（passive 観測と同じ非干渉）。
    // アンカーはキャラの足元座標なので、焦点はキャラ胴体（足元 + bodyDrop）を向くよう Y を持ち上げ、
    // 水平は X/Z をアンカーへ寄せる。これで「足元を見下ろす」構図を避ける。
    public static TitleBackgroundFixOnFocusResolution Resolve(
        bool featureEnabled,
        TitleBackgroundCharaSelectAnchor anchor,
        string? activeCandidateId,
        Vector3 observedFocus,
        float bodyDrop)
    {
        if (featureEnabled
            && anchor.HasUsableAnchor
            && TitleBackgroundCameraMath.IsFiniteVector(anchor.Position)
            && float.IsFinite(bodyDrop)
            && MatchesCandidateStrict(anchor.CandidateId, activeCandidateId))
        {
            var focus = new Vector3(
                anchor.Position.X,
                anchor.Position.Y + bodyDrop,
                anchor.Position.Z);
            if (TitleBackgroundCameraMath.IsFiniteVector(focus))
            {
                return new TitleBackgroundFixOnFocusResolution(true, focus, AnchorSource);
            }
        }

        return new TitleBackgroundFixOnFocusResolution(false, observedFocus, PassthroughSource);
    }

    // カメラ焦点 override は安全側に倒し、空 CandidateId のワイルドカード一致を許さない。
    // 設定移行や手動編集で空 ID の有効アンカーが残っても、別背景の焦点を書き換えないようにする。
    private static bool MatchesCandidateStrict(string anchorCandidateId, string? activeCandidateId)
    {
        var normalizedAnchor =
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(anchorCandidateId);
        if (string.IsNullOrEmpty(normalizedAnchor))
        {
            return false;
        }

        return string.Equals(
            normalizedAnchor,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(activeCandidateId),
            StringComparison.Ordinal);
    }
}

internal static class TitleBackgroundCharaSelectAnchorLogic
{
    public const string AnchorSource = "anchor";
    public const string CameraFocusSource = "camera-focus";

    public const float DefaultPositionNudgeStep = 0.1f;

    // 5 度ぶんのラジアン。回転の微調整単位（適用は将来対応）。
    public const float DefaultRotationNudgeStep = 0.0872664626f;

    public static TitleBackgroundCharaSelectAnchor CaptureFromDrawPosition(
        string? candidateId,
        Vector3 position,
        float rotation)
    {
        if (!TitleBackgroundCameraMath.IsFiniteVector(position) || !float.IsFinite(rotation))
        {
            return TitleBackgroundCharaSelectAnchor.None;
        }

        return new TitleBackgroundCharaSelectAnchor(
            true,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(candidateId),
            position,
            TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(rotation));
    }

    public static TitleBackgroundCharaSelectAnchor ApplyNudge(
        TitleBackgroundCharaSelectAnchor anchor,
        TitleBackgroundCharaSelectAnchorAxis axis,
        float delta)
    {
        if (!float.IsFinite(delta))
        {
            return anchor;
        }

        var position = anchor.Position;
        var rotation = anchor.Rotation;
        switch (axis)
        {
            case TitleBackgroundCharaSelectAnchorAxis.X:
                position.X += delta;
                break;
            case TitleBackgroundCharaSelectAnchorAxis.Y:
                position.Y += delta;
                break;
            case TitleBackgroundCharaSelectAnchorAxis.Z:
                position.Z += delta;
                break;
            case TitleBackgroundCharaSelectAnchorAxis.Rotation:
                rotation = TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(rotation + delta);
                break;
        }

        if (!TitleBackgroundCameraMath.IsFiniteVector(position) || !float.IsFinite(rotation))
        {
            return anchor;
        }

        // nudge は既存アンカーを有効化したまま編集する（capture 前の nudge は無効のまま）。
        return anchor with { Position = position, Rotation = rotation };
    }

    // placement の毎フレーム呼び出しから使う。アンカーが使え、かつ現在の候補に一致するなら
    // アンカー固定座標を返す。そうでなければ従来どおりカメラ注視点ベースのフォールバックを返す。
    public static TitleBackgroundCharaSelectPlacementResolution ResolvePlacementTarget(
        TitleBackgroundCharaSelectAnchor anchor,
        string? activeCandidateId,
        Vector3 cameraLookAt,
        float bodyDrop)
    {
        if (anchor.HasUsableAnchor && AnchorMatchesCandidate(anchor, activeCandidateId))
        {
            return new TitleBackgroundCharaSelectPlacementResolution(anchor.Position, AnchorSource, true);
        }

        var fallback = new Vector3(cameraLookAt.X, cameraLookAt.Y - bodyDrop, cameraLookAt.Z);
        return new TitleBackgroundCharaSelectPlacementResolution(fallback, CameraFocusSource, false);
    }

    public static bool AnchorMatchesCandidate(
        TitleBackgroundCharaSelectAnchor anchor,
        string? activeCandidateId)
    {
        // CandidateId が空のアンカーは、どの候補にも適用される（手動 capture 後の汎用扱い）。
        if (string.IsNullOrEmpty(anchor.CandidateId))
        {
            return true;
        }

        return string.Equals(
            anchor.CandidateId,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(activeCandidateId),
            StringComparison.Ordinal);
    }
}
