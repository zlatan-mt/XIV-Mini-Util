// Path: tools/CharaSelectLogicTests/Tests/ConfigurationTests.cs
// Description: Registers regression tests for the Configuration responsibility
// Reason: Keeps the former monolithic runner maintainable
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;
using System.Text;
using System.Text.Json;
using XivMiniUtil;
using XivMiniUtil.Services.CharaSelect;
using XivMiniUtil.Services.Market;
using XivMiniUtil.Services.Shop;
using XivMiniUtil.Services.TitleBackground;

internal static partial class TestRunner
{
    private static void AddConfigurationTests(List<LogicTestCase> tests)
    {
        void Test(int order, string name, Func<bool> assertion) =>
            tests.Add(new LogicTestCase(order, name, assertion));

Test(3, "configuration title background properties remain top-level json", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundOverrideEnabled = true,
        TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.CharaSelectOnly,
        TitleBackgroundTerritoryPath = "bg/ffxiv/fst_f1/twn/f1t1/level/f1t1",
        TitleBackgroundCameraX = 12.5f,
        TitleBackgroundCharacterPlacementExperimentalApplyMode = TitleBackgroundCharacterPlacementExperimentalApplyMode.ActorPlacementPreviewOnly,
        TitleBackgroundCharacterSelectOverrideCandidateId = "custom:n4f4",
        CharaSelectSceneCompositionEnabled = true,
        CharaSelectSceneProfileId = "custom:n4f4",
        CharaSelectOverridePositionX = 4.25f,
        ShopSearchEchoEnabled = false,
        DesynthMinLevel = 42,
        ChecklistFeatureEnabled = false,
        DutyReadySoundDurationSeconds = 12,
    };

    var json = JsonSerializer.Serialize(configuration);
    var restored = JsonSerializer.Deserialize<Configuration>(json);

    return json.Contains("\"TitleBackgroundOverrideEnabled\"", StringComparison.Ordinal)
        && json.Contains("\"TitleBackgroundRuntimeMode\"", StringComparison.Ordinal)
        && json.Contains("\"TitleBackgroundPhase2MExperimentalApplyMode\"", StringComparison.Ordinal)
        && !json.Contains("\"TitleBackgroundCharacterPlacementExperimentalApplyMode\"", StringComparison.Ordinal)
        && json.Contains("\"CharaSelectSceneCompositionEnabled\"", StringComparison.Ordinal)
        && json.Contains("\"CharaSelectSceneProfileId\"", StringComparison.Ordinal)
        && json.Contains("\"ShopSearchEchoEnabled\"", StringComparison.Ordinal)
        && json.Contains("\"ChecklistFeatureEnabled\"", StringComparison.Ordinal)
        && !json.Contains("TitleBackgroundConfig", StringComparison.Ordinal)
        && !json.Contains("CharaSelectConfig", StringComparison.Ordinal)
        && !json.Contains("ShopConfig", StringComparison.Ordinal)
        && !json.Contains("ChecklistConfig", StringComparison.Ordinal)
        && restored != null
        && restored.TitleBackgroundOverrideEnabled
        && restored.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
        && restored.TitleBackgroundTerritoryPath == configuration.TitleBackgroundTerritoryPath
        && Math.Abs(restored.TitleBackgroundCameraX - configuration.TitleBackgroundCameraX) < 0.001f
        && restored.TitleBackgroundCharacterPlacementExperimentalApplyMode == configuration.TitleBackgroundCharacterPlacementExperimentalApplyMode
        && restored.TitleBackgroundCharacterSelectOverrideCandidateId == configuration.TitleBackgroundCharacterSelectOverrideCandidateId
        && restored.CharaSelectSceneCompositionEnabled
        && restored.CharaSelectSceneProfileId == configuration.CharaSelectSceneProfileId
        && Math.Abs(restored.CharaSelectOverridePositionX - configuration.CharaSelectOverridePositionX) < 0.001f
        && !restored.ShopSearchEchoEnabled
        && restored.DesynthMinLevel == configuration.DesynthMinLevel
        && !restored.ChecklistFeatureEnabled
        && restored.DutyReadySoundDurationSeconds == configuration.DutyReadySoundDurationSeconds;
});

Test(4, "configuration reads legacy title background character placement json key", () =>
{
    const string json = """
        {
          "Version": 7,
          "TitleBackgroundPhase2MExperimentalApplyMode": 3
        }
        """;

    var restored = JsonSerializer.Deserialize<Configuration>(json);

    return restored != null
        && restored.TitleBackgroundCharacterPlacementExperimentalApplyMode == TitleBackgroundCharacterPlacementExperimentalApplyMode.ActorPlacementPreviewOnly;
});

