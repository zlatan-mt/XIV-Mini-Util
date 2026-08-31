// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundVerification.cs
// Description: Title Background の実機確認値を PASS/FAIL/not-evaluated へ集約する純粋ロジック
// Reason: 構図・向き・jitter の目視確認を自動レポートの数値判定へ置き換えるため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundVerificationStatus
{
    NotEvaluated,
    PASS,
    WARN,
    FAIL,
}

internal readonly record struct TitleBackgroundVerificationResult(
    TitleBackgroundVerificationStatus Status,
    float? Metric = null,
    string Detail = "none")
{
    public string ReportValue => Status == TitleBackgroundVerificationStatus.NotEvaluated
        ? "not-evaluated"
        : Status.ToString();
}

internal readonly record struct TitleBackgroundVerificationSummary(
    TitleBackgroundVerificationResult PoseHold,
    TitleBackgroundVerificationResult CameraJitter,
    TitleBackgroundVerificationResult RotationJitter,
    TitleBackgroundVerificationResult Facing,
    TitleBackgroundVerificationResult Framing,
    TitleBackgroundVerificationResult Suppression,
    TitleBackgroundVerificationResult LoginStop,
    TitleBackgroundVerificationResult Environment)
{
    public bool CharacterNumericallyVerified =>
        PoseHold.Status == TitleBackgroundVerificationStatus.PASS
        && CameraJitter.Status == TitleBackgroundVerificationStatus.PASS
        && RotationJitter.Status == TitleBackgroundVerificationStatus.PASS
        && Facing.Status == TitleBackgroundVerificationStatus.PASS
        && Framing.Status == TitleBackgroundVerificationStatus.PASS;
}

internal static class TitleBackgroundVerificationLogic
{
    public const float PoseTolerance = 0.01f;
    public const float JitterTolerance = 0.01f;
    public const float FacingTolerance = 0.1f;
    public const float FramingTolerance = 2f;

    public static TitleBackgroundVerificationResult EvaluatePoseHold(
        float? targetDirH,
        float? targetDirV,
        float? targetDistance,
        IReadOnlyList<TitleBackgroundViewReplayTraceSample> samples,
        float tolerance = PoseTolerance)
    {
        if (!targetDirH.HasValue || !targetDirV.HasValue || !targetDistance.HasValue)
        {
            return NotEvaluated("saved-pose-target-missing");
        }

        var captured = samples
            .Where(sample => sample.Captured
                && sample.DirH.HasValue
                && sample.DirV.HasValue
                && sample.Distance.HasValue)
            .ToArray();
        if (captured.Length < 2)
        {
            return NotEvaluated("insufficient-pose-samples");
        }

        var maxDelta = captured.Max(sample => Math.Max(
            TitleBackgroundCharaSelectCharacterFacing.AngularDistance(targetDirH.Value, sample.DirH!.Value),
            Math.Max(
                Math.Abs(targetDirV.Value - sample.DirV!.Value),
                Math.Abs(targetDistance.Value - sample.Distance!.Value))));
        return Threshold(maxDelta, tolerance, "max-pose-delta");
    }

    public static TitleBackgroundVerificationResult EvaluateCameraJitter(
        IReadOnlyList<TitleBackgroundViewReplayTraceSample> samples,
        float tolerance = JitterTolerance)
    {
        var captured = samples
            .Where(sample => sample.Captured
                && sample.DirH.HasValue
                && sample.DirV.HasValue
                && sample.Distance.HasValue)
            .OrderBy(sample => sample.RelativeFrame)
            .ToArray();
        if (captured.Length < 2)
        {
            return NotEvaluated("insufficient-camera-samples");
        }

        var maxDelta = 0f;
        for (var index = 1; index < captured.Length; index++)
        {
            maxDelta = Math.Max(
                maxDelta,
                Math.Max(
                    TitleBackgroundCharaSelectCharacterFacing.AngularDistance(
                        captured[index - 1].DirH!.Value,
                        captured[index].DirH!.Value),
                    Math.Max(
                        Math.Abs(captured[index - 1].DirV!.Value - captured[index].DirV!.Value),
                        Math.Abs(captured[index - 1].Distance!.Value - captured[index].Distance!.Value))));
        }

        return Threshold(maxDelta, tolerance, "max-frame-delta");
    }

    public static TitleBackgroundVerificationResult EvaluateRotationJitter(
        int appliedFrameCount,
        float? maxFrameDelta,
        float tolerance = JitterTolerance)
    {
        return appliedFrameCount < 2 || !maxFrameDelta.HasValue
            ? NotEvaluated("insufficient-rotation-samples")
            : Threshold(maxFrameDelta.Value, tolerance, "max-frame-delta");
    }

