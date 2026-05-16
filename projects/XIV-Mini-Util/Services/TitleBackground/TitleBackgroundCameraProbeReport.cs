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

internal enum TitleBackgroundCameraOverwritePattern
{
    Inconclusive,
    Immediate,
    Gradual,
    Late,
}

internal readonly record struct TitleBackgroundCameraProbeTimelineSample(
    int Frame,
    Vector3? SceneCameraPosition,
    Vector3? LookAtVector);

internal readonly record struct TitleBackgroundCameraProbeTimelineEventCounts(
    int FixOnCalls,
    int LobbyUpdateCalls,
    int LoadLobbySceneCalls,
    int CreateSceneCalls);

internal readonly record struct TitleBackgroundCameraProbeTimelineAnalysis(
    int? CameraOverwriteFirstObservedFrame,
    int? FocusOverwriteFirstObservedFrame,
    TitleBackgroundCameraOverwritePattern CameraOverwritePattern,
    TitleBackgroundCameraOverwritePattern FocusOverwritePattern);

internal readonly record struct TitleBackgroundPhase2DTimelineSample(
    int Frame,
    Vector3? SceneCameraPosition,
    Vector3? LookAtVector,
    float? Distance,
    float? DirH,
    float? DirV);

internal readonly record struct TitleBackgroundPhase2DAnalysis(
    string FinalCameraStabilizationObserved,
    string DistanceEventuallyOverwritten,
    string SceneTransformShiftObserved);

internal readonly record struct TitleBackgroundPhase2EProbeSample(
    int CallIndex,
    int? Frame,
    float? ReturnValue,
    float? ActiveLookAtYAfterOriginal);