Test(5, "configuration reads legacy title background character placement json key via newtonsoft", () =>
{
    // Dalamud の SavePluginConfig / GetPluginConfig は Newtonsoft.Json を使うため、
    // System.Text.Json とは別にこの経路でも旧キーが読めることを保証する
    const string json = """
        {
          "Version": 7,
          "TitleBackgroundPhase2MExperimentalApplyMode": 3
        }
        """;

    var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(json);
    var serialized = restored == null ? string.Empty : Newtonsoft.Json.JsonConvert.SerializeObject(restored);

    return restored != null
        && restored.TitleBackgroundCharacterPlacementExperimentalApplyMode == TitleBackgroundCharacterPlacementExperimentalApplyMode.ActorPlacementPreviewOnly
        && serialized.Contains("\"TitleBackgroundPhase2MExperimentalApplyMode\"", StringComparison.Ordinal);
});

Test(6, "configuration ignores removed chara select experimental stage flags", () =>
{
    const string json = """
        {
          "Version": 7,
          "CharaSelectSceneCompositionEnabled": true,
          "CharaSelectSceneStageStrategyExperimentalEnabled": true,
          "CharaSelectSceneStageStrategyOneShotProbeEnabled": true,
          "CharaSelectSceneStageStrategyLastResult": " observed ",
          "CharaSelectSceneStageStrategyLastReason": " manual "
        }
        """;

    var restored = JsonSerializer.Deserialize<Configuration>(json);
    var serialized = JsonSerializer.Serialize(restored);

    return restored != null
        && restored.CharaSelectSceneCompositionEnabled
        && restored.CharaSelectSceneStageStrategyLastResult == " observed "
        && restored.CharaSelectSceneStageStrategyLastReason == " manual "
        && !serialized.Contains("CharaSelectSceneStageStrategyExperimentalEnabled", StringComparison.Ordinal)
        && !serialized.Contains("CharaSelectSceneStageStrategyOneShotProbeEnabled", StringComparison.Ordinal);
});

Test(7, "configuration apply from preserves top-level feature settings and clamps unsafe values", () =>
{
    var target = new Configuration();
    var source = new Configuration
    {
        Version = -1,
        ShopSearchEchoEnabled = false,
        ShopSearchWindowEnabled = false,
        ShopSearchAutoTeleportEnabled = true,
        ShopSearchAreaPriority = [132, 128],
        ShopDataVerboseLogging = true,
        UniversalisShowTopThreeListings = true,
        UniversalisSearchRegionWide = true,
        DesynthMinLevel = 2000,
        DesynthMaxLevel = -5,
        DesynthWarningThreshold = 0,
        DesynthTargetCount = 1200,
        NotificationRateLimitRetryMax = 99,
        DutyReadySoundDurationSeconds = 99,
        CharaSelectOverridePositionX = float.PositiveInfinity,
        CharaSelectSceneProfileId = " missing-profile ",
        // 旧バージョン由来の空文字列signature（trim後も空白のみ）を模擬。
        // 通常経路（ReloadNativeIntegration, useKnownSignaturesForMissing=false）でも
        // resolverが「not-configured」で失敗しないよう、既知既定値へ補完される契約を固定する。
        TitleBackgroundCreateSceneSignature = "   ",
        TitleBackgroundFixOnSignature = string.Empty,
        TitleBackgroundLobbyUpdateSignature = "   ",
        TitleBackgroundLoadLobbySceneSignature = string.Empty,
        TitleBackgroundLobbyCurrentMapSignature = "   ",
        TitleBackgroundCalculateLobbyCameraLookAtYSignature = string.Empty,
        // 非空のカスタム値はtrimのみで既知既定値へ上書きされないことも同じ契約内で確認する。
        TitleBackgroundSetCameraCurveMidPointSignature = " custom-set-camera-curve-mid-point-sig ",
        TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = string.Empty,
    };

    target.ApplyFrom(source);

    return target.Version == Configuration.CurrentVersion
        && !target.ShopSearchEchoEnabled
        && !target.ShopSearchWindowEnabled
        && target.ShopSearchAutoTeleportEnabled
        && target.ShopSearchAreaPriority.SequenceEqual(new uint[] { 132, 128 })
        && target.ShopDataVerboseLogging
        && target.UniversalisShowTopThreeListings
        && target.UniversalisSearchRegionWide
        && target.DesynthMinLevel == 1
        && target.DesynthMaxLevel == 999
        && target.DesynthWarningThreshold == 1
        && target.DesynthTargetCount == 999
        && target.NotificationRateLimitRetryMax == 10
        && target.DutyReadySoundDurationSeconds == 30
        && target.CharaSelectOverridePositionX == 0f
        && target.CharaSelectSceneProfileId == "missing-profile"
        && target.TitleBackgroundCreateSceneSignature == TitleBackgroundKnownSignatures.CreateScene
        && target.TitleBackgroundFixOnSignature == TitleBackgroundKnownSignatures.FixOn
        && target.TitleBackgroundLobbyUpdateSignature == TitleBackgroundKnownSignatures.LobbyUpdate
        && target.TitleBackgroundLoadLobbySceneSignature == TitleBackgroundKnownSignatures.LoadLobbyScene
        && target.TitleBackgroundLobbyCurrentMapSignature == TitleBackgroundKnownSignatures.LobbyCurrentMap
        && target.TitleBackgroundCalculateLobbyCameraLookAtYSignature == TitleBackgroundKnownSignatures.CalculateLobbyCameraLookAtY
        && target.TitleBackgroundSetCameraCurveMidPointSignature == "custom-set-camera-curve-mid-point-sig"
        && target.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature == TitleBackgroundKnownSignatures.CalculateCameraCurveLowAndHighPoint;
});

