// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraRestoreCurveRuntimeState.cs
// Description: カメラcapture結果・CharaSelectカメラruntime記録/復元・sceneReady信号・runtime復元・curve適用のセッション限定診断状態を保持する
// Reason: 巨大サービスから同一ライフサイクルの可変状態を責務単位で分離するため
namespace XivMiniUtil.Services.TitleBackground;

// カメラcapture結果・CharaSelectカメラruntime記録/復元・sceneReady信号・runtime復元・curve適用のセッション限定診断状態（config非保存）。
internal sealed class TitleBackgroundCameraRestoreCurveRuntimeState
{
    public TitleBackgroundCameraCaptureResult LastCameraCaptureResult { get; set; } = TitleBackgroundCameraCaptureResult.NotRun;

    public string LastCharaSelectCameraRuntimeRecordStatus { get; set; } = "not-run";

    public string LastCharaSelectCameraRuntimeRestoreStatus { get; set; } = "not-run";

    public string LastCharaSelectCameraRuntimeRecordError { get; set; } = string.Empty;

    public string LastCharaSelectCameraRuntimeRestoreFailureReason { get; set; } = string.Empty;

    public int LastCharaSelectCameraRuntimeRestoreSceneGeneration { get; set; }

    public int SceneReadySignalCallCount { get; set; }

    public int SceneReadySignalAcceptedCount { get; set; }

    public string SceneReadySignalLastAdapterStateBeforeHandle { get; set; } = "not-run";

    public GameLobbyType SceneReadySignalLastResolvedLobbyMap { get; set; } = GameLobbyType.None;

    public int RuntimeRestoreAttemptCount { get; set; }

    public int RuntimeRestoreSuccessCount { get; set; }

    public float? RuntimeRestoreLastRestoredYaw { get; set; }

    public float? RuntimeRestoreLastRestoredPitch { get; set; }

    public float? RuntimeRestoreLastRestoredDistance { get; set; }

    public float? RuntimeRestoreLastRestoredFovY { get; set; }

    // 保存 view の pose（DirH/DirV/Distance/FovY）を scene load 後に 1 回適用した記録（診断用）。
    public int SavedViewPoseAppliedCount { get; set; }

    public float? SavedViewPoseAppliedDirH { get; set; }

    public float? SavedViewPoseAppliedDirV { get; set; }

    public float? SavedViewPoseAppliedDistance { get; set; }

    public float? SavedViewPoseAppliedFovY { get; set; }

    // view 非関与 load の skipped 等で上書きしない、最後の保存 view pose 復元結果。
    public string SavedViewPoseLastRestoreStatus { get; set; } = "not-run";

    public int SavedViewPoseLastRestoreSceneGeneration { get; set; }

    public int CurveApplyAttemptCount { get; set; }

    public int CurveApplySuccessCount { get; set; }

    public string CurveApplyLastStatus { get; set; } = "not-run";

    public string CurveApplyLastFailureReason { get; set; } = string.Empty;

    public float? CurveApplyLastAppliedLow { get; set; }

    public float? CurveApplyLastAppliedMid { get; set; }

    public float? CurveApplyLastAppliedHigh { get; set; }

    public int? CurveApplyAppliedFrame { get; set; }

    public float? CurveApplyRequestedMid { get; set; }

    public float? CurveApplyReadBackValueImmediatelyAfterWrite { get; set; }

    public string CurveApplyImmediateReadBackStatus { get; set; } = "not-run";

    public TitleBackgroundActiveCameraSnapshot? CurveApplyActiveCameraBefore { get; set; }

    public TitleBackgroundActiveCameraSnapshot? CurveApplyActiveCameraAfter { get; set; }

    public string CurveApplyActiveCameraBeforeStatus { get; set; } = "not-run";

    public string CurveApplyActiveCameraAfterStatus { get; set; } = "not-run";

    public int? RuntimeRestoreAppliedFrame { get; set; }
}
