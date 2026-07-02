// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundPhaseRecordingRuntimeState.cs
// Description: phase2M placement / phase2E lookAtY / phase2F generated curve / phase2G generation overrideのセッション限定記録状態を保持する
// Reason: 巨大サービスから同一ライフサイクルの可変状態を責務単位で分離するため
namespace XivMiniUtil.Services.TitleBackground;

// phase2M placement / phase2E lookAtY / phase2F generated curve / phase2G generation overrideのセッション限定記録状態（config非保存）。
internal sealed class TitleBackgroundPhaseRecordingRuntimeState
{
    public Dictionary<int, TitleBackgroundCharacterPlacementFrame> Phase2MPlacementFrames { get; } = [];

    public int Phase2MPlacementCaptureSceneGeneration { get; set; }

    public string Phase2MPlacementCaptureReason { get; set; } = "not-run";

    public int Phase2MPlacementSkippedPostLoginCount { get; set; }

    public int Phase2MPlacementSkippedInactiveCount { get; set; }

    public int Phase2MPlacementSkippedSceneGenerationCount { get; set; }

    public string Phase2MPlacementLastSkipReason { get; set; } = "none";

    public string Phase2MExperimentalLastStatus { get; set; } = "not-run";

    public int Phase2MExperimentalWriteCount { get; set; }

    public int Phase2MExperimentalSkippedCount { get; set; }

    public List<TitleBackgroundPhase2ECalculateLookAtYCall> Phase2ECalculateLookAtYCalls { get; } = [];

    public List<TitleBackgroundPhase2FGeneratedCurveCall> Phase2FSetCameraCurveMidPointCalls { get; } = [];

    public List<TitleBackgroundPhase2FGeneratedCurveCall> Phase2FCalculateCameraCurveLowAndHighPointCalls { get; } = [];

    public List<TitleBackgroundPhase2FGeneratedCurveCall> Phase2FSetCameraCurveMidPointInterestingCalls { get; } = [];

    public List<TitleBackgroundPhase2FGeneratedCurveCall> Phase2FCalculateCameraCurveLowAndHighPointInterestingCalls { get; } = [];

    public int Phase2ECalculateLookAtYCallCount { get; set; }

    public int Phase2FSetCameraCurveMidPointCallCount { get; set; }

    public int Phase2FCalculateCameraCurveLowAndHighPointCallCount { get; set; }

    public float? Phase2FSetCameraCurveMidPointPreviousInputValue { get; set; }

    public float? Phase2FCalculateCameraCurveLowAndHighPointPreviousInputValue { get; set; }

    public string Phase2ECalculateLookAtYLastError { get; set; } = string.Empty;

    public string Phase2FSetCameraCurveMidPointLastError { get; set; } = string.Empty;

    public string Phase2FCalculateCameraCurveLowAndHighPointLastError { get; set; } = string.Empty;

    public int Phase2GGenerationOverrideSetMidAttemptCount { get; set; }

    public int Phase2GGenerationOverrideSetMidAppliedCount { get; set; }

    public int Phase2GGenerationOverrideLowHighAttemptCount { get; set; }

    public int Phase2GGenerationOverrideLowHighAppliedCount { get; set; }

    public int? Phase2GGenerationOverrideLastAppliedFrame { get; set; }

    public int Phase2GGenerationOverrideLastAppliedSceneGeneration { get; set; }

    public string Phase2GGenerationOverrideLastStatus { get; set; } = "not-run";

    public string Phase2GGenerationOverrideLastSkippedReason { get; set; } = string.Empty;
}