Test(148, "title background preset applicator keeps configuration on invalid preset", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "old",
        TitleBackgroundTerritoryPath = "ffxiv/old/region/level/old",
        TitleBackgroundCameraX = 99f,
    };
    var invalidPreset = new TitleBackgroundPreset
    {
        TerritoryPath = "ffxiv/area/region/sample",
        CameraX = 4f,
    };

    return !TitleBackgroundPresetApplicator.TryApplyPreset(
            configuration,
            invalidPreset,
            "bad",
            _ => true,
            out var errorMessage)
        && !string.IsNullOrWhiteSpace(errorMessage)
        && configuration.TitleBackgroundSelectedPresetId == "old"
        && configuration.TitleBackgroundTerritoryPath == "ffxiv/old/region/level/old"
        && configuration.TitleBackgroundCameraX == 99f;
});

Test(149, "title background preset applicator keeps configuration when lvb is missing", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "old",
        TitleBackgroundTerritoryPath = "ffxiv/old/region/level/old",
        TitleBackgroundCameraX = 99f,
    };
    var preset = new TitleBackgroundPreset
    {
        TerritoryPath = "ffxiv/area/region/level/sample",
        CameraX = 4f,
    };

    return !TitleBackgroundPresetApplicator.TryApplyPreset(
            configuration,
            preset,
            "missing-lvb",
            _ => false,
            out var errorMessage)
        && errorMessage.Contains("LVB", StringComparison.Ordinal)
        && configuration.TitleBackgroundSelectedPresetId == "old"
        && configuration.TitleBackgroundTerritoryPath == "ffxiv/old/region/level/old"
        && configuration.TitleBackgroundCameraX == 99f;
});

Test(344, "configuration chara select anchor fields persist as top-level json", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundCharaSelectAnchorEnabled = true,
        TitleBackgroundCharaSelectAnchorCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectAnchorX = 5.5f,
        TitleBackgroundCharaSelectAnchorY = 2.25f,
        TitleBackgroundCharaSelectAnchorZ = -7.75f,
        TitleBackgroundCharaSelectAnchorRotation = 1.5f,
    };

    var json = JsonSerializer.Serialize(configuration);
    var restored = JsonSerializer.Deserialize<Configuration>(json);

    return json.Contains("\"TitleBackgroundCharaSelectAnchorEnabled\"", StringComparison.Ordinal)
        && json.Contains("\"TitleBackgroundCharaSelectAnchorCandidateId\"", StringComparison.Ordinal)
        && restored!.TitleBackgroundCharaSelectAnchorEnabled
        && restored.TitleBackgroundCharaSelectAnchorCandidateId == "custom:n4f4"
        && restored.TitleBackgroundCharaSelectAnchorX == 5.5f
        && restored.TitleBackgroundCharaSelectAnchorY == 2.25f
        && restored.TitleBackgroundCharaSelectAnchorZ == -7.75f;
});

