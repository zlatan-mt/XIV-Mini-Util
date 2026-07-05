// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundViewReplayTraceRuntimeState.cs
// Description: 保存view再現バグ診断（FixOn view override適用直後〜後続フレームのSceneCamera実値トレース）の
//              run-scoped状態を保持する
// Reason: scene load毎にリセットされる診断専用の可変状態を、既存パターンに倣い専用holderへ集約するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

// 保存 view pose または FixOn view override が成立した scene generation に限定して、その後のカメラ実値を
// read-only で少数フレームだけ記録する。view 非関与 load では消さず、次の view 関与 load まで保持する。
// カメラへの書き込みは一切行わない。上限件数は TitleBackgroundViewReplayTraceLogic.MaxSampleCount で固定。
internal sealed class TitleBackgroundViewReplayTraceRuntimeState
{
    // trace が有効化された scene generation。
    public int TraceSceneGeneration { get; set; }

    public string Source { get; set; } = "not-run";

    public bool StartedDuringAutomaticRun { get; set; }

    // 保存 view / FixOn へ実際に適用した値（この値との乖離を判定する基準）。
    public Vector3? TargetCamera { get; set; }

    public Vector3? TargetFocus { get; set; }

    public float? TargetFovY { get; set; }

    public float? TargetDirH { get; set; }

    public float? TargetDirV { get; set; }

    public float? TargetDistance { get; set; }

    // -1 は非アクティブ（trace未開始、完了後、または次sceneへ遷移後）。0 以上は trace 開始からの相対フレーム数。
    public int RelativeFrameCounter { get; set; } = -1;

    // trace開始（FixOn view override適用）時点の Phase2C 絶対フレーム。相対フレーム（FixOn=0起点）と
    // Phase2E/2F 記録の絶対フレームを同一軸で突き合わせるための基準（Phase2C未開始なら null）。
    public int? StartAbsoluteFrame { get; set; }

    // trace開始時点の累計フックカウンタ（ベースライン）。サンプル側の累計値との差分で
    // 「trace開始以降に何回動いたか」を読むためのもの。
    public int StartLookAtYCallCount { get; set; }

    public int StartCurveSetMidCallCount { get; set; }

    public int StartCurveLowHighCallCount { get; set; }

    public int? PoseApplyAbsoluteFrame { get; set; }

    public int? FixOnApplyAbsoluteFrame { get; set; }

    public Dictionary<int, TitleBackgroundViewReplayTraceSample> Samples { get; } = [];

    public string Status { get; set; } = "not-run";

    public bool IsActive => RelativeFrameCounter >= 0;

    public void Reset()
    {
        TraceSceneGeneration = 0;
        Source = "not-run";
        StartedDuringAutomaticRun = false;
        TargetCamera = null;
        TargetFocus = null;
        TargetFovY = null;
        TargetDirH = null;
        TargetDirV = null;
        TargetDistance = null;
        RelativeFrameCounter = -1;
        StartAbsoluteFrame = null;
        StartLookAtYCallCount = 0;
        StartCurveSetMidCallCount = 0;
        StartCurveLowHighCallCount = 0;
        PoseApplyAbsoluteFrame = null;
        FixOnApplyAbsoluteFrame = null;
        Samples.Clear();
        Status = "not-run";
    }

    public void StopCollectionForSceneChange()
    {
        if (!IsActive)
        {
            return;
        }

        RelativeFrameCounter = -1;
        Status = Samples.Count > 0 ? "interrupted-scene-change" : "not-run";
    }

    public void PrepareForSceneLoad(int activeSceneGeneration)
    {
        // adapter reset後はgenerationが0から再採番される。同じ番号のtraceがload開始時点で残っていれば
        // 同一loadではなく旧epochとの衝突なので破棄する（traceはscene-ready以降にしか開始されない）。
        if (TraceSceneGeneration != 0 && TraceSceneGeneration == activeSceneGeneration)
        {
            Reset();
            return;
        }

        StopCollectionForSceneChange();
    }
}
