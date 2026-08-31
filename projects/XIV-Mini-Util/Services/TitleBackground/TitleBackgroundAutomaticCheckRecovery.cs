// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAutomaticCheckRecovery.cs
// Description: Title Background 自動確認で一時変更する設定の退避・復元モデル
// Reason: 異常終了や再起動をまたいでも確認前の設定へ戻せるようにするため
using System.Text.Json;

namespace XivMiniUtil.Services.TitleBackground;

internal sealed record TitleBackgroundAutomaticCheckSettingsSnapshot
{
    public bool OverrideEnabled { get; init; }
    public bool CameraOverrideEnabled { get; init; }
    public bool IntegratedCompositionEnabled { get; init; }
    // V2 production path フラグ。旧 journal（キー無し）は既定 false で復元され、V2 が無効になるだけで安全（fail-closed）。
    public bool V2Enabled { get; init; }
    // TitleEdit-informed placement の保存値も run 開始時点へ戻す。capture は run-scoped のため、
    // 失敗/partial run が既存の persistent placement を壊さないように journal へ含める。
    public bool CharaSelectPlacementEnabled { get; init; }
    public string CharaSelectPlacementCandidateId { get; init; } = string.Empty;
    public bool CharaSelectPlacementPositionCaptured { get; init; }
    public float CharaSelectPlacementPositionX { get; init; }
    public float CharaSelectPlacementPositionY { get; init; }
    public float CharaSelectPlacementPositionZ { get; init; }
    public float CharaSelectPlacementRotation { get; init; }
    public bool SceneCompositionEnabled { get; init; }
    public string SelectedPresetId { get; init; } = string.Empty;
    public string CandidateId { get; init; } = string.Empty;
    public string TerritoryPath { get; init; } = string.Empty;
    public uint TerritoryTypeId { get; init; }
    public uint LayoutTerritoryTypeId { get; init; }
    public uint LayoutLayerFilterKey { get; init; }
    public TitleBackgroundRuntimeMode RuntimeMode { get; init; }
    public TitleBackgroundCharacterSelectBackgroundMode BackgroundMode { get; init; }
    public TitleBackgroundCharacterSelectLightingMode LightingMode { get; init; }
    public TitleBackgroundCharaSelectCameraFramingMode CameraFramingMode { get; init; }
    public bool FixOnPassiveObservationEnabled { get; init; }
    public bool FixOnFocusAnchorOverrideEnabled { get; init; }
    public bool AnchorEnabled { get; init; }
    public string AnchorCandidateId { get; init; } = string.Empty;
    public float AnchorX { get; init; }
    public float AnchorY { get; init; }
    public float AnchorZ { get; init; }
    public float AnchorRotation { get; init; }
    public string AnchorFrame { get; init; } = string.Empty;
    public uint AnchorTerritoryTypeId { get; init; }
    public bool AnchorWorldExperimentalEnabled { get; init; }
    public bool ViewEnabled { get; init; }
    public string ViewCandidateId { get; init; } = string.Empty;
    public float ViewCameraX { get; init; }
    public float ViewCameraY { get; init; }
    public float ViewCameraZ { get; init; }
    public float ViewFocusX { get; init; }
    public float ViewFocusY { get; init; }
    public float ViewFocusZ { get; init; }
    public float ViewFovY { get; init; }
    // 保存 view のネイティブ pose（DirH/DirV/Distance）。旧 journal（キー無し）は既定値
    // （PoseCaptured=false）で復元され、pose 復元が働かないだけで安全（fail-closed）。
    public bool ViewPoseCaptured { get; init; }
    public float ViewDirH { get; init; }
    public float ViewDirV { get; init; }
    public float ViewDistance { get; init; }
    public bool FacingCalibrationCaptured { get; init; }
    public string FacingCalibrationCandidateId { get; init; } = string.Empty;
    public float FacingCalibrationOffset { get; init; } = TitleBackgroundCharaSelectCharacterFacing.DefaultCalibrationOffset;