internal readonly record struct TitleBackgroundPhase2EAnalysis(
    string NativeReturnMatchesActiveLookAtY,
    string NativeReturnMatchesFinalStableLookAtY,
    int ComparedCallCount);

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
    public const float DistanceOverwriteThreshold = 0.1f;
    public const float StabilizationDistanceTolerance = 0.02f;
    public const float StabilizationAngleTolerance = 0.01f;
    public const float StabilizationVectorTolerance = 0.05f;
    public const int FinalStabilizationMinimumFrame = 300;

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

    public static TitleBackgroundCameraProbeTimelineAnalysis AnalyzeTimeline(
        IReadOnlyList<TitleBackgroundCameraProbeTimelineSample> samples,
        Vector3? postFixOnSceneCameraPosition,
        Vector3? postFixOnLookAtVector)
    {
        var cameraFrame = FindOverwriteFirstObservedFrame(
            samples,
            postFixOnSceneCameraPosition,
            sample => sample.SceneCameraPosition);
        var focusFrame = FindOverwriteFirstObservedFrame(
            samples,
            postFixOnLookAtVector,
            sample => sample.LookAtVector);

        return new TitleBackgroundCameraProbeTimelineAnalysis(
            cameraFrame,
            focusFrame,
            GetOverwritePattern(cameraFrame),
            GetOverwritePattern(focusFrame));
    }

    public static string FormatOverwritePattern(TitleBackgroundCameraOverwritePattern pattern)
    {
        return pattern switch
        {
            TitleBackgroundCameraOverwritePattern.Immediate => "immediate",
            TitleBackgroundCameraOverwritePattern.Gradual => "gradual",
            TitleBackgroundCameraOverwritePattern.Late => "late",
            _ => "inconclusive",
        };
    }

    public static TitleBackgroundPhase2DAnalysis AnalyzePhase2D(
        IReadOnlyList<TitleBackgroundPhase2DTimelineSample> samples,
        float? restoredDistance)
    {
        var capturedSamples = samples
            .Where(HasAnyPhase2DCameraValue)
            .OrderBy(sample => sample.Frame)
            .ToArray();
        var sceneTransformShiftObserved = EvaluateSceneTransformShift(capturedSamples);
        var distanceEventuallyOverwritten = EvaluateDistanceEventuallyOverwritten(capturedSamples, restoredDistance);
        var finalCameraStabilizationObserved = EvaluateFinalCameraStabilization(capturedSamples);

        return new TitleBackgroundPhase2DAnalysis(
            finalCameraStabilizationObserved,
            distanceEventuallyOverwritten,
            sceneTransformShiftObserved);
    }

    public static TitleBackgroundPhase2EAnalysis AnalyzePhase2E(
        IReadOnlyList<TitleBackgroundPhase2EProbeSample> samples,
        float? finalStableLookAtY)
    {
        var comparedCalls = samples
            .Where(sample => sample.ReturnValue.HasValue && sample.ActiveLookAtYAfterOriginal.HasValue)
            .ToArray();
        var nativeReturnMatchesActiveLookAtY = comparedCalls.Length == 0
            ? "inconclusive"
            : comparedCalls.Any(sample => Math.Abs(sample.ReturnValue!.Value - sample.ActiveLookAtYAfterOriginal!.Value) <= StabilizationVectorTolerance)
                ? "observed"
                : "not-observed";

        var latestReturn = samples
            .Where(sample => sample.ReturnValue.HasValue)
            .OrderByDescending(sample => sample.CallIndex)
            .Select(sample => sample.ReturnValue!.Value)
            .Cast<float?>()
            .FirstOrDefault();
        var nativeReturnMatchesFinalStableLookAtY = latestReturn.HasValue && finalStableLookAtY.HasValue
            ? Math.Abs(latestReturn.Value - finalStableLookAtY.Value) <= StabilizationVectorTolerance
                ? "observed"
                : "not-observed"
            : "inconclusive";

        return new TitleBackgroundPhase2EAnalysis(
            nativeReturnMatchesActiveLookAtY,
            nativeReturnMatchesFinalStableLookAtY,
            comparedCalls.Length);
    }

    public static string DescribeCoincidentEvents(
        int? observedFrame,
        TitleBackgroundCameraProbeTimelineEventCounts events)
    {
        if (!observedFrame.HasValue)
        {
            return "none";
        }

        return FormatEventCounts(events);
    }

    public static string DescribeFocusDriftEvents(
        IReadOnlyList<TitleBackgroundCameraProbeTimelineSample> samples,
        Func<int, int, TitleBackgroundCameraProbeTimelineEventCounts> getEventsInRange,
        Vector3? postFixOnLookAtVector)
    {
        if (!postFixOnLookAtVector.HasValue)
        {
            return "none";
        }

        int? firstDriftFrame = null;
        int? lastDriftFrame = null;
        foreach (var sample in samples.OrderBy(sample => sample.Frame))
        {
            var delta = CalculateYDelta(sample.LookAtVector, postFixOnLookAtVector);
            if (!delta.HasValue || Math.Abs(delta.Value) < OverwriteThreshold)
            {
                continue;
            }

            firstDriftFrame ??= sample.Frame;
            lastDriftFrame = sample.Frame;
        }

        return firstDriftFrame.HasValue && lastDriftFrame.HasValue
            ? FormatEventCounts(getEventsInRange(firstDriftFrame.Value, lastDriftFrame.Value))
            : "none";
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

    private static int? FindOverwriteFirstObservedFrame(
        IReadOnlyList<TitleBackgroundCameraProbeTimelineSample> samples,
        Vector3? baseline,
        Func<TitleBackgroundCameraProbeTimelineSample, Vector3?> getValue)
    {
        if (!baseline.HasValue)
        {
            return null;
        }

        foreach (var sample in samples.OrderBy(sample => sample.Frame))
        {
            var delta = CalculateYDelta(getValue(sample), baseline);
            if (delta.HasValue && Math.Abs(delta.Value) >= OverwriteThreshold)
            {
                return sample.Frame;
            }
        }

        return null;
    }

    private static TitleBackgroundCameraOverwritePattern GetOverwritePattern(int? firstObservedFrame)
    {
        if (!firstObservedFrame.HasValue)
        {
            return TitleBackgroundCameraOverwritePattern.Inconclusive;
        }

        if (firstObservedFrame.Value <= 2)
        {
            return TitleBackgroundCameraOverwritePattern.Immediate;
        }

        return firstObservedFrame.Value >= 16
            ? TitleBackgroundCameraOverwritePattern.Late
            : TitleBackgroundCameraOverwritePattern.Gradual;
    }

    private static string EvaluateSceneTransformShift(IReadOnlyList<TitleBackgroundPhase2DTimelineSample> samples)
    {
        var baseline = samples.FirstOrDefault(sample => sample.SceneCameraPosition.HasValue);
        if (!baseline.SceneCameraPosition.HasValue)
        {
            return "inconclusive";
        }

        return samples.Any(sample =>
        {
            var delta = TitleBackgroundCameraMath.CalculateVectorDelta(sample.SceneCameraPosition, baseline.SceneCameraPosition);
            return delta.HasValue && delta.Value.Length() >= OverwriteThreshold;
        })
            ? "observed"
            : "not-observed";
    }

    private static string EvaluateDistanceEventuallyOverwritten(
        IReadOnlyList<TitleBackgroundPhase2DTimelineSample> samples,
        float? restoredDistance)
    {
        if (!restoredDistance.HasValue)
        {
            return "inconclusive";
        }

        var capturedDistances = samples
            .Where(sample => sample.Distance.HasValue)
            .Select(sample => sample.Distance!.Value)
            .ToArray();
        if (capturedDistances.Length == 0)
        {
            return "inconclusive";
        }

        return capturedDistances.Any(distance => Math.Abs(distance - restoredDistance.Value) >= DistanceOverwriteThreshold)
            ? "observed"
            : "not-observed";
    }

    private static string EvaluateFinalCameraStabilization(IReadOnlyList<TitleBackgroundPhase2DTimelineSample> samples)
    {
        var stableWindow = samples
            .Where(sample => sample.Frame >= FinalStabilizationMinimumFrame && HasFullPhase2DCameraValue(sample))
            .OrderByDescending(sample => sample.Frame)
            .Take(3)
            .OrderBy(sample => sample.Frame)
            .ToArray();
        if (stableWindow.Length < 3)
        {
            return "inconclusive";
        }

        for (var i = 1; i < stableWindow.Length; i++)
        {
            if (!IsStablePair(stableWindow[i - 1], stableWindow[i]))
            {
                return "not-observed";
            }
        }

        return "observed";
    }

    private static bool HasAnyPhase2DCameraValue(TitleBackgroundPhase2DTimelineSample sample)
    {
        return sample.SceneCameraPosition.HasValue
            || sample.LookAtVector.HasValue
            || sample.Distance.HasValue
            || sample.DirH.HasValue
            || sample.DirV.HasValue;
    }

    private static bool HasFullPhase2DCameraValue(TitleBackgroundPhase2DTimelineSample sample)
    {
        return sample.SceneCameraPosition.HasValue
            && sample.LookAtVector.HasValue
            && sample.Distance.HasValue
            && sample.DirH.HasValue
            && sample.DirV.HasValue;
    }

    private static bool IsStablePair(TitleBackgroundPhase2DTimelineSample previous, TitleBackgroundPhase2DTimelineSample current)
    {
        var positionDelta = TitleBackgroundCameraMath.CalculateVectorDelta(current.SceneCameraPosition, previous.SceneCameraPosition);
        var lookAtDelta = TitleBackgroundCameraMath.CalculateVectorDelta(current.LookAtVector, previous.LookAtVector);
        var distanceDelta = TitleBackgroundCameraMath.CalculateFloatDelta(current.Distance, previous.Distance);
        var dirHDelta = TitleBackgroundCameraMath.CalculateFloatDelta(current.DirH, previous.DirH);
        var dirVDelta = TitleBackgroundCameraMath.CalculateFloatDelta(current.DirV, previous.DirV);

        return positionDelta.HasValue
            && lookAtDelta.HasValue
            && distanceDelta.HasValue
            && dirHDelta.HasValue
            && dirVDelta.HasValue
            && positionDelta.Value.Length() <= StabilizationVectorTolerance
            && lookAtDelta.Value.Length() <= StabilizationVectorTolerance
            && Math.Abs(distanceDelta.Value) <= StabilizationDistanceTolerance
            && Math.Abs(dirHDelta.Value) <= StabilizationAngleTolerance
            && Math.Abs(dirVDelta.Value) <= StabilizationAngleTolerance;
    }

    private static string FormatEventCounts(TitleBackgroundCameraProbeTimelineEventCounts events)
    {
        return $"fixOn={events.FixOnCalls},lobbyUpdate={events.LobbyUpdateCalls},loadLobbyScene={events.LoadLobbySceneCalls},createScene={events.CreateSceneCalls}";
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
