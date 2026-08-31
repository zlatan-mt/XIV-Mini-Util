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
    // V2 production path (Il Mheg proof)。true のとき legacy camera-maintain（saved-view per-frame
    // reassert / Phase2G generated curve override / curve-based camera ownership）は一切 arm せず、
    // scene-ready で bounded に 1 回だけ input params (DirH/DirV/Distance/FoV) を書く。
    // 既定 false で追加前と完全に同じ挙動。名前が JSON キーと一致するため両シリアライザで属性不要（skill §1.7）。
    public bool TitleBackgroundV2Enabled { get; set; } = false;
    // TitleEdit-informed Character Select placement path (Phase 0 対応表 §6 / §7(A))。true のとき
    // legacy camera-maintain / 毎フレーム DrawObject placement / FixOn override / Phase2G curve override は
    // 一切 arm しない（V2 と同じ排他規約）。キャラ配置は (新 scene generation) または (選択キャラ変更) の
    // ときだけ native GameObject.SetPosition + actor SetRotation(float) で bounded に書く。
    // 既定 false で追加前と完全に同じ挙動。名前が JSON キーと一致するため両シリアライザで属性不要（skill §1.7）。
    public bool TitleBackgroundCharaSelectPlacementEnabled { get; set; } = false;
    // 配置 path を適用する候補（不一致なら書かない fail-closed）。
    public string TitleBackgroundCharaSelectPlacementCandidateId { get; set; } = string.Empty;
    // 配置座標が one-click 実機 run の read-only capture で確定済みか。false の間は配置を書かない
    // （world→scene-local 推測をしない。source-backed 座標だけを使う）。
    public bool TitleBackgroundCharaSelectPlacementPositionCaptured { get; set; } = false;
    public float TitleBackgroundCharaSelectPlacementPositionX { get; set; } = 0f;
    public float TitleBackgroundCharaSelectPlacementPositionY { get; set; } = 0f;
    public float TitleBackgroundCharaSelectPlacementPositionZ { get; set; } = 0f;
    public float TitleBackgroundCharaSelectPlacementRotation { get; set; } = 0f;
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
    // 問題4: world アンカー保存時の TerritoryTypeId（実測 _clientState.TerritoryType）。
    // experimental world placement の territory 照合に使う。0 のときは適用しない（fail-closed）。
    public uint TitleBackgroundCharaSelectAnchorTerritoryTypeId { get; set; } = 0;
    // world 座標を experimental にキャラ配置へ適用するか。既定 OFF で挙動不変。実機で陸上一致が
    // 確認されるまで unverified 扱い（ground-verified へは昇格しない）。
    public bool TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled { get; set; } = false;
    // 「今の見え方を保存」した CharaSelect カメラ（TitleEdit 方式）。FixOn で camera+focus+fov を
    // scene-local 絶対値で 1 回だけ上書きするための保存値。候補一致時のみ適用。既定 OFF で挙動不変。
    public bool TitleBackgroundCharaSelectViewEnabled { get; set; } = false;
    public string TitleBackgroundCharaSelectViewCandidateId { get; set; } = string.Empty;
    public float TitleBackgroundCharaSelectViewCameraX { get; set; } = 0f;
    public float TitleBackgroundCharaSelectViewCameraY { get; set; } = 0f;
    public float TitleBackgroundCharaSelectViewCameraZ { get; set; } = 0f;
    public float TitleBackgroundCharaSelectViewFocusX { get; set; } = 0f;
    public float TitleBackgroundCharaSelectViewFocusY { get; set; } = 0f;
    public float TitleBackgroundCharaSelectViewFocusZ { get; set; } = 0f;
    public float TitleBackgroundCharaSelectViewFovY { get; set; } = TitleBackgroundPreset.DefaultFovY;
    // 保存 view のネイティブカメラ pose（DirH/DirV/Distance）。FixOn の camera 引数はエンジンが
    // DirH/DirV/Distance＋焦点から毎フレーム位置を再導出するため数フレームで負ける（view.trace 実測）。
    // pose 形式でも保存し、scene load 後に 1 回だけ LobbyCamera へ復元することで構図を持続させる。
    // PoseCaptured=false（旧バージョンで保存された view）は pose 復元せず従来挙動（後方互換）。
    public bool TitleBackgroundCharaSelectViewPoseCaptured { get; set; } = false;
    public float TitleBackgroundCharaSelectViewDirH { get; set; } = 0f;
    public float TitleBackgroundCharaSelectViewDirV { get; set; } = 0f;
    public float TitleBackgroundCharaSelectViewDistance { get; set; } = 0f;
    // 確認runの自然回転とcamera方向から求めたactor回転規約offset。候補一致時だけ使用する。
    // 未校正・旧configでは既定πを使うため、追加前と同じ挙動になる。
    public bool TitleBackgroundCharaSelectFacingCalibrationCaptured { get; set; } = false;
    public string TitleBackgroundCharaSelectFacingCalibrationCandidateId { get; set; } = string.Empty;
    public float TitleBackgroundCharaSelectFacingCalibrationOffset { get; set; } = TitleBackgroundCharaSelectCharacterFacing.DefaultCalibrationOffset;
    // FixOn フックを passive 観測専用（override 無し）で装着するか。発火可否の診断用。既定 OFF で挙動不変。
    public bool TitleBackgroundFixOnPassiveObservationEnabled { get; set; } = false;
    // 保存済み陸上アンカー座標を FixOn の焦点へ「候補一致時のみ」適用するか。
    // passive 観測（上書きしない）とは独立した専用ゲート。既定 OFF で挙動不変。
    public bool TitleBackgroundFixOnFocusAnchorOverrideEnabled { get; set; } = false;
    // 背景セッション中（pre-login・CharaSelect のみ）に環境時刻をエオルゼア正午へ毎フレーム
    // 固定するか。ログイン画面が実時刻・天候により暗くなる問題への対策。ログイン中には絶対に
    // 適用しない（IsLoggedIn ゲートで遮断）。既定 true（ユーザー要望により明るさ優先）。
    public bool TitleBackgroundEnvironmentNoonEnabled { get; set; } = true;
    // 背景セッション中（pre-login・CharaSelectのみ）に環境天候をClear Skiesへ毎フレーム固定するか。
    // 雨天時にログイン画面が暗いスカイドーム・濡れ表現になる問題への対策。ログイン中には絶対に
    // 適用しない（IsLoggedIn ゲートで遮断）。既定 true（ユーザー要望により明るさ優先）。
    public bool TitleBackgroundEnvironmentClearSkyEnabled { get; set; } = true;
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
    public string TitleBackgroundCreateSceneSignature { get; set; } = TitleBackgroundKnownSignatures.CreateScene;
    public string TitleBackgroundFixOnSignature { get; set; } = TitleBackgroundKnownSignatures.FixOn;
    public string TitleBackgroundLobbyUpdateSignature { get; set; } = TitleBackgroundKnownSignatures.LobbyUpdate;
    public string TitleBackgroundLoadLobbySceneSignature { get; set; } = TitleBackgroundKnownSignatures.LoadLobbyScene;
    public string TitleBackgroundLobbyCurrentMapSignature { get; set; } = TitleBackgroundKnownSignatures.LobbyCurrentMap;
    public string TitleBackgroundCalculateLobbyCameraLookAtYSignature { get; set; } = TitleBackgroundKnownSignatures.CalculateLobbyCameraLookAtY;
    public string TitleBackgroundSetCameraCurveMidPointSignature { get; set; } = TitleBackgroundKnownSignatures.SetCameraCurveMidPoint;
    public string TitleBackgroundCalculateCameraCurveLowAndHighPointSignature { get; set; } = TitleBackgroundKnownSignatures.CalculateCameraCurveLowAndHighPoint;
    private void ApplyTitleBackgroundFrom(Configuration source)
    {
        TitleBackgroundOverrideEnabled = source.TitleBackgroundOverrideEnabled;
        TitleBackgroundCameraOverrideEnabled = source.TitleBackgroundCameraOverrideEnabled;
        TitleBackgroundIntegratedCompositionEnabled = source.TitleBackgroundIntegratedCompositionEnabled;
        TitleBackgroundV2Enabled = source.TitleBackgroundV2Enabled;
        TitleBackgroundCharaSelectPlacementEnabled = source.TitleBackgroundCharaSelectPlacementEnabled;
        TitleBackgroundCharaSelectPlacementCandidateId = NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(source.TitleBackgroundCharaSelectPlacementCandidateId);
        TitleBackgroundCharaSelectPlacementPositionCaptured =
            source.TitleBackgroundCharaSelectPlacementPositionCaptured
            && float.IsFinite(source.TitleBackgroundCharaSelectPlacementPositionX)
            && float.IsFinite(source.TitleBackgroundCharaSelectPlacementPositionY)
            && float.IsFinite(source.TitleBackgroundCharaSelectPlacementPositionZ)
            && float.IsFinite(source.TitleBackgroundCharaSelectPlacementRotation);
        TitleBackgroundCharaSelectPlacementPositionX = SanitizeCoordinate(source.TitleBackgroundCharaSelectPlacementPositionX);
        TitleBackgroundCharaSelectPlacementPositionY = SanitizeCoordinate(source.TitleBackgroundCharaSelectPlacementPositionY);
        TitleBackgroundCharaSelectPlacementPositionZ = SanitizeCoordinate(source.TitleBackgroundCharaSelectPlacementPositionZ);
        TitleBackgroundCharaSelectPlacementRotation = SanitizeCoordinate(source.TitleBackgroundCharaSelectPlacementRotation);
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
        TitleBackgroundCharaSelectAnchorTerritoryTypeId = source.TitleBackgroundCharaSelectAnchorTerritoryTypeId;
        TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = source.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled;
        TitleBackgroundCharaSelectViewEnabled = source.TitleBackgroundCharaSelectViewEnabled;
        TitleBackgroundCharaSelectViewCandidateId = NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(source.TitleBackgroundCharaSelectViewCandidateId);
        TitleBackgroundCharaSelectViewCameraX = SanitizeCoordinate(source.TitleBackgroundCharaSelectViewCameraX);
        TitleBackgroundCharaSelectViewCameraY = SanitizeCoordinate(source.TitleBackgroundCharaSelectViewCameraY);
        TitleBackgroundCharaSelectViewCameraZ = SanitizeCoordinate(source.TitleBackgroundCharaSelectViewCameraZ);
        TitleBackgroundCharaSelectViewFocusX = SanitizeCoordinate(source.TitleBackgroundCharaSelectViewFocusX);
        TitleBackgroundCharaSelectViewFocusY = SanitizeCoordinate(source.TitleBackgroundCharaSelectViewFocusY);
        TitleBackgroundCharaSelectViewFocusZ = SanitizeCoordinate(source.TitleBackgroundCharaSelectViewFocusZ);
        TitleBackgroundCharaSelectViewFovY = SanitizeFovY(source.TitleBackgroundCharaSelectViewFovY);
        TitleBackgroundCharaSelectViewPoseCaptured = source.TitleBackgroundCharaSelectViewPoseCaptured;
        TitleBackgroundCharaSelectViewDirH = SanitizeCoordinate(source.TitleBackgroundCharaSelectViewDirH);
        TitleBackgroundCharaSelectViewDirV = SanitizeCoordinate(source.TitleBackgroundCharaSelectViewDirV);
        TitleBackgroundCharaSelectViewDistance = SanitizeCoordinate(source.TitleBackgroundCharaSelectViewDistance);
        TitleBackgroundCharaSelectFacingCalibrationCaptured =
            source.TitleBackgroundCharaSelectFacingCalibrationCaptured
            && float.IsFinite(source.TitleBackgroundCharaSelectFacingCalibrationOffset);
        TitleBackgroundCharaSelectFacingCalibrationCandidateId = NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(source.TitleBackgroundCharaSelectFacingCalibrationCandidateId);
        TitleBackgroundCharaSelectFacingCalibrationOffset = SanitizeCoordinate(source.TitleBackgroundCharaSelectFacingCalibrationOffset);
        TitleBackgroundFixOnPassiveObservationEnabled = source.TitleBackgroundFixOnPassiveObservationEnabled;
        TitleBackgroundFixOnFocusAnchorOverrideEnabled = source.TitleBackgroundFixOnFocusAnchorOverrideEnabled;
        TitleBackgroundEnvironmentNoonEnabled = source.TitleBackgroundEnvironmentNoonEnabled;
        TitleBackgroundEnvironmentClearSkyEnabled = source.TitleBackgroundEnvironmentClearSkyEnabled;
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

        // World experimental anchor は厳格な前提を満たさない限り自動有効化しない（fail-closed）。
        // 旧設定（territory 未保存）の昇格や、frame 不一致・候補空・非有限座標で enabled を残さない。
        // 定数 TitleBackgroundCharaSelectAnchorFrame.World の値は "world"。
        if (TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled
            && (TitleBackgroundCharaSelectAnchorTerritoryTypeId == 0
                || TitleBackgroundCharaSelectAnchorFrame != "world"
                || string.IsNullOrEmpty(NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(TitleBackgroundCharaSelectAnchorCandidateId))
                || !float.IsFinite(TitleBackgroundCharaSelectAnchorX)
                || !float.IsFinite(TitleBackgroundCharaSelectAnchorY)
                || !float.IsFinite(TitleBackgroundCharaSelectAnchorZ)))
        {
            TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = false;
            changed = true;
        }

        var normalizedViewCandidateId = NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(TitleBackgroundCharaSelectViewCandidateId);
        // 不正な保存値（候補が解決不能・座標が非有限・FOV が不正）のまま enabled=true を残すと、
        // SanitizeCoordinate が NaN を 0 に丸めた結果「原点カメラの壊れた view」が有効化され得る。
        // 元値が usable view の条件を満たさない場合は view 自体を無効化して安全側に倒す。
        if (TitleBackgroundCharaSelectViewEnabled
            && (string.IsNullOrEmpty(normalizedViewCandidateId)
                || !float.IsFinite(TitleBackgroundCharaSelectViewCameraX)
                || !float.IsFinite(TitleBackgroundCharaSelectViewCameraY)
                || !float.IsFinite(TitleBackgroundCharaSelectViewCameraZ)
                || !float.IsFinite(TitleBackgroundCharaSelectViewFocusX)
                || !float.IsFinite(TitleBackgroundCharaSelectViewFocusY)
                || !float.IsFinite(TitleBackgroundCharaSelectViewFocusZ)
                || !float.IsFinite(TitleBackgroundCharaSelectViewFovY)
                || TitleBackgroundCharaSelectViewFovY <= 0f))
        {
            TitleBackgroundCharaSelectViewEnabled = false;
            changed = true;
        }

        var normalizedViewCameraX = SanitizeCoordinate(TitleBackgroundCharaSelectViewCameraX);
        var normalizedViewCameraY = SanitizeCoordinate(TitleBackgroundCharaSelectViewCameraY);
        var normalizedViewCameraZ = SanitizeCoordinate(TitleBackgroundCharaSelectViewCameraZ);
        var normalizedViewFocusX = SanitizeCoordinate(TitleBackgroundCharaSelectViewFocusX);
        var normalizedViewFocusY = SanitizeCoordinate(TitleBackgroundCharaSelectViewFocusY);
        var normalizedViewFocusZ = SanitizeCoordinate(TitleBackgroundCharaSelectViewFocusZ);
        var normalizedViewFovY = SanitizeFovY(TitleBackgroundCharaSelectViewFovY);
        if (TitleBackgroundCharaSelectViewCandidateId != normalizedViewCandidateId
            || TitleBackgroundCharaSelectViewCameraX != normalizedViewCameraX
            || TitleBackgroundCharaSelectViewCameraY != normalizedViewCameraY
            || TitleBackgroundCharaSelectViewCameraZ != normalizedViewCameraZ
            || TitleBackgroundCharaSelectViewFocusX != normalizedViewFocusX
            || TitleBackgroundCharaSelectViewFocusY != normalizedViewFocusY
            || TitleBackgroundCharaSelectViewFocusZ != normalizedViewFocusZ
            || TitleBackgroundCharaSelectViewFovY != normalizedViewFovY)
        {
            TitleBackgroundCharaSelectViewCandidateId = normalizedViewCandidateId;
            TitleBackgroundCharaSelectViewCameraX = normalizedViewCameraX;
            TitleBackgroundCharaSelectViewCameraY = normalizedViewCameraY;
            TitleBackgroundCharaSelectViewCameraZ = normalizedViewCameraZ;
            TitleBackgroundCharaSelectViewFocusX = normalizedViewFocusX;
            TitleBackgroundCharaSelectViewFocusY = normalizedViewFocusY;
            TitleBackgroundCharaSelectViewFocusZ = normalizedViewFocusZ;
            TitleBackgroundCharaSelectViewFovY = normalizedViewFovY;
            changed = true;
        }

        // 保存 view の pose（DirH/DirV/Distance）。PoseCaptured=true なのに値が非有限、または
        // Distance が 0 以下の場合は pose だけを無効化する（fail-closed）。view 本体（camera/focus 形式）は
        // pose とは独立して従来どおり使えるため、view enabled はここでは落とさない。
        if (TitleBackgroundCharaSelectViewPoseCaptured
            && (!float.IsFinite(TitleBackgroundCharaSelectViewDirH)
                || !float.IsFinite(TitleBackgroundCharaSelectViewDirV)
                || !float.IsFinite(TitleBackgroundCharaSelectViewDistance)
                || TitleBackgroundCharaSelectViewDistance <= 0f))
        {
            TitleBackgroundCharaSelectViewPoseCaptured = false;
            changed = true;
        }

        var normalizedViewDirH = SanitizeCoordinate(TitleBackgroundCharaSelectViewDirH);
        var normalizedViewDirV = SanitizeCoordinate(TitleBackgroundCharaSelectViewDirV);
        var normalizedViewDistance = SanitizeCoordinate(TitleBackgroundCharaSelectViewDistance);
        if (TitleBackgroundCharaSelectViewDirH != normalizedViewDirH
            || TitleBackgroundCharaSelectViewDirV != normalizedViewDirV
            || TitleBackgroundCharaSelectViewDistance != normalizedViewDistance)
        {
            TitleBackgroundCharaSelectViewDirH = normalizedViewDirH;
            TitleBackgroundCharaSelectViewDirV = normalizedViewDirV;
            TitleBackgroundCharaSelectViewDistance = normalizedViewDistance;
            changed = true;
        }

        var normalizedFacingCalibrationCandidateId =
            NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(TitleBackgroundCharaSelectFacingCalibrationCandidateId);
        if (TitleBackgroundCharaSelectFacingCalibrationCaptured
            && (string.IsNullOrEmpty(normalizedFacingCalibrationCandidateId)
                || !float.IsFinite(TitleBackgroundCharaSelectFacingCalibrationOffset)))
        {
            TitleBackgroundCharaSelectFacingCalibrationCaptured = false;
            changed = true;
        }

        var normalizedFacingCalibrationOffset = float.IsFinite(TitleBackgroundCharaSelectFacingCalibrationOffset)
            ? TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(TitleBackgroundCharaSelectFacingCalibrationOffset)
            : TitleBackgroundCharaSelectCharacterFacing.DefaultCalibrationOffset;
        if (TitleBackgroundCharaSelectFacingCalibrationCandidateId != normalizedFacingCalibrationCandidateId
            || TitleBackgroundCharaSelectFacingCalibrationOffset != normalizedFacingCalibrationOffset)
        {
            TitleBackgroundCharaSelectFacingCalibrationCandidateId = normalizedFacingCalibrationCandidateId;
            TitleBackgroundCharaSelectFacingCalibrationOffset = normalizedFacingCalibrationOffset;
            changed = true;
        }

        // 前方自動移行は撤回した（PR #7 根本修正 点8）。verified Il Mheg V2 を rollback baseline として
        // 維持し、TitleEdit-informed placement path は OneClick run 中だけ run-scoped に arm する。
        // ロード時に既存ユーザーの保存設定（V2Enabled）を勝手に書き換えない。

        // TitleEdit-informed placement path: 候補 id を正規化し、座標が非有限なら captured を落とす（fail-closed）。
        var normalizedPlacementCandidateId =
            NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(TitleBackgroundCharaSelectPlacementCandidateId);
        if (TitleBackgroundCharaSelectPlacementCandidateId != normalizedPlacementCandidateId)
        {
            TitleBackgroundCharaSelectPlacementCandidateId = normalizedPlacementCandidateId;
            changed = true;
        }

        // placement path の候補 id は「選択中の curated 背景」へ恒久追従させる（run-scoped フリップではない）。
        // これは owner フラグ（V2Enabled 等）を書き換えないので Il Mheg V2 baseline は非退行。
        // 追従で候補が変わったら採取済み座標は無効化（点4: candidate 変更時 PositionCaptured=false）。
        if (TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId(
                TitleBackgroundCharacterSelectOverrideCandidateId))
        {
            var syncedId = NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(
                TitleBackgroundCharacterSelectOverrideCandidateId);
            if (!string.Equals(
                    TitleBackgroundCharaSelectPlacementCandidateId,
                    syncedId,
                    StringComparison.Ordinal))
            {
                TitleBackgroundCharaSelectPlacementCandidateId = syncedId;
                TitleBackgroundCharaSelectPlacementPositionCaptured = false;
                TitleBackgroundCharaSelectPlacementPositionX = 0f;
                TitleBackgroundCharaSelectPlacementPositionY = 0f;
                TitleBackgroundCharaSelectPlacementPositionZ = 0f;
                TitleBackgroundCharaSelectPlacementRotation = 0f;
                changed = true;
            }
        }

        // captured 座標は「curated 候補向けに採取された source-backed 値」のときだけ有効（点4）。
        // 候補が空 / 非 curated なら captured を落とす（fail-closed）。
        if (TitleBackgroundCharaSelectPlacementPositionCaptured
            && (string.IsNullOrEmpty(TitleBackgroundCharaSelectPlacementCandidateId)
                || !TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId(
                    TitleBackgroundCharaSelectPlacementCandidateId)
                || !float.IsFinite(TitleBackgroundCharaSelectPlacementPositionX)
                || !float.IsFinite(TitleBackgroundCharaSelectPlacementPositionY)
                || !float.IsFinite(TitleBackgroundCharaSelectPlacementPositionZ)
                || !float.IsFinite(TitleBackgroundCharaSelectPlacementRotation)))
        {
            TitleBackgroundCharaSelectPlacementPositionCaptured = false;
            changed = true;
        }

        var normalizedPlacementPositionX = SanitizeCoordinate(TitleBackgroundCharaSelectPlacementPositionX);
        var normalizedPlacementPositionY = SanitizeCoordinate(TitleBackgroundCharaSelectPlacementPositionY);
        var normalizedPlacementPositionZ = SanitizeCoordinate(TitleBackgroundCharaSelectPlacementPositionZ);
        var normalizedPlacementRotation = SanitizeCoordinate(TitleBackgroundCharaSelectPlacementRotation);
        if (TitleBackgroundCharaSelectPlacementPositionX != normalizedPlacementPositionX
            || TitleBackgroundCharaSelectPlacementPositionY != normalizedPlacementPositionY
            || TitleBackgroundCharaSelectPlacementPositionZ != normalizedPlacementPositionZ
            || TitleBackgroundCharaSelectPlacementRotation != normalizedPlacementRotation)
        {
            TitleBackgroundCharaSelectPlacementPositionX = normalizedPlacementPositionX;
            TitleBackgroundCharaSelectPlacementPositionY = normalizedPlacementPositionY;
            TitleBackgroundCharaSelectPlacementPositionZ = normalizedPlacementPositionZ;
            TitleBackgroundCharaSelectPlacementRotation = normalizedPlacementRotation;
            changed = true;
        }

        var normalizedTitleBgmPath = NormalizeAssetPath(TitleBackgroundBgmPath);
        if (TitleBackgroundBgmPath != normalizedTitleBgmPath)
        {
            TitleBackgroundBgmPath = normalizedTitleBgmPath;
            changed = true;
        }

        changed |= NormalizeSignatureProperty(TitleBackgroundCreateSceneSignature, TitleBackgroundKnownSignatures.CreateScene, value => TitleBackgroundCreateSceneSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundFixOnSignature, TitleBackgroundKnownSignatures.FixOn, value => TitleBackgroundFixOnSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundLobbyUpdateSignature, TitleBackgroundKnownSignatures.LobbyUpdate, value => TitleBackgroundLobbyUpdateSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundLoadLobbySceneSignature, TitleBackgroundKnownSignatures.LoadLobbyScene, value => TitleBackgroundLoadLobbySceneSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundLobbyCurrentMapSignature, TitleBackgroundKnownSignatures.LobbyCurrentMap, value => TitleBackgroundLobbyCurrentMapSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundCalculateLobbyCameraLookAtYSignature, TitleBackgroundKnownSignatures.CalculateLobbyCameraLookAtY, value => TitleBackgroundCalculateLobbyCameraLookAtYSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundSetCameraCurveMidPointSignature, TitleBackgroundKnownSignatures.SetCameraCurveMidPoint, value => TitleBackgroundSetCameraCurveMidPointSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundCalculateCameraCurveLowAndHighPointSignature, TitleBackgroundKnownSignatures.CalculateCameraCurveLowAndHighPoint, value => TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = value);
        changed |= TitleBackgroundPresetApplicator.ClearInvalidSelectedPreset(this);

        return changed;
    }
}
