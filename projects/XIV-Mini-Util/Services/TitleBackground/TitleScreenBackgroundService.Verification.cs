// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.Verification.cs
// Description: 実機診断状態から Title Background の数値検証サマリを組み立てる
// Reason: QuickCheck・delivery・自動レポートで同じ PASS/FAIL 判定を共有するため
namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    private void ResetFacingVerificationForSavedViewLoad()
    {
        _characterPlacement.FacingAppliedFrameCount = 0;
        _characterPlacement.FacingAppliedYaw = null;
        _characterPlacement.FacingSavedDirH = null;
        _characterPlacement.FacingReadBackRotation = null;
        _characterPlacement.FacingCalibrationOffset = null;
        _characterPlacement.FacingCalibrationSource = "default-zero";
        _characterPlacement.FacingGeometricExpectedYaw = null;
        _characterPlacement.FacingAngularError = null;
        _characterPlacement.FacingMaxAngularError = null;
        _characterPlacement.FacingSettledConsecutiveFrameCount = 0;
        _characterPlacement.FacingSettledMaxAngularError = null;
        _characterPlacement.FacingPreviousReadBackRotation = null;
        _characterPlacement.FacingMaxFrameDelta = null;
        _characterPlacement.FacingMaxAppliedError = null;
        _characterPlacement.FacingOffsetAbsError = null;
        _characterPlacement.FacingLastError = "not-run";
    }

    private TitleBackgroundVerificationSummary BuildTitleBackgroundVerificationSummary()
    {
        var traceSamples = _viewReplayTrace.Samples.Values
            .OrderBy(sample => sample.RelativeFrame)
            .ToArray();
        var savedViewExpected = _configuration.TitleBackgroundCharaSelectViewEnabled
            && _configuration.TitleBackgroundCharaSelectViewPoseCaptured;
        var suppressed = IsSavedViewSuppressedByAutomaticRun() && savedViewExpected;
        var runScoped = IsRunScopedQuickCheckActive();
        var runPlacementCount = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedPlacementCount(
            runScoped,
            _characterPlacement.CharaSelectCharacterPlacementCount,
            _quickCheckState.CharacterPlacementCountStart);
        var framingCharacterPosition = !runScoped || runPlacementCount > 0
            ? _characterPlacement.LastCharaSelectCharacterPlacementTarget
            : null;

        return new TitleBackgroundVerificationSummary(
            TitleBackgroundVerificationLogic.EvaluatePoseHold(
                _viewReplayTrace.TargetDirH,
                _viewReplayTrace.TargetDirV,
                _viewReplayTrace.TargetDistance,
                traceSamples),
            TitleBackgroundVerificationLogic.EvaluateCameraJitter(traceSamples),
            TitleBackgroundVerificationLogic.EvaluateRotationJitter(
                _characterPlacement.FacingAppliedFrameCount,
                _characterPlacement.FacingMaxFrameDelta),
            TitleBackgroundVerificationLogic.EvaluateFacing(
                _characterPlacement.FacingSettledMaxAngularError,
                _characterPlacement.FacingMaxAngularError ?? _characterPlacement.FacingAngularError,
                _characterPlacement.FacingSettledConsecutiveFrameCount,
                offsetAbsError: _characterPlacement.FacingOffsetAbsError),
            TitleBackgroundVerificationLogic.EvaluateFraming(
                framingCharacterPosition,
                _cameraObservation.LastPreLoginSceneCameraLookAt),
            TitleBackgroundVerificationLogic.EvaluateSuppression(
                savedViewExpected && _automaticCheck.Requested,
                suppressed,
                _savedViewPoseMaintain.Active,
                _characterPlacement.FacingActive),
            TitleBackgroundVerificationLogic.EvaluateLoginStop(
                _clientState.IsLoggedIn,
                _savedViewPoseMaintain.Active,
                _characterPlacement.FacingActive,
                _savedViewPoseMaintain.StopReason,
                savedViewExpected,
                suppressed),
            TitleBackgroundVerificationLogic.EvaluateEnvironment(
                _configuration.TitleBackgroundEnvironmentNoonEnabled,
                _environmentNoon.AppliedFrameCount,
                _environmentNoon.LastStatus,
                _configuration.TitleBackgroundEnvironmentClearSkyEnabled,
                _environmentClearSky.AppliedFrameCount,
                _environmentClearSky.LastStatus));
    }

    private void AddTitleBackgroundVerificationLines(List<string> lines)
    {
        var summary = BuildTitleBackgroundVerificationSummary();
        AddVerificationResult(lines, "verify.poseHold", summary.PoseHold);
        AddVerificationResult(lines, "verify.cameraJitter", summary.CameraJitter);
        AddVerificationResult(lines, "verify.rotationJitter", summary.RotationJitter);
        AddVerificationResult(lines, "verify.facing", summary.Facing);
        AddVerificationResult(lines, "verify.framing", summary.Framing);
        AddVerificationResult(lines, "verify.suppression", summary.Suppression);
        AddVerificationResult(lines, "verify.loginStop", summary.LoginStop);
        AddVerificationResult(lines, "verify.environment", summary.Environment);
    }

    private static void AddVerificationResult(
        List<string> lines,
        string key,
        TitleBackgroundVerificationResult result)
    {
        lines.Add($"{key}={result.ReportValue}");
        lines.Add($"{key}.metric={(result.Metric.HasValue ? result.Metric.Value.ToString("0.###") : "none")}");
        lines.Add($"{key}.detail={result.Detail}");
    }
}
