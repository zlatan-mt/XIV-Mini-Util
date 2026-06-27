// Path: projects/XIV-Mini-Util/Configuration.TitleBackground.cs
// Description: TitleBackground 関連の保存設定を保持する
// Reason: Configuration の巨大化を抑え、JSON プロパティ互換を維持したまま機能別に分割するため
using System.Text.Json.Serialization;
using XivMiniUtil.Services.TitleBackground;

namespace XivMiniUtil;

public sealed partial class Configuration
{
    // タイトル背景設定
    public bool TitleBackgroundOverrideEnabled { get; set; } = false;
    public bool TitleBackgroundCameraOverrideEnabled { get; set; } = false;
    public bool TitleBackgroundIntegratedCompositionEnabled { get; set; } = false;
    public string TitleBackgroundSelectedPresetId { get; set; } = string.Empty;
    public string TitleBackgroundCharacterSelectOverrideCandidateId { get; set; } = string.Empty;
    public bool TitleBackgroundCharacterSelectManualCandidate1Enabled { get; set; } = false;
    public string TitleBackgroundCharacterSelectManualCandidate1DisplayName { get; set; } = string.Empty;
    public string TitleBackgroundCharacterSelectManualCandidate1TerritoryPath { get; set; } = string.Empty;
    public uint TitleBackgroundCharacterSelectManualCandidate1TerritoryId { get; set; } = 0;
    public uint TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey { get; set; } = 0;
    public TitleBackgroundCharacterSelectExpectedBrightness TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness { get; set; } = TitleBackgroundCharacterSelectExpectedBrightness.Unknown;
    public TitleBackgroundRuntimeMode TitleBackgroundRuntimeMode { get; set; } = TitleBackgroundRuntimeMode.ResolveOnly;
    public TitleBackgroundCharacterSelectBackgroundMode TitleBackgroundCharacterSelectBackgroundMode { get; set; } = TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly;
    public TitleBackgroundCharacterSelectLightingMode TitleBackgroundCharacterSelectLightingMode { get; set; } = TitleBackgroundCharacterSelectLightingMode.Default;
    public TitleBackgroundSettingsDisplayMode TitleBackgroundSettingsDisplayMode { get; set; } = TitleBackgroundSettingsDisplayMode.Simple;
    public TitleBackgroundCharaSelectCameraFramingMode TitleBackgroundCharaSelectCameraFramingMode { get; set; } = TitleBackgroundCharaSelectCameraFramingMode.Default;
    public TitleBackgroundCharacterVisualStatus TitleBackgroundCharacterVisualStatus { get; set; } = TitleBackgroundCharacterVisualStatus.Unknown;
    public bool TitleBackgroundCapturedCameraProfileEnabled { get; set; } = false;
    public string TitleBackgroundCapturedCameraProfileSource { get; set; } = string.Empty;
    public float TitleBackgroundCapturedDirH { get; set; } = 0f;
    public float TitleBackgroundCapturedDirV { get; set; } = 0f;
    public float TitleBackgroundCapturedDistance { get; set; } = 0f;
    public float TitleBackgroundCapturedPositionX { get; set; } = 0f;
    public float TitleBackgroundCapturedPositionY { get; set; } = 0f;
    public float TitleBackgroundCapturedPositionZ { get; set; } = 0f;
    public float TitleBackgroundCapturedLookAtX { get; set; } = 0f;
    public float TitleBackgroundCapturedLookAtY { get; set; } = 0f;
    public float TitleBackgroundCapturedLookAtZ { get; set; } = 0f;
    public string TitleBackgroundCapturedCameraProfileCapturedAt { get; set; } = string.Empty;
    public TitleBackgroundQuickCheckLevel TitleBackgroundLastQuickCheckResult { get; set; } = TitleBackgroundQuickCheckLevel.NotRun;
    public string TitleBackgroundLastQuickCheckCandidateId { get; set; } = string.Empty;
    public string TitleBackgroundLastQuickCheckReason { get; set; } = string.Empty;
    public string TitleBackgroundLastQuickCheckNextAction { get; set; } = string.Empty;
    public string TitleBackgroundLastQuickCheckTime { get; set; } = string.Empty;
    public string TitleBackgroundLastQuickCheckDetailFileName { get; set; } = string.Empty;
    // Character Select 陸上アンカー（湖上ではなく陸上の固定立ち位置）。capture+nudge でゲーム内確定する。
    public bool TitleBackgroundCharaSelectAnchorEnabled { get; set; } = false;
    public string TitleBackgroundCharaSelectAnchorCandidateId { get; set; } = string.Empty;
    public float TitleBackgroundCharaSelectAnchorX { get; set; } = 0f;
    public float TitleBackgroundCharaSelectAnchorY { get; set; } = 0f;
    public float TitleBackgroundCharaSelectAnchorZ { get; set; } = 0f;
    public float TitleBackgroundCharaSelectAnchorRotation { get; set; } = 0f;
    // アンカー取得元のフレーム種別（world / lobby-native / chara-select-fallback / unknown）。
    // placement/カメラ挙動には影響しない診断用 provenance タグ。R 実験で座標系を判別するために保持する。
    public string TitleBackgroundCharaSelectAnchorFrame { get; set; } = string.Empty;
    // FixOn フックを passive 観測専用（override 無し）で装着するか。発火可否の診断用。既定 OFF で挙動不変。
    public bool TitleBackgroundFixOnPassiveObservationEnabled { get; set; } = false;
    // 保存済み陸上アンカー座標を FixOn の焦点へ「候補一致時のみ」適用するか。
    // passive 観測（上書きしない）とは独立した専用ゲート。既定 OFF で挙動不変。
    public bool TitleBackgroundFixOnFocusAnchorOverrideEnabled { get; set; } = false;
    // SavePluginConfig (Dalamud) は Newtonsoft.Json、ExportToBase64 は System.Text.Json を使うため両方の互換属性が必要
    [Newtonsoft.Json.JsonProperty("TitleBackgroundPhase2MExperimentalApplyMode")]
    [JsonPropertyName("TitleBackgroundPhase2MExperimentalApplyMode")]
    public TitleBackgroundCharacterPlacementExperimentalApplyMode TitleBackgroundCharacterPlacementExperimentalApplyMode { get; set; } = TitleBackgroundCharacterPlacementExperimentalApplyMode.None;
    public TitleBackgroundResolverMode TitleBackgroundCreateSceneResolverMode { get; set; } = TitleBackgroundResolverMode.AutoDiagnosticOnly;
    public TitleBackgroundResolverMode TitleBackgroundLobbyUpdateResolverMode { get; set; } = TitleBackgroundResolverMode.AutoDiagnosticOnly;
    public string TitleBackgroundTerritoryPath { get; set; } = string.Empty;
    public uint TitleBackgroundTerritoryTypeId { get; set; } = 0;
    public uint TitleBackgroundLayoutTerritoryTypeId { get; set; } = 0;
    public uint TitleBackgroundLayoutLayerFilterKey { get; set; } = 0;
    public float TitleBackgroundCharacterPositionX { get; set; } = 0f;
    public float TitleBackgroundCharacterPositionY { get; set; } = 0f;
    public float TitleBackgroundCharacterPositionZ { get; set; } = 0f;
    public float TitleBackgroundCharacterRotation { get; set; } = 0f;
    public float TitleBackgroundCameraX { get; set; } = 0f;
    public float TitleBackgroundCameraY { get; set; } = 0f;
    public float TitleBackgroundCameraZ { get; set; } = 0f;
    public float TitleBackgroundFocusX { get; set; } = 0f;
    public float TitleBackgroundFocusY { get; set; } = 0f;
    public float TitleBackgroundFocusZ { get; set; } = 0f;
    public float TitleBackgroundFovY { get; set; } = TitleBackgroundPreset.DefaultFovY;
    public byte TitleBackgroundWeatherId { get; set; } = 0;
    public ushort TitleBackgroundTimeOffset { get; set; } = 0;
    public string TitleBackgroundBgmPath { get; set; } = string.Empty;
    public string TitleBackgroundCreateSceneSignature { get; set; } = "E8 ?? ?? ?? ?? 66 89 3D ?? ?? ?? ?? E9";
    public string TitleBackgroundFixOnSignature { get; set; } = "C6 81 ?? ?? ?? ?? ?? 0F 28 CB 8B 02";
    public string TitleBackgroundLobbyUpdateSignature { get; set; } = "E8 ?? ?? ?? ?? 80 BF ?? ?? ?? ?? ?? 48 8D 35";
    public string TitleBackgroundLoadLobbySceneSignature { get; set; } = "48 89 5C 24 ?? 57 48 83 EC ?? 8B D9 E8";
    public string TitleBackgroundLobbyCurrentMapSignature { get; set; } = "66 89 05 ?? ?? ?? ?? 66 89 05 ?? ?? ?? ?? 66 89 05 ?? ?? ?? ?? 48 8B 4B";
    public string TitleBackgroundCalculateLobbyCameraLookAtYSignature { get; set; } = "48 83 EC ?? F3 41 0F 10 01 0F 28 D1";
    public string TitleBackgroundSetCameraCurveMidPointSignature { get; set; } = "0F 57 C0 0F 2F C1 73 ?? F3 0F 11 89";
    public string TitleBackgroundCalculateCameraCurveLowAndHighPointSignature { get; set; } = "F3 0F 10 81 ?? ?? ?? ?? F3 0F 11 89";
    private void ApplyTitleBackgroundFrom(Configuration source)
    {
        TitleBackgroundOverrideEnabled = source.TitleBackgroundOverrideEnabled;
        TitleBackgroundCameraOverrideEnabled = source.TitleBackgroundCameraOverrideEnabled;
        TitleBackgroundIntegratedCompositionEnabled = source.TitleBackgroundIntegratedCompositionEnabled;
        TitleBackgroundSelectedPresetId = TitleBackgroundBuiltInPresetCatalog.NormalizeId(source.TitleBackgroundSelectedPresetId);
        TitleBackgroundCharacterSelectOverrideCandidateId = NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(source.TitleBackgroundCharacterSelectOverrideCandidateId);
        TitleBackgroundCharacterSelectManualCandidate1Enabled = source.TitleBackgroundCharacterSelectManualCandidate1Enabled;
        TitleBackgroundCharacterSelectManualCandidate1DisplayName = NormalizeTitleBackgroundManualCandidateDisplayName(source.TitleBackgroundCharacterSelectManualCandidate1DisplayName);
        TitleBackgroundCharacterSelectManualCandidate1TerritoryPath = NormalizeTitleBackgroundTerritoryPath(source.TitleBackgroundCharacterSelectManualCandidate1TerritoryPath);
        TitleBackgroundCharacterSelectManualCandidate1TerritoryId = source.TitleBackgroundCharacterSelectManualCandidate1TerritoryId;
        TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey = source.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey;
        TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness = NormalizeTitleBackgroundCharacterSelectExpectedBrightness(source.TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness);
        TitleBackgroundRuntimeMode = NormalizeTitleBackgroundRuntimeMode(source.TitleBackgroundRuntimeMode);
        TitleBackgroundCharacterSelectBackgroundMode = NormalizeTitleBackgroundCharacterSelectBackgroundMode(source.TitleBackgroundCharacterSelectBackgroundMode);
        TitleBackgroundCharacterSelectLightingMode = NormalizeTitleBackgroundCharacterSelectLightingMode(source.TitleBackgroundCharacterSelectLightingMode);
        TitleBackgroundSettingsDisplayMode = NormalizeTitleBackgroundSettingsDisplayMode(source.TitleBackgroundSettingsDisplayMode);
        TitleBackgroundCharaSelectCameraFramingMode = NormalizeTitleBackgroundCameraFramingMode(source.TitleBackgroundCharaSelectCameraFramingMode);
        TitleBackgroundCharacterVisualStatus = NormalizeTitleBackgroundCharacterVisualStatus(source.TitleBackgroundCharacterVisualStatus);
        TitleBackgroundCapturedCameraProfileEnabled = source.TitleBackgroundCapturedCameraProfileEnabled;
        TitleBackgroundCapturedCameraProfileSource = NormalizeShortDiagnostic(source.TitleBackgroundCapturedCameraProfileSource);
        TitleBackgroundCapturedDirH = SanitizeCoordinate(source.TitleBackgroundCapturedDirH);
        TitleBackgroundCapturedDirV = SanitizeCoordinate(source.TitleBackgroundCapturedDirV);
        TitleBackgroundCapturedDistance = SanitizeCoordinate(source.TitleBackgroundCapturedDistance);
        TitleBackgroundCapturedPositionX = SanitizeCoordinate(source.TitleBackgroundCapturedPositionX);
        TitleBackgroundCapturedPositionY = SanitizeCoordinate(source.TitleBackgroundCapturedPositionY);
        TitleBackgroundCapturedPositionZ = SanitizeCoordinate(source.TitleBackgroundCapturedPositionZ);
        TitleBackgroundCapturedLookAtX = SanitizeCoordinate(source.TitleBackgroundCapturedLookAtX);
        TitleBackgroundCapturedLookAtY = SanitizeCoordinate(source.TitleBackgroundCapturedLookAtY);
        TitleBackgroundCapturedLookAtZ = SanitizeCoordinate(source.TitleBackgroundCapturedLookAtZ);
        TitleBackgroundCapturedCameraProfileCapturedAt = NormalizeShortDiagnostic(source.TitleBackgroundCapturedCameraProfileCapturedAt);
        TitleBackgroundLastQuickCheckResult = NormalizeTitleBackgroundQuickCheckLevel(source.TitleBackgroundLastQuickCheckResult);
        TitleBackgroundLastQuickCheckCandidateId = NormalizeShortDiagnostic(source.TitleBackgroundLastQuickCheckCandidateId);
        TitleBackgroundLastQuickCheckReason = NormalizeShortDiagnostic(source.TitleBackgroundLastQuickCheckReason);
        TitleBackgroundLastQuickCheckNextAction = NormalizeShortDiagnostic(source.TitleBackgroundLastQuickCheckNextAction);
        TitleBackgroundLastQuickCheckTime = NormalizeShortDiagnostic(source.TitleBackgroundLastQuickCheckTime);
        TitleBackgroundLastQuickCheckDetailFileName = NormalizeShortDiagnostic(source.TitleBackgroundLastQuickCheckDetailFileName);
        TitleBackgroundCharaSelectAnchorEnabled = source.TitleBackgroundCharaSelectAnchorEnabled;
        TitleBackgroundCharaSelectAnchorCandidateId = NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(source.TitleBackgroundCharaSelectAnchorCandidateId);
        TitleBackgroundCharaSelectAnchorX = SanitizeCoordinate(source.TitleBackgroundCharaSelectAnchorX);
        TitleBackgroundCharaSelectAnchorY = SanitizeCoordinate(source.TitleBackgroundCharaSelectAnchorY);
        TitleBackgroundCharaSelectAnchorZ = SanitizeCoordinate(source.TitleBackgroundCharaSelectAnchorZ);
        TitleBackgroundCharaSelectAnchorRotation = SanitizeCoordinate(source.TitleBackgroundCharaSelectAnchorRotation);
        TitleBackgroundCharaSelectAnchorFrame = NormalizeShortDiagnostic(source.TitleBackgroundCharaSelectAnchorFrame);
        TitleBackgroundFixOnPassiveObservationEnabled = source.TitleBackgroundFixOnPassiveObservationEnabled;
        TitleBackgroundFixOnFocusAnchorOverrideEnabled = source.TitleBackgroundFixOnFocusAnchorOverrideEnabled;
        TitleBackgroundCharacterPlacementExperimentalApplyMode = NormalizeTitleBackgroundCharacterPlacementExperimentalApplyMode(source.TitleBackgroundCharacterPlacementExperimentalApplyMode);
        TitleBackgroundCreateSceneResolverMode = NormalizeTitleBackgroundResolverMode(source.TitleBackgroundCreateSceneResolverMode);
        TitleBackgroundLobbyUpdateResolverMode = NormalizeTitleBackgroundResolverMode(source.TitleBackgroundLobbyUpdateResolverMode);
        TitleBackgroundTerritoryPath = NormalizeTitleBackgroundTerritoryPath(source.TitleBackgroundTerritoryPath);
        TitleBackgroundTerritoryTypeId = source.TitleBackgroundTerritoryTypeId;
        TitleBackgroundLayoutTerritoryTypeId = source.TitleBackgroundLayoutTerritoryTypeId;
        TitleBackgroundLayoutLayerFilterKey = source.TitleBackgroundLayoutLayerFilterKey;
        TitleBackgroundCharacterPositionX = SanitizeCoordinate(source.TitleBackgroundCharacterPositionX);
        TitleBackgroundCharacterPositionY = SanitizeCoordinate(source.TitleBackgroundCharacterPositionY);
        TitleBackgroundCharacterPositionZ = SanitizeCoordinate(source.TitleBackgroundCharacterPositionZ);
        TitleBackgroundCharacterRotation = SanitizeCoordinate(source.TitleBackgroundCharacterRotation);
        TitleBackgroundCameraX = SanitizeCoordinate(source.TitleBackgroundCameraX);
        TitleBackgroundCameraY = SanitizeCoordinate(source.TitleBackgroundCameraY);
        TitleBackgroundCameraZ = SanitizeCoordinate(source.TitleBackgroundCameraZ);
        TitleBackgroundFocusX = SanitizeCoordinate(source.TitleBackgroundFocusX);
        TitleBackgroundFocusY = SanitizeCoordinate(source.TitleBackgroundFocusY);
        TitleBackgroundFocusZ = SanitizeCoordinate(source.TitleBackgroundFocusZ);
        TitleBackgroundFovY = SanitizeFovY(source.TitleBackgroundFovY);
        TitleBackgroundWeatherId = source.TitleBackgroundWeatherId;
        TitleBackgroundTimeOffset = source.TitleBackgroundTimeOffset;
        TitleBackgroundBgmPath = NormalizeAssetPath(source.TitleBackgroundBgmPath);
        TitleBackgroundCreateSceneSignature = NormalizeSignature(source.TitleBackgroundCreateSceneSignature);
        TitleBackgroundFixOnSignature = NormalizeSignature(source.TitleBackgroundFixOnSignature);
        TitleBackgroundLobbyUpdateSignature = NormalizeSignature(source.TitleBackgroundLobbyUpdateSignature);
        TitleBackgroundLoadLobbySceneSignature = NormalizeSignature(source.TitleBackgroundLoadLobbySceneSignature);
        TitleBackgroundLobbyCurrentMapSignature = NormalizeSignature(source.TitleBackgroundLobbyCurrentMapSignature);
        TitleBackgroundCalculateLobbyCameraLookAtYSignature = NormalizeSignature(source.TitleBackgroundCalculateLobbyCameraLookAtYSignature);
        TitleBackgroundSetCameraCurveMidPointSignature = NormalizeSignature(source.TitleBackgroundSetCameraCurveMidPointSignature);
        TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = NormalizeSignature(source.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature);
    }