Test(359, "configuration chara select view fields persist as top-level json", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundCharaSelectViewEnabled = true,
        TitleBackgroundCharaSelectViewCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectViewCameraX = 1.5f,
        TitleBackgroundCharaSelectViewFocusZ = -2.5f,
        TitleBackgroundCharaSelectViewFovY = 0.9f,
    };
    var json = JsonSerializer.Serialize(configuration);
    var restored = JsonSerializer.Deserialize<Configuration>(json);
    return json.Contains("\"TitleBackgroundCharaSelectViewEnabled\"", StringComparison.Ordinal)
        && restored!.TitleBackgroundCharaSelectViewEnabled
        && restored.TitleBackgroundCharaSelectViewCandidateId == "custom:n4f4"
        && Math.Abs(restored.TitleBackgroundCharaSelectViewCameraX - 1.5f) < 0.0001f
        && Math.Abs(restored.TitleBackgroundCharaSelectViewFocusZ - (-2.5f)) < 0.0001f;
});

Test(366, "configuration anchor frame tag persists as top-level json", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundCharaSelectAnchorFrame = "world",
    };

    var json = JsonSerializer.Serialize(configuration);
    var restored = JsonSerializer.Deserialize<Configuration>(json);

    return json.Contains("\"TitleBackgroundCharaSelectAnchorFrame\"", StringComparison.Ordinal)
        && restored!.TitleBackgroundCharaSelectAnchorFrame == "world";
});

Test(378, "configuration persists world experimental fields as top-level json", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundCharaSelectAnchorTerritoryTypeId = 816,
        TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = true,
    };

    var json = JsonSerializer.Serialize(configuration);
    var restored = JsonSerializer.Deserialize<Configuration>(json);

    return json.Contains("\"TitleBackgroundCharaSelectAnchorTerritoryTypeId\"", StringComparison.Ordinal)
        && json.Contains("\"TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled\"", StringComparison.Ordinal)
        && restored!.TitleBackgroundCharaSelectAnchorTerritoryTypeId == 816
        && restored.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled;
});

Test(416, "phase 0C collection does not write configuration or call Save", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var add = ExtractMethodBody(timelineText, "public bool TryAddWorldCoordinateSampleFromRun(string runId, string completedAt)");
    var persist = ExtractMethodBody(timelineText, "private void PersistWorldCoordinateCorrespondenceReport()");
    var clear = ExtractMethodBody(timelineText, "public void ClearWorldCoordinateSamples()");
    return add.Length > 0
        && !add.Contains(".Save(", StringComparison.Ordinal)
        && !add.Contains("_configuration.TitleBackground", StringComparison.Ordinal)
        && !persist.Contains(".Save(", StringComparison.Ordinal)
        && !clear.Contains(".Save(", StringComparison.Ordinal);
});

Test(423, "configuration fixOn focus anchor override flag persists as top-level json", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundFixOnFocusAnchorOverrideEnabled = true,
    };

    var json = JsonSerializer.Serialize(configuration);
    var restored = JsonSerializer.Deserialize<Configuration>(json);

    return json.Contains("\"TitleBackgroundFixOnFocusAnchorOverrideEnabled\"", StringComparison.Ordinal)
        && restored!.TitleBackgroundFixOnFocusAnchorOverrideEnabled;
});

Test(433, "configuration fixOn passive observation flag persists as top-level json", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundFixOnPassiveObservationEnabled = true,
    };

    var json = JsonSerializer.Serialize(configuration);
    var restored = JsonSerializer.Deserialize<Configuration>(json);

    return json.Contains("\"TitleBackgroundFixOnPassiveObservationEnabled\"", StringComparison.Ordinal)
        && restored!.TitleBackgroundFixOnPassiveObservationEnabled;
});

Test(439, "configuration environment noon override flag defaults true and persists as top-level json", () =>
{
    var defaultConfiguration = new Configuration();
    var configuration = new Configuration
    {
        TitleBackgroundEnvironmentNoonEnabled = false,
    };

    var json = JsonSerializer.Serialize(configuration);
    var restored = JsonSerializer.Deserialize<Configuration>(json);

    return defaultConfiguration.TitleBackgroundEnvironmentNoonEnabled
        && json.Contains("\"TitleBackgroundEnvironmentNoonEnabled\"", StringComparison.Ordinal)
        && restored != null
        && !restored.TitleBackgroundEnvironmentNoonEnabled;
});

    }
}