    public static TitleBackgroundAutomaticCheckSettingsSnapshot Capture(Configuration configuration)
    {
        return new TitleBackgroundAutomaticCheckSettingsSnapshot
        {
            OverrideEnabled = configuration.TitleBackgroundOverrideEnabled,
            CameraOverrideEnabled = configuration.TitleBackgroundCameraOverrideEnabled,
            IntegratedCompositionEnabled = configuration.TitleBackgroundIntegratedCompositionEnabled,
            V2Enabled = configuration.TitleBackgroundV2Enabled,
            CharaSelectPlacementEnabled = configuration.TitleBackgroundCharaSelectPlacementEnabled,
            CharaSelectPlacementCandidateId = configuration.TitleBackgroundCharaSelectPlacementCandidateId,
            CharaSelectPlacementPositionCaptured = configuration.TitleBackgroundCharaSelectPlacementPositionCaptured,
            CharaSelectPlacementPositionX = configuration.TitleBackgroundCharaSelectPlacementPositionX,
            CharaSelectPlacementPositionY = configuration.TitleBackgroundCharaSelectPlacementPositionY,
            CharaSelectPlacementPositionZ = configuration.TitleBackgroundCharaSelectPlacementPositionZ,
            CharaSelectPlacementRotation = configuration.TitleBackgroundCharaSelectPlacementRotation,
            SceneCompositionEnabled = configuration.CharaSelectSceneCompositionEnabled,
            SelectedPresetId = configuration.TitleBackgroundSelectedPresetId,
            CandidateId = configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
            TerritoryPath = configuration.TitleBackgroundTerritoryPath,
            TerritoryTypeId = configuration.TitleBackgroundTerritoryTypeId,
            LayoutTerritoryTypeId = configuration.TitleBackgroundLayoutTerritoryTypeId,
            LayoutLayerFilterKey = configuration.TitleBackgroundLayoutLayerFilterKey,
            RuntimeMode = configuration.TitleBackgroundRuntimeMode,
            BackgroundMode = configuration.TitleBackgroundCharacterSelectBackgroundMode,
            LightingMode = configuration.TitleBackgroundCharacterSelectLightingMode,
            CameraFramingMode = configuration.TitleBackgroundCharaSelectCameraFramingMode,
            FixOnPassiveObservationEnabled = configuration.TitleBackgroundFixOnPassiveObservationEnabled,
            FixOnFocusAnchorOverrideEnabled = configuration.TitleBackgroundFixOnFocusAnchorOverrideEnabled,
            AnchorEnabled = configuration.TitleBackgroundCharaSelectAnchorEnabled,
            AnchorCandidateId = configuration.TitleBackgroundCharaSelectAnchorCandidateId,
            AnchorX = configuration.TitleBackgroundCharaSelectAnchorX,
            AnchorY = configuration.TitleBackgroundCharaSelectAnchorY,
            AnchorZ = configuration.TitleBackgroundCharaSelectAnchorZ,
            AnchorRotation = configuration.TitleBackgroundCharaSelectAnchorRotation,
            AnchorFrame = configuration.TitleBackgroundCharaSelectAnchorFrame,
            AnchorTerritoryTypeId = configuration.TitleBackgroundCharaSelectAnchorTerritoryTypeId,
            AnchorWorldExperimentalEnabled = configuration.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled,
            ViewEnabled = configuration.TitleBackgroundCharaSelectViewEnabled,
            ViewCandidateId = configuration.TitleBackgroundCharaSelectViewCandidateId,
            ViewCameraX = configuration.TitleBackgroundCharaSelectViewCameraX,
            ViewCameraY = configuration.TitleBackgroundCharaSelectViewCameraY,
            ViewCameraZ = configuration.TitleBackgroundCharaSelectViewCameraZ,
            ViewFocusX = configuration.TitleBackgroundCharaSelectViewFocusX,
            ViewFocusY = configuration.TitleBackgroundCharaSelectViewFocusY,
            ViewFocusZ = configuration.TitleBackgroundCharaSelectViewFocusZ,
            ViewFovY = configuration.TitleBackgroundCharaSelectViewFovY,
            ViewPoseCaptured = configuration.TitleBackgroundCharaSelectViewPoseCaptured,
            ViewDirH = configuration.TitleBackgroundCharaSelectViewDirH,
            ViewDirV = configuration.TitleBackgroundCharaSelectViewDirV,
            ViewDistance = configuration.TitleBackgroundCharaSelectViewDistance,
            FacingCalibrationCaptured = configuration.TitleBackgroundCharaSelectFacingCalibrationCaptured,
            FacingCalibrationCandidateId = configuration.TitleBackgroundCharaSelectFacingCalibrationCandidateId,
            FacingCalibrationOffset = configuration.TitleBackgroundCharaSelectFacingCalibrationOffset,
        };
    }

