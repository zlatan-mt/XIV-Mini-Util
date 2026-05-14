// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraProbeReport.cs
// Description: one-shot camera Y probe の純粋な判定ロジックを定義する
// Reason: 実機確認回数を減らすため、FixOn 直後反映と後段上書きを診断値から一貫判定するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundCameraProbeVerdict
{
    Inconclusive,
    Reflected,
    NotReflected,
    Stable,
    PossiblyOverwritten,
}

internal readonly record struct TitleBackgroundCameraProbeReportInput(
    bool Armed,
    Vector3 BaselineCamera,
    Vector3 BaselineFocus,
    Vector3 ProbeCamera,
    Vector3 ProbeFocus,
    Vector3? LastAppliedCamera,
    Vector3? PostFixOnSceneCameraPosition,
    Vector3? CurrentSceneCameraPosition,
    Vector3? LastAppliedFocus,
    Vector3? PostFixOnLookAtVector,
    Vector3? CurrentLookAtVector);

internal readonly record struct TitleBackgroundCameraProbeReportResult(
    TitleBackgroundCameraProbeVerdict CameraYFixOnReflection,
    TitleBackgroundCameraProbeVerdict CameraYPostFixOnStability,
    TitleBackgroundCameraProbeVerdict FocusYFixOnReflection,
    TitleBackgroundCameraProbeVerdict FocusYPostFixOnStability,
    string LikelyConclusion);

internal static class TitleBackgroundCameraProbeReport
{
    public const float ReflectionTolerance = 1f;
    public const float OverwriteThreshold = 5f;

    public static TitleBackgroundCameraProbeReportResult Evaluate(TitleBackgroundCameraProbeReportInput input)
    {
        if (!input.Armed)
        {
            return new TitleBackgroundCameraProbeReportResult(
                TitleBackgroundCameraProbeVerdict.Inconclusive,
                TitleBackgroundCameraProbeVerdict.Inconclusive,
                TitleBackgroundCameraProbeVerdict.Inconclusive,
                TitleBackgroundCameraProbeVerdict.Inconclusive,
                "Likely conclusion: Inconclusive; arm the probe first.");
        }

        var cameraReflection = EvaluateReflection(input.LastAppliedCamera, input.PostFixOnSceneCameraPosition);
        var cameraStability = EvaluateStability(input.PostFixOnSceneCameraPosition, input.CurrentSceneCameraPosition);
        var focusReflection = EvaluateReflection(input.LastAppliedFocus, input.PostFixOnLookAtVector);
        var focusStability = EvaluateStability(input.PostFixOnLookAtVector, input.CurrentLookAtVector);

        return new TitleBackgroundCameraProbeReportResult(
            cameraReflection,
            cameraStability,
            focusReflection,
            focusStability,
            BuildLikelyConclusion(cameraReflection, cameraStability, focusReflection, focusStability));
    }

    public static string FormatVerdict(TitleBackgroundCameraProbeVerdict verdict)
    {
        return verdict switch
        {
            TitleBackgroundCameraProbeVerdict.Reflected => "reflected",
            TitleBackgroundCameraProbeVerdict.NotReflected => "not-reflected",
            TitleBackgroundCameraProbeVerdict.Stable => "stable",
            TitleBackgroundCameraProbeVerdict.PossiblyOverwritten => "possibly-overwritten",
            _ => "inconclusive",
        };
    }

    private static TitleBackgroundCameraProbeVerdict EvaluateReflection(Vector3? applied, Vector3? postFixOn)
    {
        var delta = CalculateYDelta(postFixOn, applied);
        if (!delta.HasValue)
        {
            return TitleBackgroundCameraProbeVerdict.Inconclusive;
        }

        return Math.Abs(delta.Value) <= ReflectionTolerance
            ? TitleBackgroundCameraProbeVerdict.Reflected
            : Math.Abs(delta.Value) >= OverwriteThreshold
                ? TitleBackgroundCameraProbeVerdict.NotReflected
                : TitleBackgroundCameraProbeVerdict.Inconclusive;
    }

    private static TitleBackgroundCameraProbeVerdict EvaluateStability(Vector3? postFixOn, Vector3? current)
    {
        var delta = CalculateYDelta(current, postFixOn);
        if (!delta.HasValue)
        {
            return TitleBackgroundCameraProbeVerdict.Inconclusive;
        }

        return Math.Abs(delta.Value) <= ReflectionTolerance
            ? TitleBackgroundCameraProbeVerdict.Stable
            : Math.Abs(delta.Value) >= OverwriteThreshold
                ? TitleBackgroundCameraProbeVerdict.PossiblyOverwritten
                : TitleBackgroundCameraProbeVerdict.Inconclusive;
    }

    private static float? CalculateYDelta(Vector3? current, Vector3? baseline)
    {
        return TitleBackgroundCameraMath.CalculateVectorDelta(current, baseline)?.Y;
    }

    private static string BuildLikelyConclusion(
        TitleBackgroundCameraProbeVerdict cameraReflection,
        TitleBackgroundCameraProbeVerdict cameraStability,
        TitleBackgroundCameraProbeVerdict focusReflection,
        TitleBackgroundCameraProbeVerdict focusStability)
    {
        if (cameraReflection == TitleBackgroundCameraProbeVerdict.Reflected
            && cameraStability == TitleBackgroundCameraProbeVerdict.PossiblyOverwritten)
        {
            return "Likely conclusion: CameraY is reflected at FixOn but overwritten later.";
        }

        if (focusReflection == TitleBackgroundCameraProbeVerdict.Reflected
            && cameraReflection == TitleBackgroundCameraProbeVerdict.NotReflected)
        {
            return "Likely conclusion: FocusY reflects correctly; CameraY does not.";
        }

        if (cameraReflection == TitleBackgroundCameraProbeVerdict.Reflected
            && cameraStability == TitleBackgroundCameraProbeVerdict.Stable
            && focusReflection == TitleBackgroundCameraProbeVerdict.Reflected)
        {
            return "Likely conclusion: CameraY and FocusY are reflected at FixOn; final stability needs visual confirmation.";
        }

        return "Likely conclusion: Inconclusive; single-axis follow-up required.";
    }
}
