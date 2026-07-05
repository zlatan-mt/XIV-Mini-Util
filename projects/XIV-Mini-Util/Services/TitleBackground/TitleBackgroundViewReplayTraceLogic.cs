// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundViewReplayTraceLogic.cs
// Description: 保存view再現バグ診断のための、FixOn view override適用直後〜後続フレームのSceneCamera
//              実値サンプリングフレーム選定と、保存/適用値からの最初の乖離判定を行う純粋ロジック
// Reason: 「保存構図がFixOn適用後に何かへ上書きされているか」を実機レポート1本で切り分けるため。
//         native read・書込は行わず、フレーム選定と数値比較のみを担う純粋関数として単体テスト可能にする。
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

// 乖離判定の対象成分。camera position / lookAt(focus) / fovY を独立に比較する。
internal enum TitleBackgroundViewReplayTraceComponent
{
    None,
    Camera,
    LookAt,
    FovY,
}

// 1サンプルの読み取り結果（read-only）。相対フレーム番号は FixOn view override 適用時点を 0 とする。
// AbsoluteFrame は Phase2C タイムライン基準（GetCurrentPhase2CFrame()）の絶対フレームで、
// Phase2E/2F 記録の frame（同じ Phase2C 基準）と突き合わせるための共通軸（未確立時は null）。
// LookAtYCalls / CurveSetMidCalls / CurveLowHighCalls は採取時点の累計フックカウンタで、
// 「乖離した最初のサンプルとその直前サンプルの間にどのフックが動いたか」を区間差分で直接読むためのもの。
internal readonly record struct TitleBackgroundViewReplayTraceSample(
    int RelativeFrame,
    bool Captured,
    Vector3? SceneCameraPosition,
    Vector3? LookAtVector,
    float? FovY,
    string Status,
    string Error,
    int? AbsoluteFrame = null,
    int LookAtYCalls = 0,
    int CurveSetMidCalls = 0,
    int CurveLowHighCalls = 0,
    float? DirH = null,
    float? DirV = null,
    float? Distance = null);

// 乖離判定の結果。DivergedAtFrame が null なら、採取した全サンプルが許容誤差内で一致したことを意味する
// （「乖離なし」と「まだサンプルが無い」は Samples.Count で読み手が区別する）。
internal readonly record struct TitleBackgroundViewReplayTraceDivergence(
    bool Diverged,
    int? DivergedAtFrame,
    TitleBackgroundViewReplayTraceComponent Component,
    float Magnitude);

internal static class TitleBackgroundViewReplayTraceLogic
{
    // 要求仕様: 相対フレーム 1〜10 は毎フレーム、以降は疎に 15/20/30/45/60/90/120。
    // 0 (FixOn直後 = CapturePostFixOnCameraState 相当) は別枠で常に採取するため、ここには含めない。
    // 上限フレーム 120 でメモリ無制限成長を防ぐ（Phase2Fの Interesting 相当の考え方を踏襲）。
    public static readonly int[] SamplingFrames =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
        15, 20, 30, 45, 60, 90, 120,
    ];

    public const int MaxSampleCount = 18; // frame 0 + SamplingFrames.Length(17) の固定上限。

    // 既定の成分別許容誤差。camera/focus は world 座標系のスケール（メートル相当）で 0.05、
    // fovY はラジアン相当の小さい値なので同じ 0.05 を流用する（要求仕様の例示値をそのまま採用）。
    public const float DefaultTolerance = 0.05f;

    public static bool ShouldCaptureAtFrame(int relativeFrame)
    {
        return relativeFrame == 0 || Array.IndexOf(SamplingFrames, relativeFrame) >= 0;
    }

    public static bool IsTraceComplete(int relativeFrame)
    {
        return relativeFrame >= SamplingFrames[^1];
    }

    // target は FixOn へ実際に渡した override 値（_cameraObservation.LastAppliedCamera/Focus/FovY）。
    // samples は相対フレーム昇順である必要はない（呼び出し側が Dictionary から取り出すため、ここで整列する）。
    public static TitleBackgroundViewReplayTraceDivergence EvaluateFirstDivergence(
        Vector3? targetCamera,
        Vector3? targetFocus,
        float? targetFovY,
        IReadOnlyList<TitleBackgroundViewReplayTraceSample> samples,
        float tolerance = DefaultTolerance)
    {
        foreach (var sample in samples.OrderBy(sample => sample.RelativeFrame))
        {
            if (!sample.Captured)
            {
                continue;
            }

            if (TryFindComponentDivergence(
                    targetCamera,
                    targetFocus,
                    targetFovY,
                    sample,
                    tolerance,
                    out var component,
                    out var magnitude))
            {
                return new TitleBackgroundViewReplayTraceDivergence(true, sample.RelativeFrame, component, magnitude);
            }
        }

        return new TitleBackgroundViewReplayTraceDivergence(false, null, TitleBackgroundViewReplayTraceComponent.None, 0f);
    }

    private static bool TryFindComponentDivergence(
        Vector3? targetCamera,
        Vector3? targetFocus,
        float? targetFovY,
        TitleBackgroundViewReplayTraceSample sample,
        float tolerance,
        out TitleBackgroundViewReplayTraceComponent component,
        out float magnitude)
    {
        if (targetCamera.HasValue
            && sample.SceneCameraPosition.HasValue
            && TryGetMaxAxisDelta(targetCamera.Value, sample.SceneCameraPosition.Value, out var cameraDelta)
            && cameraDelta > tolerance)
        {
            component = TitleBackgroundViewReplayTraceComponent.Camera;
            magnitude = cameraDelta;
            return true;
        }

        if (targetFocus.HasValue
            && sample.LookAtVector.HasValue
            && TryGetMaxAxisDelta(targetFocus.Value, sample.LookAtVector.Value, out var focusDelta)
            && focusDelta > tolerance)
        {
            component = TitleBackgroundViewReplayTraceComponent.LookAt;
            magnitude = focusDelta;
            return true;
        }

        if (targetFovY.HasValue
            && sample.FovY.HasValue
            && Math.Abs(sample.FovY.Value - targetFovY.Value) > tolerance)
        {
            component = TitleBackgroundViewReplayTraceComponent.FovY;
            magnitude = Math.Abs(sample.FovY.Value - targetFovY.Value);
            return true;
        }

        component = TitleBackgroundViewReplayTraceComponent.None;
        magnitude = 0f;
        return false;
    }

    private static bool TryGetMaxAxisDelta(Vector3 expected, Vector3 actual, out float maxDelta)
    {
        if (!TitleBackgroundCameraMath.IsFiniteVector(expected) || !TitleBackgroundCameraMath.IsFiniteVector(actual))
        {
            maxDelta = 0f;
            return false;
        }

        maxDelta = Math.Max(Math.Abs(expected.X - actual.X), Math.Max(Math.Abs(expected.Y - actual.Y), Math.Abs(expected.Z - actual.Z)));
        return true;
    }
}
