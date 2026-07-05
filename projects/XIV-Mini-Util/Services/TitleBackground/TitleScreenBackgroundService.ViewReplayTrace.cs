// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.ViewReplayTrace.cs
// Description: 保存view再現バグ診断（pose/FixOn適用直後〜後続フレームのカメラ実値trace）を
//              view関与load単位で採取・保持し、最初に乖離したフレーム・成分・量を自動レポートへ出す
// Reason: 「現在の構図を保存」した構図がFixOn適用後に上書きされているかを、実機の1クリック確認だけで
//         切り分けられるようにするため。カメラへの書き込みは一切行わない read-only 計装。
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    // FixOn detour が view override を成立させた呼び出しの直後にだけ呼ぶ。同一 load で保存 pose trace が
    // 既に始まっている場合はリセットせず、FixOn の発火フレームを同じ時間軸へ追記する。
    private void StartViewReplayTraceIfApplicable(bool viewOverrideAppliedThisInvocation)
    {
        if (!viewOverrideAppliedThisInvocation)
        {
            return;
        }

        var absoluteFrame = GetCurrentPhase2CFrame();
        if (_viewReplayTrace.TraceSceneGeneration != _activeCharaSelectSceneGeneration)
        {
            InitializeViewReplayTrace(
                "fix-on-view-override",
                _cameraObservation.LastAppliedCamera,
                _cameraObservation.LastAppliedFocus,
                _cameraObservation.LastAppliedFovY,
                null,
                null,
                null);
        }
        else if (string.Equals(_viewReplayTrace.Source, "saved-view-pose", StringComparison.Ordinal))
        {
            _viewReplayTrace.Source = "saved-view-pose+fix-on-view-override";
        }

        _viewReplayTrace.FixOnApplyAbsoluteFrame ??= absoluteFrame;
    }

    // scene-ready 後の保存 pose 書込成功直後に開始する。target の pose 値と frame 0 の read-back を同時に残す。
    private void StartViewReplayTraceForSavedPose(TitleBackgroundCharaSelectView savedView)
    {
        if (_viewReplayTrace.TraceSceneGeneration != _activeCharaSelectSceneGeneration)
        {
            InitializeViewReplayTrace(
                "saved-view-pose",
                savedView.Camera,
                savedView.Focus,
                savedView.FovY,
                savedView.DirH,
                savedView.DirV,
                savedView.Distance);
        }
        else
        {
            _viewReplayTrace.Source = "fix-on-view-override+saved-view-pose";
            _viewReplayTrace.TargetDirH = savedView.DirH;
            _viewReplayTrace.TargetDirV = savedView.DirV;
            _viewReplayTrace.TargetDistance = savedView.Distance;
        }

        _viewReplayTrace.PoseApplyAbsoluteFrame = GetCurrentPhase2CFrame();
    }

    private void InitializeViewReplayTrace(
        string source,
        Vector3? targetCamera,
        Vector3? targetFocus,
        float? targetFovY,
        float? targetDirH,
        float? targetDirV,
        float? targetDistance)
    {
        _viewReplayTrace.Reset();
        _viewReplayTrace.TraceSceneGeneration = _activeCharaSelectSceneGeneration;
        _viewReplayTrace.Source = source;
        _viewReplayTrace.StartedDuringAutomaticRun = _automaticCheck.Requested;
        _viewReplayTrace.TargetCamera = targetCamera;
        _viewReplayTrace.TargetFocus = targetFocus;
        _viewReplayTrace.TargetFovY = targetFovY;
        _viewReplayTrace.TargetDirH = targetDirH;
        _viewReplayTrace.TargetDirV = targetDirV;
        _viewReplayTrace.TargetDistance = targetDistance;
        _viewReplayTrace.RelativeFrameCounter = 0;
        _viewReplayTrace.Status = "collecting";
        _viewReplayTrace.StartAbsoluteFrame = GetCurrentPhase2CFrame();
        _viewReplayTrace.StartLookAtYCallCount = _phaseRecording.Phase2ECalculateLookAtYCallCount;
        _viewReplayTrace.StartCurveSetMidCallCount = _phaseRecording.Phase2FSetCameraCurveMidPointCallCount;
        _viewReplayTrace.StartCurveLowHighCallCount = _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointCallCount;
        _viewReplayTrace.Samples[0] = CaptureViewReplayTraceSample(0);
    }

    // OnFrameworkUpdate から毎フレーム呼ぶ。pre-login かつ同一 scene generation のときだけ採取する
    // （post-login のライブカメラ読み取りは構造上無効なため、既存 CapturePreLoginCameraOnFrameworkUpdate と
    // 同じゲートパターンを踏襲する）。上限フレームに達したら自動的に停止し、以降は何もしない。
    private void CaptureViewReplayTraceOnFrameworkUpdate()
    {
        if (!_viewReplayTrace.IsActive)
        {
            return;
        }

        if (_clientState.IsLoggedIn || !_charaSelectTitleBackgroundSessionActive)
        {
            return;
        }

        if (_viewReplayTrace.TraceSceneGeneration <= 0
            || _charaSelectCameraAdapter.RuntimeState.SceneGeneration != _viewReplayTrace.TraceSceneGeneration)
        {
            return;
        }

        _viewReplayTrace.RelativeFrameCounter++;
        var relativeFrame = _viewReplayTrace.RelativeFrameCounter;
        if (TitleBackgroundViewReplayTraceLogic.ShouldCaptureAtFrame(relativeFrame)
            && !_viewReplayTrace.Samples.ContainsKey(relativeFrame))
        {
            _viewReplayTrace.Samples[relativeFrame] = CaptureViewReplayTraceSample(relativeFrame);
        }

        if (TitleBackgroundViewReplayTraceLogic.IsTraceComplete(relativeFrame))
        {
            _viewReplayTrace.Status = "complete";
            _viewReplayTrace.RelativeFrameCounter = -1;
        }
    }

    private TitleBackgroundViewReplayTraceSample CaptureViewReplayTraceSample(int relativeFrame)
    {
        // 採取時点の絶対フレーム（Phase2C基準）と累計フックカウンタを毎サンプルに併記する。
        // 直前サンプルとの差分で「この区間にどのフックが何回動いたか」を直接読めるようにするため。
        var absoluteFrame = GetCurrentPhase2CFrame();
        var lookAtYCalls = _phaseRecording.Phase2ECalculateLookAtYCallCount;
        var curveSetMidCalls = _phaseRecording.Phase2FSetCameraCurveMidPointCallCount;
        var curveLowHighCalls = _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointCallCount;
        if (TryCaptureActiveCameraSnapshot(out var snapshot, out var errorMessage))
        {
            return new TitleBackgroundViewReplayTraceSample(
                relativeFrame,
                true,
                snapshot.SceneCameraPosition,
                snapshot.LookAtVector,
                snapshot.FovY,
                "success",
                string.Empty,
                absoluteFrame,
                lookAtYCalls,
                curveSetMidCalls,
                curveLowHighCalls,
                snapshot.DirH,
                snapshot.DirV,
                snapshot.Distance);
        }

        return new TitleBackgroundViewReplayTraceSample(
            relativeFrame,
            false,
            null,
            null,
            null,
            "failed",
            string.IsNullOrWhiteSpace(errorMessage) ? "unknown" : errorMessage,
            absoluteFrame,
            lookAtYCalls,
            curveSetMidCalls,
            curveLowHighCalls);
    }

    // 自動確認レポート向け: trace の全サンプルから最初の乖離を判定し、view.trace.* 診断行を組み立てる。
    private void AddViewReplayTraceLines(List<string> lines)
    {
        var samples = _viewReplayTrace.Samples.Values.OrderBy(sample => sample.RelativeFrame).ToArray();
        var divergence = TitleBackgroundViewReplayTraceLogic.EvaluateFirstDivergence(
            _viewReplayTrace.TargetCamera,
            _viewReplayTrace.TargetFocus,
            _viewReplayTrace.TargetFovY,
            samples);

        lines.Add($"view.trace.status={FormatNone(_viewReplayTrace.Status)}");
        lines.Add($"view.trace.sceneGeneration={_viewReplayTrace.TraceSceneGeneration}");
        // 正常な自動確認runでは両トリガーが抑止されるため True は抑止漏れを示す。
        lines.Add($"view.trace.fromCurrentRun={_viewReplayTrace.StartedDuringAutomaticRun}");
        lines.Add($"view.trace.source={FormatNone(_viewReplayTrace.Source)}");
        lines.Add($"view.trace.poseApplyAbsoluteFrame={(_viewReplayTrace.PoseApplyAbsoluteFrame.HasValue ? _viewReplayTrace.PoseApplyAbsoluteFrame.Value.ToString() : "none")}");
        lines.Add($"view.trace.fixOnApplyAbsoluteFrame={(_viewReplayTrace.FixOnApplyAbsoluteFrame.HasValue ? _viewReplayTrace.FixOnApplyAbsoluteFrame.Value.ToString() : "none")}");
        // trace開始（FixOn view override適用）時点の絶対フレーム（Phase2C基準）とフックカウンタのベースライン。
        // 相対フレームの view.trace.sample[N] と Phase2E/2F 記録（絶対フレーム基準）を同一軸で読むための基準値。
        lines.Add($"view.trace.startAbsoluteFrame={(_viewReplayTrace.StartAbsoluteFrame.HasValue ? _viewReplayTrace.StartAbsoluteFrame.Value.ToString() : "none")}");
        lines.Add($"view.trace.startLookAtYCallCount={_viewReplayTrace.StartLookAtYCallCount}");
        lines.Add($"view.trace.startCurveSetMidCallCount={_viewReplayTrace.StartCurveSetMidCallCount}");
        lines.Add($"view.trace.startCurveLowHighCallCount={_viewReplayTrace.StartCurveLowHighCallCount}");
        lines.Add($"view.trace.targetCamera={FormatVector(_viewReplayTrace.TargetCamera)}");
        lines.Add($"view.trace.targetFocus={FormatVector(_viewReplayTrace.TargetFocus)}");
        lines.Add($"view.trace.targetFovY={(_viewReplayTrace.TargetFovY.HasValue ? _viewReplayTrace.TargetFovY.Value.ToString("0.###") : "none")}");
        lines.Add($"view.trace.targetDirH={(_viewReplayTrace.TargetDirH.HasValue ? _viewReplayTrace.TargetDirH.Value.ToString("0.###") : "none")}");
        lines.Add($"view.trace.targetDirV={(_viewReplayTrace.TargetDirV.HasValue ? _viewReplayTrace.TargetDirV.Value.ToString("0.###") : "none")}");
        lines.Add($"view.trace.targetDistance={(_viewReplayTrace.TargetDistance.HasValue ? _viewReplayTrace.TargetDistance.Value.ToString("0.###") : "none")}");
        lines.Add($"view.trace.sampleCount={samples.Length}");
        lines.Add($"view.trace.firstFrame={(samples.Length > 0 ? samples[0].RelativeFrame.ToString() : "none")}");
        lines.Add($"view.trace.lastFrame={(samples.Length > 0 ? samples[^1].RelativeFrame.ToString() : "none")}");
        lines.Add($"view.trace.diverged={divergence.Diverged}");
        lines.Add($"view.trace.divergedAtFrame={(divergence.DivergedAtFrame.HasValue ? divergence.DivergedAtFrame.Value.ToString() : "none")}");
        lines.Add($"view.trace.divergedComponent={divergence.Component}");
        lines.Add($"view.trace.divergedMagnitude={(divergence.Diverged ? divergence.Magnitude.ToString("0.###") : "none")}");

        // 以下のサマリはレポート生成時点のlive load値。保持trace由来のsample行とは取得loadが異なり得る。
        // 過去loadの因果判定にはsampleごとのカウンタとposeLastRestoreStatusを使う。
        var lastLookAtYCall = _phaseRecording.Phase2ECalculateLookAtYCalls.Count > 0
            ? _phaseRecording.Phase2ECalculateLookAtYCalls[^1]
            : (TitleBackgroundPhase2ECalculateLookAtYCall?)null;
        lines.Add($"view.trace.lookAtYCallCount={_phaseRecording.Phase2ECalculateLookAtYCallCount}");
        lines.Add($"view.trace.lookAtYLastFrame={(lastLookAtYCall.HasValue && lastLookAtYCall.Value.Frame.HasValue ? lastLookAtYCall.Value.Frame.Value.ToString() : "none")}");
        lines.Add($"view.trace.lookAtYLastReturnValue={(lastLookAtYCall.HasValue && lastLookAtYCall.Value.ReturnValue.HasValue ? lastLookAtYCall.Value.ReturnValue.Value.ToString("0.###") : "none")}");
        lines.Add($"view.trace.curveSetMidCallCount={_phaseRecording.Phase2FSetCameraCurveMidPointCallCount}");
        lines.Add($"view.trace.curveLowHighCallCount={_phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointCallCount}");
        lines.Add($"view.trace.curveGenerationOverrideSetMidAppliedCount={_phaseRecording.Phase2GGenerationOverrideSetMidAppliedCount}");
        lines.Add($"view.trace.curveGenerationOverrideLowHighAppliedCount={_phaseRecording.Phase2GGenerationOverrideLowHighAppliedCount}");
        lines.Add($"view.trace.curveGenerationOverrideLastAppliedFrame={(_phaseRecording.Phase2GGenerationOverrideLastAppliedFrame.HasValue ? _phaseRecording.Phase2GGenerationOverrideLastAppliedFrame.Value.ToString() : "none")}");
        lines.Add($"view.trace.runtimeRestoreStatus={FormatNone(_cameraRestoreCurve.LastCharaSelectCameraRuntimeRestoreStatus)}");

        foreach (var sample in samples)
        {
            lines.Add($"view.trace.sample[{sample.RelativeFrame}].status={FormatNone(sample.Status)}");
            lines.Add($"view.trace.sample[{sample.RelativeFrame}].camera={FormatVector(sample.SceneCameraPosition)}");
            lines.Add($"view.trace.sample[{sample.RelativeFrame}].lookAt={FormatVector(sample.LookAtVector)}");
            lines.Add($"view.trace.sample[{sample.RelativeFrame}].fovY={(sample.FovY.HasValue ? sample.FovY.Value.ToString("0.###") : "none")}");
            lines.Add($"view.trace.sample[{sample.RelativeFrame}].dirH={(sample.DirH.HasValue ? sample.DirH.Value.ToString("0.###") : "none")}");
            lines.Add($"view.trace.sample[{sample.RelativeFrame}].dirV={(sample.DirV.HasValue ? sample.DirV.Value.ToString("0.###") : "none")}");
            lines.Add($"view.trace.sample[{sample.RelativeFrame}].distance={(sample.Distance.HasValue ? sample.Distance.Value.ToString("0.###") : "none")}");
            // 絶対フレーム（Phase2C基準）とサンプル採取時点の累計フックカウンタ。直前サンプルとの差分で
            // 「乖離した最初のサンプルとその直前サンプルの間にどのフックが動いたか」を区間ブラケットで読む。
            lines.Add($"view.trace.sample[{sample.RelativeFrame}].absoluteFrame={(sample.AbsoluteFrame.HasValue ? sample.AbsoluteFrame.Value.ToString() : "none")}");
            lines.Add($"view.trace.sample[{sample.RelativeFrame}].lookAtYCalls={sample.LookAtYCalls}");
            lines.Add($"view.trace.sample[{sample.RelativeFrame}].curveSetMidCalls={sample.CurveSetMidCalls}");
            lines.Add($"view.trace.sample[{sample.RelativeFrame}].curveLowHighCalls={sample.CurveLowHighCalls}");
        }
    }
}
