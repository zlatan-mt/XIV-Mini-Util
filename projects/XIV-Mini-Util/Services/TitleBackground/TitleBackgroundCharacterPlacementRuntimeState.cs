// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharacterPlacementRuntimeState.cs
// Description: pre-loginキャラDrawObject観測とCharaSelectキャラ配置記録のセッション限定状態を保持する
// Reason: 巨大サービスから同一ライフサイクルの可変状態を責務単位で分離するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

// pre-loginキャラDrawObject観測とCharaSelectキャラ配置記録のセッション限定状態（config非保存）。
internal sealed class TitleBackgroundCharacterPlacementRuntimeState
{
    public Vector3? LastPreLoginCharacterDrawPosition { get; set; }

    public float LastPreLoginCharacterDrawRotation { get; set; }

    public int PreLoginCharacterDrawObservedCount { get; set; }

    public int CharaSelectCharacterPlacementCount { get; set; }

    public string CharaSelectCharacterPlacementLastError { get; set; } = "none";

    public Vector3? LastCharaSelectCharacterPlacementTarget { get; set; }

    public string LastCharaSelectCharacterPlacementSource { get; set; } = "none";

    // 直近の配置で使ったアンカーの frame（地面 provenance 判定に使う）。camera-focus 由来は Unknown。
    public string LastCharaSelectCharacterPlacementAnchorFrame { get; set; } = TitleBackgroundCharaSelectAnchorFrame.Unknown;

    public bool FacingActive { get; set; }

    public int FacingAppliedFrameCount { get; set; }

    public float? FacingAppliedYaw { get; set; }

    public float? FacingSavedDirH { get; set; }

    public float? FacingReadBackRotation { get; set; }

    public float? FacingCalibrationOffset { get; set; }

    public string FacingCalibrationSource { get; set; } = "default-zero";

    public float? FacingGeometricExpectedYaw { get; set; }

    public float? FacingAngularError { get; set; }

    public float? FacingMaxAngularError { get; set; }

    public int FacingSettledConsecutiveFrameCount { get; set; }

    public float? FacingSettledMaxAngularError { get; set; }

    public float? FacingPreviousReadBackRotation { get; set; }

    public float? FacingMaxFrameDelta { get; set; }

    public float? FacingMaxAppliedError { get; set; }

    public bool FacingCalibrationCapturedDuringRun { get; set; }

    public string FacingCalibrationCandidateId { get; set; } = string.Empty;

    public float? FacingCalibrationDerivedOffset { get; set; }

    public float? FacingCalibrationFirstOffset { get; set; }

    public float? FacingCalibrationMinRelativeOffset { get; set; }

    public float? FacingCalibrationMaxRelativeOffset { get; set; }

    public float? FacingCalibrationMaxOffsetDelta { get; set; }

    public float? FacingCalibrationPreviousNaturalRotation { get; set; }

    public float? FacingCalibrationPreviousExpectedFromGeometry { get; set; }

    public float? FacingCalibrationNaturalRotation { get; set; }

    public float? FacingCalibrationExpectedFromGeometry { get; set; }

    public int FacingCalibrationSampleCount { get; set; }

    public int FacingCalibrationRejectedTransientCount { get; set; }

    public float? FacingOffsetAbsError { get; set; }

    public string FacingLastError { get; set; } = "none";
}