    public void ApplyTo(Configuration configuration)
    {
        configuration.TitleBackgroundOverrideEnabled = OverrideEnabled;
        configuration.TitleBackgroundCameraOverrideEnabled = CameraOverrideEnabled;
        configuration.TitleBackgroundIntegratedCompositionEnabled = IntegratedCompositionEnabled;
        configuration.TitleBackgroundV2Enabled = V2Enabled;
        configuration.TitleBackgroundCharaSelectPlacementEnabled = CharaSelectPlacementEnabled;
        configuration.TitleBackgroundCharaSelectPlacementCandidateId = CharaSelectPlacementCandidateId;
        configuration.TitleBackgroundCharaSelectPlacementPositionCaptured = CharaSelectPlacementPositionCaptured;
        configuration.TitleBackgroundCharaSelectPlacementPositionX = CharaSelectPlacementPositionX;
        configuration.TitleBackgroundCharaSelectPlacementPositionY = CharaSelectPlacementPositionY;
        configuration.TitleBackgroundCharaSelectPlacementPositionZ = CharaSelectPlacementPositionZ;
        configuration.TitleBackgroundCharaSelectPlacementRotation = CharaSelectPlacementRotation;
        configuration.CharaSelectSceneCompositionEnabled = SceneCompositionEnabled;
        configuration.TitleBackgroundSelectedPresetId = SelectedPresetId;
        configuration.TitleBackgroundCharacterSelectOverrideCandidateId = CandidateId;
        configuration.TitleBackgroundTerritoryPath = TerritoryPath;
        configuration.TitleBackgroundTerritoryTypeId = TerritoryTypeId;
        configuration.TitleBackgroundLayoutTerritoryTypeId = LayoutTerritoryTypeId;
        configuration.TitleBackgroundLayoutLayerFilterKey = LayoutLayerFilterKey;
        configuration.TitleBackgroundRuntimeMode = RuntimeMode;
        configuration.TitleBackgroundCharacterSelectBackgroundMode = BackgroundMode;
        configuration.TitleBackgroundCharacterSelectLightingMode = LightingMode;
        configuration.TitleBackgroundCharaSelectCameraFramingMode = CameraFramingMode;
        configuration.TitleBackgroundFixOnPassiveObservationEnabled = FixOnPassiveObservationEnabled;
        configuration.TitleBackgroundFixOnFocusAnchorOverrideEnabled = FixOnFocusAnchorOverrideEnabled;
        configuration.TitleBackgroundCharaSelectAnchorEnabled = AnchorEnabled;
        configuration.TitleBackgroundCharaSelectAnchorCandidateId = AnchorCandidateId;
        configuration.TitleBackgroundCharaSelectAnchorX = AnchorX;
        configuration.TitleBackgroundCharaSelectAnchorY = AnchorY;
        configuration.TitleBackgroundCharaSelectAnchorZ = AnchorZ;
        configuration.TitleBackgroundCharaSelectAnchorRotation = AnchorRotation;
        configuration.TitleBackgroundCharaSelectAnchorFrame = AnchorFrame;
        configuration.TitleBackgroundCharaSelectAnchorTerritoryTypeId = AnchorTerritoryTypeId;
        configuration.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = AnchorWorldExperimentalEnabled;
        configuration.TitleBackgroundCharaSelectViewEnabled = ViewEnabled;
        configuration.TitleBackgroundCharaSelectViewCandidateId = ViewCandidateId;
        configuration.TitleBackgroundCharaSelectViewCameraX = ViewCameraX;
        configuration.TitleBackgroundCharaSelectViewCameraY = ViewCameraY;
        configuration.TitleBackgroundCharaSelectViewCameraZ = ViewCameraZ;
        configuration.TitleBackgroundCharaSelectViewFocusX = ViewFocusX;
        configuration.TitleBackgroundCharaSelectViewFocusY = ViewFocusY;
        configuration.TitleBackgroundCharaSelectViewFocusZ = ViewFocusZ;
        configuration.TitleBackgroundCharaSelectViewFovY = ViewFovY;
        configuration.TitleBackgroundCharaSelectViewPoseCaptured = ViewPoseCaptured;
        configuration.TitleBackgroundCharaSelectViewDirH = ViewDirH;
        configuration.TitleBackgroundCharaSelectViewDirV = ViewDirV;
        configuration.TitleBackgroundCharaSelectViewDistance = ViewDistance;
        configuration.TitleBackgroundCharaSelectFacingCalibrationCaptured = FacingCalibrationCaptured;
        configuration.TitleBackgroundCharaSelectFacingCalibrationCandidateId = FacingCalibrationCandidateId;
        configuration.TitleBackgroundCharaSelectFacingCalibrationOffset = FacingCalibrationOffset;
    }
}

internal sealed record TitleBackgroundAutomaticCheckRecoveryJournal(
    int SchemaVersion,
    string RunId,
    DateTimeOffset StartedAt,
    TitleBackgroundAutomaticCheckSettingsSnapshot OriginalSettings)
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "title-background-auto-check-recovery.json";

    public static TitleBackgroundAutomaticCheckRecoveryJournal Create(
        string runId,
        DateTimeOffset startedAt,
        Configuration configuration)
    {
        return new TitleBackgroundAutomaticCheckRecoveryJournal(
            CurrentSchemaVersion,
            runId,
            startedAt,
            TitleBackgroundAutomaticCheckSettingsSnapshot.Capture(configuration));
    }

    public static string Serialize(TitleBackgroundAutomaticCheckRecoveryJournal journal)
    {
        return JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true });
    }

    public static TitleBackgroundAutomaticCheckRecoveryJournal? Deserialize(string json)
    {
        var journal = JsonSerializer.Deserialize<TitleBackgroundAutomaticCheckRecoveryJournal>(json);
        return journal?.SchemaVersion == CurrentSchemaVersion ? journal : null;
    }
}