    public static TitleBackgroundVerificationResult EvaluateFacing(
        float? settledAngularError,
        float? fallbackAngularError = null,
        int settledConsecutiveFrameCount = TitleBackgroundCharaSelectCharacterFacing.FacingSettledFrameThreshold,
        float tolerance = FacingTolerance,
        float? offsetAbsError = null)
    {
        var detail = offsetAbsError.HasValue
            ? "settled-angular-error;see=character.facing.offsetAbsError"
            : "settled-angular-error";
        if (settledAngularError.HasValue)
        {
            return Threshold(settledAngularError.Value, tolerance, detail);
        }

        if (settledConsecutiveFrameCount > 0
            && settledConsecutiveFrameCount < TitleBackgroundCharaSelectCharacterFacing.FacingSettledFrameThreshold)
        {
            return NotEvaluated("facing-not-settled");
        }

        if (fallbackAngularError.HasValue)
        {
            return Threshold(fallbackAngularError.Value, tolerance, "angular-error-legacy-fallback");
        }

        return NotEvaluated("facing-error-missing");
    }

    public static TitleBackgroundVerificationResult EvaluateFraming(
        Vector3? characterPosition,
        Vector3? cameraLookAt,
        float tolerance = FramingTolerance)
    {
        if (!characterPosition.HasValue
            || !cameraLookAt.HasValue
            || !TitleBackgroundCameraMath.IsFiniteVector(characterPosition.Value)
            || !TitleBackgroundCameraMath.IsFiniteVector(cameraLookAt.Value))
        {
            return NotEvaluated("character-or-lookat-missing");
        }

        return Threshold(
            Vector3.Distance(characterPosition.Value, cameraLookAt.Value),
            tolerance,
            "character-to-lookat-distance");
    }

    public static TitleBackgroundVerificationResult EvaluateSuppression(
        bool expected,
        bool suppressed,
        bool poseMaintainActive,
        bool facingActive)
    {
        if (!expected)
        {
            return NotEvaluated("saved-view-not-enabled");
        }

        return suppressed && !poseMaintainActive && !facingActive
            ? Pass("run-suppression-held")
            : Fail("saved-view-write-active-during-run");
    }

    public static TitleBackgroundVerificationResult EvaluateLoginStop(
        bool isLoggedIn,
        bool poseMaintainActive,
        bool facingActive,
        string? stopReason,
        bool writesExpected,
        bool writesSuppressed)
    {
        if (!isLoggedIn)
        {
            return NotEvaluated("login-not-observed");
        }

        if (poseMaintainActive || facingActive)
        {
            return Fail("bounded-write-still-active-after-login");
        }

        if (!writesExpected)
        {
            return NotEvaluated("saved-view-not-enabled");
        }

        if (writesSuppressed)
        {
            return Pass("bounded-writes-suppressed-through-login");
        }

        var stoppedForLogin = string.Equals(stopReason, "world-login-transition", StringComparison.Ordinal)
            || string.Equals(stopReason, "logged-in", StringComparison.Ordinal);
        return stoppedForLogin
            ? Pass("bounded-writes-stopped")
            : Fail("bounded-write-stop-not-confirmed");
    }

    public static TitleBackgroundVerificationResult EvaluateEnvironment(
        bool noonEnabled,
        int noonAppliedFrameCount,
        string? noonStatus,
        bool clearSkyEnabled,
        int clearSkyAppliedFrameCount,
        string? clearSkyStatus)
    {
        var noonPassed = !noonEnabled
            || (noonAppliedFrameCount > 0 && string.Equals(noonStatus, "applied", StringComparison.Ordinal));
        var clearSkyPassed = !clearSkyEnabled
            || (clearSkyAppliedFrameCount > 0 && string.Equals(clearSkyStatus, "applied", StringComparison.Ordinal));
        return noonPassed && clearSkyPassed
            ? Pass("configured-environment-writes-applied")
            : Fail("configured-environment-write-missing");
    }

    private static TitleBackgroundVerificationResult Threshold(float metric, float tolerance, string detail)
    {
        if (!float.IsFinite(metric))
        {
            return NotEvaluated($"{detail}-non-finite");
        }

        return metric <= tolerance
            ? new TitleBackgroundVerificationResult(TitleBackgroundVerificationStatus.PASS, metric, detail)
            : new TitleBackgroundVerificationResult(TitleBackgroundVerificationStatus.FAIL, metric, detail);
    }

    private static TitleBackgroundVerificationResult Pass(string detail) =>
        new(TitleBackgroundVerificationStatus.PASS, null, detail);

    private static TitleBackgroundVerificationResult Fail(string detail) =>
        new(TitleBackgroundVerificationStatus.FAIL, null, detail);

    private static TitleBackgroundVerificationResult NotEvaluated(string detail) =>
        new(TitleBackgroundVerificationStatus.NotEvaluated, null, detail);
}
