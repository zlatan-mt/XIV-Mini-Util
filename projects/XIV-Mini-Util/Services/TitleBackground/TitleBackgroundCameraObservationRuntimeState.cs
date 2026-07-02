// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraObservationRuntimeState.cs
// Description: FixOn観測・focus/view override記録・pre-login/post-FixOnカメラ観測のセッション限定状態を保持する
// Reason: 巨大サービスから同一ライフサイクルの可変状態を責務単位で分離するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

// FixOn観測・focus/view override記録・pre-login/post-FixOnカメラ観測のセッション限定状態（config非保存）。
// - FixOn観測値・適用済みcamera/focus/FovY（Observed/Applied）
// - focus override / view override の適用回数・source・gate reason
// - FixOn発火「時点」の scene generation / capture context（実験系）
// - pre-login CharaSelect カメラの scene generation 整合スナップショット
// - post-FixOn カメラ観測のstatus/error/値
internal sealed class TitleBackgroundCameraObservationRuntimeState
{
    public Vector3? LastObservedFixOnCamera { get; set; }

    public Vector3? LastObservedFixOnFocus { get; set; }

    public float? LastObservedFixOnFovY { get; set; }

    public bool LastCameraOverrideApplied { get; set; }

    public Vector3? LastAppliedCamera { get; set; }

    public Vector3? LastAppliedFocus { get; set; }

    public float? LastAppliedFovY { get; set; }

    public string LastFixOnInvocationMode { get; set; } = "not-run";

    public int FixOnPassiveCallCount { get; set; }

    public int FixOnFocusOverrideAppliedCount { get; set; }

    public string LastFixOnFocusOverrideSource { get; set; } = "not-run";

    public int FixOnViewOverrideAppliedCount { get; set; }

    public string LastFixOnViewOverrideSource { get; set; } = "not-run";

    public int LastViewOverrideAppliedGeneration { get; set; }

    public string LastFixOnFocusOverrideGateReason { get; set; } = "not-run";

    // FixOn 発火「時点」の scene generation / context を保持する（報告時の値では pre-login 実態を取り違えるため）。
    public int FixOnExperimentSceneGeneration { get; set; }

    public string FixOnExperimentCaptureContext { get; set; } = "not-run";

    public bool FixOnExperimentCharaSelectSession { get; set; }

    // 同一 scene generation の整合フレームで保持する最後の pre-login CharaSelect カメラ。
    // 「安定後」を名乗らず、generation 一致と captured frame を併記して読み手が settled 度を判断できるようにする。
    public Vector3? LastPreLoginSceneCameraPosition { get; set; }

    public Vector3? LastPreLoginSceneCameraLookAt { get; set; }

    public float? LastPreLoginSceneCameraDistance { get; set; }

    public float? LastPreLoginSceneCameraFovY { get; set; }

    public int LastPreLoginSceneCameraGeneration { get; set; }

    public int? LastPreLoginSceneCameraFrame { get; set; }

    public string LastPostFixOnCameraCaptureStatus { get; set; } = "not-run";

    public string LastPostFixOnCameraCaptureError { get; set; } = string.Empty;

    public Vector3? LastPostFixOnSceneCameraPosition { get; set; }

    public Vector3? LastPostFixOnLookAtVector { get; set; }

    public float? LastPostFixOnDistance { get; set; }

    public float? LastPostFixOnFovY { get; set; }
}