    private bool NormalizeTitleBackgroundSettings()
    {
        var changed = false;

        var normalizedTitleTerritoryPath = NormalizeTitleBackgroundTerritoryPath(TitleBackgroundTerritoryPath);
        if (TitleBackgroundTerritoryPath != normalizedTitleTerritoryPath)
        {
            TitleBackgroundTerritoryPath = normalizedTitleTerritoryPath;
            changed = true;
        }

        var normalizedSelectedPresetId = TitleBackgroundBuiltInPresetCatalog.NormalizeId(TitleBackgroundSelectedPresetId);
        if (TitleBackgroundSelectedPresetId != normalizedSelectedPresetId)
        {
            TitleBackgroundSelectedPresetId = normalizedSelectedPresetId;
            changed = true;
        }

        var normalizedOverrideCandidateId = NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(TitleBackgroundCharacterSelectOverrideCandidateId);
        if (TitleBackgroundCharacterSelectOverrideCandidateId != normalizedOverrideCandidateId)
        {
            TitleBackgroundCharacterSelectOverrideCandidateId = normalizedOverrideCandidateId;
            changed = true;
        }

        var normalizedManualDisplayName = NormalizeTitleBackgroundManualCandidateDisplayName(TitleBackgroundCharacterSelectManualCandidate1DisplayName);
        if (TitleBackgroundCharacterSelectManualCandidate1DisplayName != normalizedManualDisplayName)
        {
            TitleBackgroundCharacterSelectManualCandidate1DisplayName = normalizedManualDisplayName;
            changed = true;
        }

        var normalizedManualTerritoryPath = NormalizeTitleBackgroundTerritoryPath(TitleBackgroundCharacterSelectManualCandidate1TerritoryPath);
        if (TitleBackgroundCharacterSelectManualCandidate1TerritoryPath != normalizedManualTerritoryPath)
        {
            TitleBackgroundCharacterSelectManualCandidate1TerritoryPath = normalizedManualTerritoryPath;
            changed = true;
        }

        var normalizedManualExpectedBrightness = NormalizeTitleBackgroundCharacterSelectExpectedBrightness(TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness);
        if (TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness != normalizedManualExpectedBrightness)
        {
            TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness = normalizedManualExpectedBrightness;
            changed = true;
        }

        var normalizedTitleRuntimeMode = NormalizeTitleBackgroundRuntimeMode(TitleBackgroundRuntimeMode);
        if (TitleBackgroundRuntimeMode != normalizedTitleRuntimeMode)
        {
            TitleBackgroundRuntimeMode = normalizedTitleRuntimeMode;
            changed = true;
        }

        var normalizedCharaSelectBackgroundMode = NormalizeTitleBackgroundCharacterSelectBackgroundMode(TitleBackgroundCharacterSelectBackgroundMode);
        if (TitleBackgroundCharacterSelectBackgroundMode != normalizedCharaSelectBackgroundMode)
        {
            TitleBackgroundCharacterSelectBackgroundMode = normalizedCharaSelectBackgroundMode;
            changed = true;
        }

        var normalizedCharaSelectLightingMode = NormalizeTitleBackgroundCharacterSelectLightingMode(TitleBackgroundCharacterSelectLightingMode);
        if (TitleBackgroundCharacterSelectLightingMode != normalizedCharaSelectLightingMode)
        {
            TitleBackgroundCharacterSelectLightingMode = normalizedCharaSelectLightingMode;
            changed = true;
        }

        var normalizedSettingsDisplayMode = NormalizeTitleBackgroundSettingsDisplayMode(TitleBackgroundSettingsDisplayMode);
        if (TitleBackgroundSettingsDisplayMode != normalizedSettingsDisplayMode)
        {
            TitleBackgroundSettingsDisplayMode = normalizedSettingsDisplayMode;
            changed = true;
        }

        var normalizedCameraFramingMode = NormalizeTitleBackgroundCameraFramingMode(TitleBackgroundCharaSelectCameraFramingMode);
        if (TitleBackgroundCharaSelectCameraFramingMode != normalizedCameraFramingMode)
        {
            TitleBackgroundCharaSelectCameraFramingMode = normalizedCameraFramingMode;
            changed = true;
        }

        var normalizedCharacterVisualStatus = NormalizeTitleBackgroundCharacterVisualStatus(TitleBackgroundCharacterVisualStatus);
        if (TitleBackgroundCharacterVisualStatus != normalizedCharacterVisualStatus)
        {
            TitleBackgroundCharacterVisualStatus = normalizedCharacterVisualStatus;
            changed = true;
        }

        var normalizedQuickCheckLevel = NormalizeTitleBackgroundQuickCheckLevel(TitleBackgroundLastQuickCheckResult);
        if (TitleBackgroundLastQuickCheckResult != normalizedQuickCheckLevel)
        {
            TitleBackgroundLastQuickCheckResult = normalizedQuickCheckLevel;
            changed = true;
        }

        var normalizedCharacterPlacementExperimentalApplyMode = NormalizeTitleBackgroundCharacterPlacementExperimentalApplyMode(TitleBackgroundCharacterPlacementExperimentalApplyMode);
        if (TitleBackgroundCharacterPlacementExperimentalApplyMode != normalizedCharacterPlacementExperimentalApplyMode)
        {
            TitleBackgroundCharacterPlacementExperimentalApplyMode = normalizedCharacterPlacementExperimentalApplyMode;
            changed = true;
        }

        var normalizedCreateSceneResolverMode = NormalizeTitleBackgroundResolverMode(TitleBackgroundCreateSceneResolverMode);
        var normalizedLobbyUpdateResolverMode = NormalizeTitleBackgroundResolverMode(TitleBackgroundLobbyUpdateResolverMode);
        if (TitleBackgroundCreateSceneResolverMode != normalizedCreateSceneResolverMode
            || TitleBackgroundLobbyUpdateResolverMode != normalizedLobbyUpdateResolverMode)
        {
            TitleBackgroundCreateSceneResolverMode = normalizedCreateSceneResolverMode;
            TitleBackgroundLobbyUpdateResolverMode = normalizedLobbyUpdateResolverMode;
            changed = true;
        }

        var normalizedTitleCharacterPositionX = SanitizeCoordinate(TitleBackgroundCharacterPositionX);
        var normalizedTitleCharacterPositionY = SanitizeCoordinate(TitleBackgroundCharacterPositionY);
        var normalizedTitleCharacterPositionZ = SanitizeCoordinate(TitleBackgroundCharacterPositionZ);
        var normalizedTitleCharacterRotation = SanitizeCoordinate(TitleBackgroundCharacterRotation);
        if (TitleBackgroundCharacterPositionX != normalizedTitleCharacterPositionX
            || TitleBackgroundCharacterPositionY != normalizedTitleCharacterPositionY
            || TitleBackgroundCharacterPositionZ != normalizedTitleCharacterPositionZ
            || TitleBackgroundCharacterRotation != normalizedTitleCharacterRotation)
        {
            TitleBackgroundCharacterPositionX = normalizedTitleCharacterPositionX;
            TitleBackgroundCharacterPositionY = normalizedTitleCharacterPositionY;
            TitleBackgroundCharacterPositionZ = normalizedTitleCharacterPositionZ;
            TitleBackgroundCharacterRotation = normalizedTitleCharacterRotation;
            changed = true;
        }

        var normalizedTitleCameraX = SanitizeCoordinate(TitleBackgroundCameraX);
        var normalizedTitleCameraY = SanitizeCoordinate(TitleBackgroundCameraY);
        var normalizedTitleCameraZ = SanitizeCoordinate(TitleBackgroundCameraZ);
        var normalizedTitleFocusX = SanitizeCoordinate(TitleBackgroundFocusX);
        var normalizedTitleFocusY = SanitizeCoordinate(TitleBackgroundFocusY);
        var normalizedTitleFocusZ = SanitizeCoordinate(TitleBackgroundFocusZ);
        if (TitleBackgroundCameraX != normalizedTitleCameraX
            || TitleBackgroundCameraY != normalizedTitleCameraY
            || TitleBackgroundCameraZ != normalizedTitleCameraZ
            || TitleBackgroundFocusX != normalizedTitleFocusX
            || TitleBackgroundFocusY != normalizedTitleFocusY
            || TitleBackgroundFocusZ != normalizedTitleFocusZ)
        {
            TitleBackgroundCameraX = normalizedTitleCameraX;
            TitleBackgroundCameraY = normalizedTitleCameraY;
            TitleBackgroundCameraZ = normalizedTitleCameraZ;
            TitleBackgroundFocusX = normalizedTitleFocusX;
            TitleBackgroundFocusY = normalizedTitleFocusY;
            TitleBackgroundFocusZ = normalizedTitleFocusZ;
            changed = true;
        }

        var normalizedTitleFovY = SanitizeFovY(TitleBackgroundFovY);
        if (TitleBackgroundFovY != normalizedTitleFovY)
        {
            TitleBackgroundFovY = normalizedTitleFovY;
            changed = true;
        }

        var normalizedCapturedProfileSource = NormalizeShortDiagnostic(TitleBackgroundCapturedCameraProfileSource);
        var normalizedCapturedAt = NormalizeShortDiagnostic(TitleBackgroundCapturedCameraProfileCapturedAt);
        var normalizedCapturedDirH = SanitizeCoordinate(TitleBackgroundCapturedDirH);
        var normalizedCapturedDirV = SanitizeCoordinate(TitleBackgroundCapturedDirV);
        var normalizedCapturedDistance = SanitizeCoordinate(TitleBackgroundCapturedDistance);
        var normalizedCapturedPositionX = SanitizeCoordinate(TitleBackgroundCapturedPositionX);
        var normalizedCapturedPositionY = SanitizeCoordinate(TitleBackgroundCapturedPositionY);
        var normalizedCapturedPositionZ = SanitizeCoordinate(TitleBackgroundCapturedPositionZ);
        var normalizedCapturedLookAtX = SanitizeCoordinate(TitleBackgroundCapturedLookAtX);
        var normalizedCapturedLookAtY = SanitizeCoordinate(TitleBackgroundCapturedLookAtY);
        var normalizedCapturedLookAtZ = SanitizeCoordinate(TitleBackgroundCapturedLookAtZ);
        if (TitleBackgroundCapturedCameraProfileSource != normalizedCapturedProfileSource
            || TitleBackgroundCapturedCameraProfileCapturedAt != normalizedCapturedAt
            || TitleBackgroundCapturedDirH != normalizedCapturedDirH
            || TitleBackgroundCapturedDirV != normalizedCapturedDirV
            || TitleBackgroundCapturedDistance != normalizedCapturedDistance
            || TitleBackgroundCapturedPositionX != normalizedCapturedPositionX
            || TitleBackgroundCapturedPositionY != normalizedCapturedPositionY
            || TitleBackgroundCapturedPositionZ != normalizedCapturedPositionZ
            || TitleBackgroundCapturedLookAtX != normalizedCapturedLookAtX
            || TitleBackgroundCapturedLookAtY != normalizedCapturedLookAtY
            || TitleBackgroundCapturedLookAtZ != normalizedCapturedLookAtZ)
        {
            TitleBackgroundCapturedCameraProfileSource = normalizedCapturedProfileSource;
            TitleBackgroundCapturedCameraProfileCapturedAt = normalizedCapturedAt;
            TitleBackgroundCapturedDirH = normalizedCapturedDirH;
            TitleBackgroundCapturedDirV = normalizedCapturedDirV;
            TitleBackgroundCapturedDistance = normalizedCapturedDistance;
            TitleBackgroundCapturedPositionX = normalizedCapturedPositionX;
            TitleBackgroundCapturedPositionY = normalizedCapturedPositionY;
            TitleBackgroundCapturedPositionZ = normalizedCapturedPositionZ;
            TitleBackgroundCapturedLookAtX = normalizedCapturedLookAtX;
            TitleBackgroundCapturedLookAtY = normalizedCapturedLookAtY;
            TitleBackgroundCapturedLookAtZ = normalizedCapturedLookAtZ;
            changed = true;
        }

        var normalizedTitleBgmPath = NormalizeAssetPath(TitleBackgroundBgmPath);
        if (TitleBackgroundBgmPath != normalizedTitleBgmPath)
        {
            TitleBackgroundBgmPath = normalizedTitleBgmPath;
            changed = true;
        }

        changed |= NormalizeSignatureProperty(TitleBackgroundCreateSceneSignature, value => TitleBackgroundCreateSceneSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundFixOnSignature, value => TitleBackgroundFixOnSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundLobbyUpdateSignature, value => TitleBackgroundLobbyUpdateSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundLoadLobbySceneSignature, value => TitleBackgroundLoadLobbySceneSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundLobbyCurrentMapSignature, value => TitleBackgroundLobbyCurrentMapSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundCalculateLobbyCameraLookAtYSignature, value => TitleBackgroundCalculateLobbyCameraLookAtYSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundSetCameraCurveMidPointSignature, value => TitleBackgroundSetCameraCurveMidPointSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundCalculateCameraCurveLowAndHighPointSignature, value => TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = value);
        changed |= TitleBackgroundPresetApplicator.ClearInvalidSelectedPreset(this);

        return changed;
    }
}
