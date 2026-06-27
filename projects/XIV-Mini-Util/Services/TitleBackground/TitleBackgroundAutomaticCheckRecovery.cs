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
    public bool ViewEnabled { get; init; }
    public string ViewCandidateId { get; init; } = string.Empty;
    public float ViewCameraX { get; init; }
    public float ViewCameraY { get; init; }
    public float ViewCameraZ { get; init; }
    public float ViewFocusX { get; init; }
    public float ViewFocusY { get; init; }
    public float ViewFocusZ { get; init; }
    public float ViewFovY { get; init; }

    public static TitleBackgroundAutomaticCheckSettingsSnapshot Capture(Configuration configuration)
    {
        return new TitleBackgroundAutomaticCheckSettingsSnapshot
        {
            OverrideEnabled = configuration.TitleBackgroundOverrideEnabled,
            CameraOverrideEnabled = configuration.TitleBackgroundCameraOverrideEnabled,
            IntegratedCompositionEnabled = configuration.TitleBackgroundIntegratedCompositionEnabled,
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
            ViewEnabled = configuration.TitleBackgroundCharaSelectViewEnabled,
            ViewCandidateId = configuration.TitleBackgroundCharaSelectViewCandidateId,
            ViewCameraX = configuration.TitleBackgroundCharaSelectViewCameraX,
            ViewCameraY = configuration.TitleBackgroundCharaSelectViewCameraY,
            ViewCameraZ = configuration.TitleBackgroundCharaSelectViewCameraZ,
            ViewFocusX = configuration.TitleBackgroundCharaSelectViewFocusX,
            ViewFocusY = configuration.TitleBackgroundCharaSelectViewFocusY,
            ViewFocusZ = configuration.TitleBackgroundCharaSelectViewFocusZ,
            ViewFovY = configuration.TitleBackgroundCharaSelectViewFovY,
        };
    }

    public void ApplyTo(Configuration configuration)
    {
        configuration.TitleBackgroundOverrideEnabled = OverrideEnabled;
        configuration.TitleBackgroundCameraOverrideEnabled = CameraOverrideEnabled;
        configuration.TitleBackgroundIntegratedCompositionEnabled = IntegratedCompositionEnabled;
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
        configuration.TitleBackgroundCharaSelectViewEnabled = ViewEnabled;
        configuration.TitleBackgroundCharaSelectViewCandidateId = ViewCandidateId;
        configuration.TitleBackgroundCharaSelectViewCameraX = ViewCameraX;
        configuration.TitleBackgroundCharaSelectViewCameraY = ViewCameraY;
        configuration.TitleBackgroundCharaSelectViewCameraZ = ViewCameraZ;
        configuration.TitleBackgroundCharaSelectViewFocusX = ViewFocusX;
        configuration.TitleBackgroundCharaSelectViewFocusY = ViewFocusY;
        configuration.TitleBackgroundCharaSelectViewFocusZ = ViewFocusZ;
        configuration.TitleBackgroundCharaSelectViewFovY = ViewFovY;
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
