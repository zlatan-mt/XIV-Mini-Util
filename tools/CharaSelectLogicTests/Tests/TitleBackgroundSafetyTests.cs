// Path: tools/CharaSelectLogicTests/Tests/TitleBackgroundSafetyTests.cs
// Description: Registers regression tests for the TitleBackgroundSafety responsibility
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
    private static void AddTitleBackgroundSafetyTests(List<LogicTestCase> tests)
    {
        void Test(int order, string name, Func<bool> assertion) =>
            tests.Add(new LogicTestCase(order, name, assertion));

Test(16, "chara select scene profile maps to override territory config", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        CharaSelectSceneUseProfileTerritory = true,
        CharaSelectSceneStageStrategy = CharaSelectStageStrategy.ClientSelectDataTerritoryPatch,
        CharaSelectSceneProfileId = "scene:old-sharlayan-k5t1",
        TitleBackgroundOverrideEnabled = true,
    };

    CharaSelectSceneCompositionPlanner.ApplyProfileToConfiguration(
        configuration,
        CharaSelectSceneProfileRegistry.GetDefault());

    return configuration.CharaSelectOverrideTerritoryEnabled
        && configuration.CharaSelectOverrideTerritoryTypeId == 962
        && !configuration.TitleBackgroundOverrideEnabled;
});

Test(17, "chara select scene final mode disables title background route", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = false,
        TitleBackgroundOverrideEnabled = true,
    };

    CharaSelectSceneCompositionPlanner.SetFinalCompositionEnabled(configuration, true);

    return configuration.CharaSelectSceneCompositionEnabled
        && !configuration.TitleBackgroundOverrideEnabled;
});

Test(18, "title background route disables final scene composition mode", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        TitleBackgroundOverrideEnabled = false,
    };

    CharaSelectSceneCompositionPlanner.SetTitleBackgroundRouteEnabled(configuration, true);

    return configuration.TitleBackgroundOverrideEnabled
        && !configuration.CharaSelectSceneCompositionEnabled;
});

Test(22, "chara select scene diagnostic uses foreground preserving route", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        CharaSelectSceneUseProfileTerritory = true,
        CharaSelectSceneStageStrategy = CharaSelectStageStrategy.ClientSelectDataTerritoryPatch,
        CharaSelectOverrideTerritoryEnabled = true,
        CharaSelectOverrideTerritoryTypeId = 962,
        CharaSelectSceneUseSavedEmote = true,
        CharaSelectEmoteEnabled = true,
    };

    var diagnostic = CharaSelectSceneCompositionPlanner.BuildDiagnostic(configuration, "Test Emote", "Unknown");
    var lines = CharaSelectSceneCompositionPlanner.BuildDiagnosticLines(diagnostic);

    return diagnostic.Route == "foreground-preserving"
        && diagnostic.ExpectedCharacterVisible
        && diagnostic.TerritoryOverrideEnabled
        && diagnostic.TerritoryOverrideTerritoryTypeId == 962
        && diagnostic.EmoteEnabled
        && diagnostic.NextAction == "verify-character-visible-background-and-emote-with-screenshot"
        && diagnostic.VisualLocation.ExpectedTerritoryTypeId == 962
        && lines.Contains("charaSelectScene.profileId=scene:old-sharlayan-k5t1")
        && lines.Contains("charaSelectStageProbe.routeVerdict=source-not-resolved");
});

Test(25, "chara select visual location reports territory patch unchanged", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        CharaSelectSceneUseProfileTerritory = true,
        CharaSelectSceneStageStrategy = CharaSelectStageStrategy.ClientSelectDataTerritoryPatch,
        CharaSelectOverrideTerritoryEnabled = true,
        CharaSelectOverrideTerritoryTypeId = 962,
        LastSceneProfileCharacterVisibleResult = CharaSelectSceneBinaryResult.Yes,
        LastSceneProfileLocationChangedResult = CharaSelectSceneBinaryResult.No,
    };
    var observation = new CharaSelectSceneLastObservation(
        true,
        true,
        123,
        0,
        "scene:old-sharlayan-k5t1",
        "Old Sharlayan outdoor test",
        true,
        true,
        true,
        false,
        "2026-05-31T00:00:00.0000000+00:00",
        CharaSelectStageProbeSnapshot.Empty with
        {
            Available = true,
            CharacterPointerResolved = true,
            ClientSelectDataPatchAttempted = true,
            ClientSelectDataPatchApplied = true,
            ClientSelectDataRestoreApplied = true,
        });

    var diagnostic = CharaSelectSceneCompositionPlanner.BuildDiagnostic(configuration, "Test Emote", "True", observation);
    var lines = CharaSelectSceneCompositionPlanner.BuildDiagnosticLines(diagnostic);

    return diagnostic.LastObservationCharacterPointerResolved
        && diagnostic.VisualLocation.ManualResult == "Unchanged"
        && diagnostic.VisualLocation.RouteVerdict == "territory-patch-did-not-change-visible-stage"
        && diagnostic.VisualLocation.NextAction == "discover-visible-stage-source"
        && diagnostic.NextAction == "discover-visible-stage-source"
        && lines.Contains("charaSelectScene.visualLocation.routeVerdict=territory-patch-did-not-change-visible-stage");
});

Test(27, "chara select stage probe stores primitive diagnostics only", () =>
{
    var snapshot = CharaSelectStageProbeSnapshot.Empty with
    {
        Available = true,
        Reason = "read-only-observation",
        ContentId = 123,
        CharacterPointerResolved = true,
        ClientSelectDataOriginalTerritoryType = 1,
        ClientSelectDataPatchedTerritoryType = 962,
        ClientSelectDataPatchAttempted = true,
        ClientSelectDataPatchApplied = true,
        ClientSelectDataRestoreApplied = true,
    };
    var text = snapshot.ToString();

    return snapshot.Available
        && snapshot.ContentId == 123
        && !text.Contains("Character*", StringComparison.Ordinal)
        && !text.Contains("0x", StringComparison.Ordinal);
});

Test(29, "legacy title background camera framing controls are removed", () =>
{
    var root = FindRepositoryRoot();
    var settings = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components"), "SettingsTab*.cs").Select(File.ReadAllText));
    return !settings.Contains("カメラ構図（湖中心は変わりません）", StringComparison.Ordinal)
        && !settings.Contains("DrawTitleBackgroundCameraFramingControls", StringComparison.Ordinal);
});

Test(30, "title background set enabled auto-enables camera override", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    // Verify auto-enable logic exists in SetEnabled
    return serviceText.Contains("TitleBackgroundCameraOverrideEnabled = true", StringComparison.Ordinal)
        && serviceText.Contains("Auto-enable so the adapter can arm correctly", StringComparison.Ordinal);
});

Test(31, "title background integrated composition on does not require legacy composition", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        titleBackgroundOverrideEnabled: true,
        titleBackgroundCameraOverrideEnabled: true,
        legacySceneCompositionEnabled: false,
        integratedCompositionEnabled: true,
        shouldArmAdapter: true,
        overrideAppliedCount: 1,
        backgroundApplied: true,
        backgroundObserved: true));
    return result.Level is TitleBackgroundQuickCheckLevel.OK
        or TitleBackgroundQuickCheckLevel.WARN;
});

Test(32, "title background integrated composition off blocks with specific ng reason", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        titleBackgroundOverrideEnabled: true,
        titleBackgroundCameraOverrideEnabled: true,
        integratedCompositionEnabled: false,
        shouldArmAdapter: true,
        overrideAppliedCount: 0,
        backgroundApplied: false,
        backgroundObserved: false));
    return result.Level == TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("integrated character composition is disabled", StringComparison.Ordinal);
});

Test(33, "title background should arm adapter false surfaces blocking reason", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        titleBackgroundOverrideEnabled: true,
        titleBackgroundCameraOverrideEnabled: true,
        shouldArmAdapter: false,
        shouldArmAdapterReason: "runtimeModeNotCharaSelectOnly",
        overrideAppliedCount: 0,
        backgroundApplied: false,
        backgroundObserved: false));
    return result.Level == TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("adapter was not armed", StringComparison.Ordinal)
        && result.Reason.Contains("runtimeModeNotCharaSelectOnly", StringComparison.Ordinal);
});

Test(34, "title background legacy composition off alone is not ng", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        legacySceneCompositionEnabled: false,
        overrideAppliedCount: 1,
        backgroundApplied: true,
        backgroundObserved: true));
    return result.Level != TitleBackgroundQuickCheckLevel.NG;
});

Test(35, "title background override disabled surfaces specific ng reason", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        titleBackgroundOverrideEnabled: false,
        overrideAppliedCount: 0,
        backgroundApplied: false,
        backgroundObserved: false));
    return result.Level == TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("Character Select Background is disabled", StringComparison.Ordinal);
});

Test(36, "title background camera override disabled surfaces specific ng reason", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        titleBackgroundOverrideEnabled: true,
        titleBackgroundCameraOverrideEnabled: false,
        overrideAppliedCount: 0,
        backgroundApplied: false,
        backgroundObserved: false));
    return result.Level == TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("Title Background camera override is disabled", StringComparison.Ordinal);
});

Test(38, "title background bridge case 1 legacy shooting composition on path is discoverable", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        CharaSelectSceneUseProfileTerritory = true,
        CharaSelectSceneStageStrategy = CharaSelectStageStrategy.ClientSelectDataTerritoryPatch,
    };
    var root = FindRepositoryRoot();
    var serviceText = string.Join(Environment.NewLine, Directory
        .EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "CharaSelect"), "CharaSelectService*.cs")
        .Select(File.ReadAllText));
    return CharaSelectSceneCompositionPlanner.ResolveCompositionCaller(configuration) == "legacy-shooting-composition"
        && CharaSelectSceneCompositionPlanner.UsesClientSelectDataTerritoryPatch(configuration)
        && serviceText.Contains("TryPatchOverrideDisplayData", StringComparison.Ordinal)
        && serviceText.Contains("UpdateCharaSelectDisplayDetour", StringComparison.Ordinal);
});

Test(39, "title background bridge case 2 required for title background n4f4", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundOverrideEnabled = true,
        TitleBackgroundIntegratedCompositionEnabled = true,
        TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.CharaSelectOnly,
        TitleBackgroundCharacterSelectOverrideCandidateId = "custom:n4f4",
        CharaSelectSceneCompositionEnabled = false,
    };
    return CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeRequired(configuration);
});

Test(40, "title background bridge case 3 invoked and character applied is ok", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        legacySceneCompositionEnabled: false,
        overrideAppliedCount: 1,
        backgroundApplied: true,
        backgroundObserved: true,
        cameraFramingApplied: true,
        sceneOverrideApplyObserved: true,
        characterVisualStatus: TitleBackgroundCharacterVisualStatus.Visible,
        cameraProfileId: "n4f4-visible",
        cameraProfileSource: "candidate",
        cameraFramesCharacter: "True",
        cameraFinalYawPitchDistanceMatchesProfile: "True",
        cameraVisibleProfileApplied: true,
        bridgeCharacterCompositionApplied: true,
        bridgeCameraProfileApplied: true,
        characterCompositionBridge: new TitleBackgroundCharacterCompositionBridgeSnapshot(
            true,
            true,
            true,
            "TitleBackgroundCharacterVisibility",
            "title-background-integrated",
            true,
            true,
            true,
            true,
            true)));

    return result.Level == TitleBackgroundQuickCheckLevel.OK;
});

Test(41, "title background bridge case 4 missing warns with bridge not invoked", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        legacySceneCompositionEnabled: false,
        overrideAppliedCount: 1,
        backgroundApplied: true,
        backgroundObserved: true,
        cameraFramingApplied: true,
        sceneOverrideApplyObserved: true,
        characterCompositionBridge: new TitleBackgroundCharacterCompositionBridgeSnapshot(
            true,
            true,
            false,
            "not-run",
            "none",
            false,
            false,
            true,
            true,
            false)));

    return result.Level is TitleBackgroundQuickCheckLevel.WARN or TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("background works with warnings", StringComparison.Ordinal)
        && result.Warnings.Any(warning => warning.Contains("bridge not invoked", StringComparison.Ordinal));
});

Test(42, "title background bridge case 5 camera only is not enough", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        overrideAppliedCount: 1,
        backgroundApplied: true,
        backgroundObserved: true,
        cameraFramingApplied: true,
        sceneOverrideApplyObserved: true,
        characterCompositionBridge: new TitleBackgroundCharacterCompositionBridgeSnapshot(
            true,
            true,
            true,
            "camera-only",
            "title-background-integrated",
            false,
            false,
            true,
            true,
            false)));

    return result.Level is TitleBackgroundQuickCheckLevel.WARN or TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("camera only", StringComparison.Ordinal);
});

Test(43, "title background bridge detail lines include bridge status", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterCompositionBridge: new TitleBackgroundCharacterCompositionBridgeSnapshot(
            true,
            true,
            true,
            "TitleBackgroundCharacterVisibility",
            "title-background-integrated",
            true,
            true,
            true,
            true,
            true)));

    return result.DetailLines.Any(l => l == "quickCheck.characterCompositionBridge.enabled=True")
        && result.DetailLines.Any(l => l == "quickCheck.characterCompositionBridge.invoked=True")
        && result.DetailLines.Any(l => l == "quickCheck.characterCompositionBridge.source=title-background-integrated")
        && result.DetailLines.Any(l => l == "quickCheck.characterCompositionBridge.appliedStage=True")
        && result.DetailLines.Any(l => l == "quickCheck.characterCompositionBridge.appliedCharacter=True")
        && result.DetailLines.Any(l => l == "quickCheck.characterCompositionBridge.appliedCamera=True")
        && result.DetailLines.Any(l => l == "quickCheck.characterVisualKnownByBridge=True");
});

Test(44, "title background bridge warns when legacy shooting composition is still required", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        legacySceneCompositionEnabled: true,
        characterCompositionBridge: new TitleBackgroundCharacterCompositionBridgeSnapshot(
            true,
            true,
            true,
            "TitleBackgroundCharacterVisibility",
            "legacy-shooting-composition",
            true,
            true,
            true,
            true,
            true)));

    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Warnings.Any(warning => warning.Contains("legacy shooting composition dependency still required", StringComparison.Ordinal));
});

Test(45, "title background camera profile case 1 bridge applied but visual unknown is warn", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        cameraFramingApplied: true,
        sceneOverrideApplyObserved: true,
        characterVisualStatus: TitleBackgroundCharacterVisualStatus.Unknown,
        cameraProfileId: "n4f4-visible",
        cameraProfileSource: "candidate",
        cameraFramesCharacter: "Unknown",
        cameraFinalYawPitchDistanceMatchesProfile: "Unknown",
        cameraVisibleProfileResolved: true,
        cameraVisibleProfileApplied: true,
        bridgeCharacterCompositionApplied: true,
        bridgeCameraProfileApplied: true,
        characterCompositionBridge: new TitleBackgroundCharacterCompositionBridgeSnapshot(
            true,
            true,
            true,
            "TitleBackgroundCharacterVisibility",
            "title-background-integrated",
            true,
            true,
            true,
            true,
            true)));

    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Level != TitleBackgroundQuickCheckLevel.OK
        && result.Reason.Contains("visual", StringComparison.Ordinal)
        && result.NextAction.Contains("automatically copied report", StringComparison.Ordinal)
        && !result.NextAction.Contains("legacy shooting composition", StringComparison.Ordinal);
});

Test(46, "title background camera profile case 2 n4f4 candidate recommended requires visible profile", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        candidateRecommendedFraming: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        cameraFramingApplied: true,
        sceneOverrideApplyObserved: true,
        cameraVisibleProfileApplied: false));

    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Level != TitleBackgroundQuickCheckLevel.OK
        && result.NextAction.Contains("camera framing investigation", StringComparison.Ordinal)
        && !result.NextAction.Contains("legacy shooting composition", StringComparison.Ordinal);
});

Test(47, "title background camera profile case 3 visible profile and visual visible is ok", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        candidateRecommendedFraming: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        cameraFramingApplied: true,
        sceneOverrideApplyObserved: true,
        characterVisualStatus: TitleBackgroundCharacterVisualStatus.Visible,
        cameraProfileId: "n4f4-visible",
        cameraProfileSource: "candidate",
        cameraYaw: "1",
        cameraPitch: "0.2",
        cameraDistance: "5",
        cameraFramesCharacter: "True",
        cameraFinalYawPitchDistanceMatchesProfile: "True",
        cameraVisibleProfileResolved: true,
        cameraVisibleProfileApplied: true,
        bridgeCharacterCompositionApplied: true,
        bridgeCameraProfileApplied: true,
        characterCompositionBridge: new TitleBackgroundCharacterCompositionBridgeSnapshot(
            true,
            true,
            true,
            "TitleBackgroundCharacterVisibility",
            "title-background-integrated",
            true,
            true,
            true,
            true,
            true)));

    return result.Level == TitleBackgroundQuickCheckLevel.OK;
});

Test(48, "title background camera profile case 4 y only framing is not enough", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        cameraFramingApplied: true,
        sceneOverrideApplyObserved: true,
        cameraProfileId: "n4f4-visible",
        cameraProfileSource: "candidate",
        cameraFramesCharacter: "False",
        cameraFinalYawPitchDistanceMatchesProfile: "False",
        cameraVisibleProfileResolved: true,
        cameraVisibleProfileApplied: true,
        characterCompositionBridge: new TitleBackgroundCharacterCompositionBridgeSnapshot(
            true,
            true,
            true,
            "TitleBackgroundCharacterVisibility",
            "title-background-integrated",
            true,
            true,
            true,
            true,
            true)));

    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Reason == "camera does not frame the character"
        && result.Warnings.Any(warning => warning.Contains("camera does not frame the character", StringComparison.Ordinal));
});

Test(49, "title background captured camera case 1 profile resolved but yaw pitch distance none is warn", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        candidateRecommendedFraming: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        cameraProfileId: "n4f4-visible",
        cameraProfileSource: "candidate",
        cameraYaw: "none",
        cameraPitch: "none",
        cameraDistance: "none",
        cameraFramesCharacter: "False",
        cameraVisibleProfileResolved: true,
        cameraVisibleProfileApplied: false,
        cameraVisibleProfileAppliedState: "Partial",
        characterCompositionBridge: new TitleBackgroundCharacterCompositionBridgeSnapshot(
            true,
            true,
            true,
            "TitleBackgroundCharacterVisibility",
            "title-background-integrated",
            true,
            true,
            true,
            true,
            true)));

    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.DetailLines.Any(line => line == "camera.visibleProfileApplied=Partial")
        && result.Reason == "captured legacy visible camera profile is missing";
});

Test(50, "title background captured camera case 2 captured profile preferred", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGetPreferredCameraProfile(
            "custom:n4f4",
            TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
            true,
            1.2f,
            0.3f,
            4.5f,
            new Vector3(1f, 2f, 3f),
            new Vector3(4f, 5f, 6f),
            out var profile)
        && profile.ProfileSource == "captured"
        && profile.ProfileId == "n4f4-visible-captured"
        && profile.Yaw.HasValue
        && profile.Pitch.HasValue
        && profile.Distance.HasValue;
});

Test(51, "title background captured camera case 2b capture button stores valid legacy visible profile", () =>
{
    var result = TitleBackgroundCapturedCameraProfileLogic.Validate(new TitleBackgroundCapturedCameraProfileInput(
        true,
        TitleBackgroundCharacterVisualStatus.Visible,
        -1.6f,
        -0.2f,
        2.8f,
        new Vector3(1f, 2f, 3f),
        new Vector3(4f, 5f, 6f)));

    return result.Success
        && result.Source == TitleBackgroundCapturedCameraProfileLogic.VisibleLegacySource
        && result.Distance > 0f
        && result.DirH != 0f
        && result.DirV != 0f;
});

Test(52, "title background captured camera case 3 captured profile applied and frames character is ok", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        candidateRecommendedFraming: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        characterVisualStatus: TitleBackgroundCharacterVisualStatus.Visible,
        cameraProfileId: "n4f4-visible-captured",
        cameraProfileSource: "captured",
        cameraYaw: "1.2",
        cameraPitch: "0.3",
        cameraDistance: "4.5",
        cameraFramesCharacter: "True",
        cameraFinalYawPitchDistanceMatchesProfile: "True",
        cameraVisibleProfileResolved: true,
        cameraVisibleProfileApplied: true,
        cameraVisibleProfileAppliedState: "True",
        cameraProfileApplyRoute: "captured-profile",
        cameraCapturedProfileEnabled: true,
        bridgeCameraProfileApplied: true));

    return result.Level == TitleBackgroundQuickCheckLevel.OK
        && result.DetailLines.Any(line => line == "camera.visibleProfileApplied=True")
        && result.DetailLines.Any(line => line == "bridge.cameraProfileApplied=True");
});

Test(53, "title background captured camera case 4 fallback profile without captured data warns", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        candidateRecommendedFraming: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        cameraProfileId: "n4f4-visible",
        cameraProfileSource: "candidate",
        cameraYaw: "none",
        cameraPitch: "none",
        cameraDistance: "none",
        cameraFramesCharacter: "False",
        cameraVisibleProfileResolved: true,
        cameraVisibleProfileAppliedState: "Partial",
        characterCompositionBridge: new TitleBackgroundCharacterCompositionBridgeSnapshot(
            true,
            true,
            true,
            "TitleBackgroundCharacterVisibility",
            "title-background-integrated",
            true,
            true,
            true,
            true,
            true)));

    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.NextAction.Contains("camera framing investigation", StringComparison.Ordinal)
        && !result.NextAction.Contains("legacy shooting composition", StringComparison.Ordinal);
});

Test(54, "title background bridge applied camera is stored for diagnostics report", () =>
{
    var root = FindRepositoryRoot();
    var charaSelectText = string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(
                Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "CharaSelect"),
                "CharaSelectService*.cs")
            .Select(File.ReadAllText));
    var titleBackgroundText = string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(
                Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground"),
                "TitleScreenBackgroundService*.cs")
            .Select(File.ReadAllText));
    var quickCheckText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundQuickCheck.cs"));

    return charaSelectText.Contains("MarkTitleBackgroundCharacterCompositionBridgeCameraApplied", StringComparison.Ordinal)
        && charaSelectText.Contains("AppliedCamera = true", StringComparison.Ordinal)
        && titleBackgroundText.Contains("MarkTitleBackgroundCharacterCompositionBridgeCameraApplied", StringComparison.Ordinal)
        && titleBackgroundText.Contains("ResetTitleBackgroundCharacterCompositionBridgeSnapshot", StringComparison.Ordinal)
        && quickCheckText.Contains("quickCheck.characterCompositionBridge.appliedCamera=", StringComparison.Ordinal);
});

Test(55, "chara select scene phase3a does not introduce forbidden write paths", () =>
{
    var root = FindRepositoryRoot();
    var files = new[]
    {
        Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "CharaSelect", "CharaSelectSceneProfileRegistry.cs"),
        Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "CharaSelect", "CharaSelectSceneCompositionPlanner.cs"),
    };
    var text = string.Join("\n", files.Select(File.ReadAllText));

    return !text.Contains("SceneCamera.Position", StringComparison.Ordinal)
        && !text.Contains("SceneCamera.LookAtVector", StringComparison.Ordinal)
        && !text.Contains("SceneCamera.FoV", StringComparison.Ordinal)
        && !text.Contains("ObjectTable", StringComparison.Ordinal)
        && !text.Contains(".Position =", StringComparison.Ordinal)
        && !text.Contains(".Rotation =", StringComparison.Ordinal)
        && !text.Contains("lighting", StringComparison.OrdinalIgnoreCase)
        && !text.Contains("environment", StringComparison.OrdinalIgnoreCase);
});

Test(80, "simple title background reset clears hidden experimental settings", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(
            Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground"),
            "TitleScreenBackgroundService*.cs").Select(File.ReadAllText));
    var settingsText = string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(
            Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components"),
            "SettingsTab*.cs").Select(File.ReadAllText));
    var reset = ExtractMethodBody(
        serviceText,
        "internal bool ResetSimpleTitleBackgroundSettings()");
    var normal = ExtractMethodBody(
        settingsText,
        "private void DrawTitleBackgroundSettings()");

    return normal.Contains("ResetSimpleTitleBackgroundSettings()", StringComparison.Ordinal)
        && reset.Contains("TitleBackgroundOverrideEnabled = false", StringComparison.Ordinal)
        && reset.Contains("TitleBackgroundIntegratedCompositionEnabled = false", StringComparison.Ordinal)
        && reset.Contains("TitleBackgroundFixOnPassiveObservationEnabled = false", StringComparison.Ordinal)
        && reset.Contains("TitleBackgroundFixOnFocusAnchorOverrideEnabled = false", StringComparison.Ordinal)
        && reset.Contains("TitleBackgroundCharaSelectAnchorEnabled = false", StringComparison.Ordinal)
        && reset.Contains("ReloadNativeIntegration()", StringComparison.Ordinal);
});

Test(82, "title background automatic diagnostic is bounded and curated", () =>
{
    var selected = TitleBackgroundAutomaticCheckDiagnosticSelector.Select(
    [
        "runtimeMode=CharaSelectOnly",
        "lastOverrideApplied=True",
        "transition.verdict.loginTransitionSafety=safe",
        "characterPlace.appliedFrameCount=42",
        "characterPlace.runAppliedFrameCount=1",
        "fixOn.calls=1",
        "fixOn.exp.sceneGeneration=7",
        "phase2C.timeline.sample[0]=raw",
        "native.signature.address=0x1234",
        "transition.detailDump=details.txt",
    ]);

    return selected.Count == 6
        && selected.Contains("runtimeMode=CharaSelectOnly")
        && selected.Contains("lastOverrideApplied=True")
        && selected.Contains("transition.verdict.loginTransitionSafety=safe")
        // 自動レポートは run-scoped の配置証拠を選び、累積 appliedFrameCount は選ばない。
        && selected.Contains("characterPlace.runAppliedFrameCount=1")
        && !selected.Contains("characterPlace.appliedFrameCount=42")
        && selected.Contains("fixOn.calls=1")
        && selected.Contains("fixOn.exp.sceneGeneration=7")
        && !selected.Any(line => line.Contains("raw", StringComparison.Ordinal))
        && !selected.Any(line => line.Contains("signature", StringComparison.Ordinal))
        && !selected.Any(line => line.Contains("detailDump", StringComparison.Ordinal));
});

Test(87, "advanced title background display mode migrates to simple", () =>
{
    var target = new Configuration();
    var source = new Configuration
    {
        TitleBackgroundSettingsDisplayMode = TitleBackgroundSettingsDisplayMode.Advanced,
    };

    target.ApplyFrom(source);
    return target.TitleBackgroundSettingsDisplayMode == TitleBackgroundSettingsDisplayMode.Simple;
});

Test(89, "title background automatic partial report is explicit", () =>
{
    var report = TitleBackgroundAutomaticCheckReportBuilder.Build(
        DateTimeOffset.Now,
        ["[XMU QuickCheck] WARN"],
        ["transition.verdict.loginTransitionSafety=unknown"],
        partial: true);

    return report.Contains("completion=partial", StringComparison.Ordinal)
        && report.Contains("transition.verdict.loginTransitionSafety=unknown", StringComparison.Ordinal);
});

Test(92, "title background native character capture gate is pre-login only", () =>
{
    var allowed = TitleBackgroundCharacterSourceCaptureGate.Evaluate(
        isLoggedIn: false,
        isCharaSelectActive: true,
        activeSceneGeneration: 2,
        runtimeSceneGeneration: 2);
    var postLogin = TitleBackgroundCharacterSourceCaptureGate.Evaluate(true, true, 2, 2);
    var inactive = TitleBackgroundCharacterSourceCaptureGate.Evaluate(false, false, 2, 2);
    var stale = TitleBackgroundCharacterSourceCaptureGate.Evaluate(false, true, 1, 2);

    return allowed.Allowed && allowed.Status == "pre-login"
        && !postLogin.Allowed && postLogin.Status == "skipped-post-login"
        && !inactive.Allowed && inactive.Status == "skipped-inactive-chara-select"
        && !stale.Allowed && stale.Status == "skipped-scene-generation-mismatch";
});

Test(93, "title background native character source evaluation is decisive", () =>
{
    var zero = TitleBackgroundCharacterSourceEvaluation.Evaluate(
        [NativeCharacterSnapshot(0, new nint(0x1000), Vector3.Zero)]);
    var stable = TitleBackgroundCharacterSourceEvaluation.Evaluate(
        [
            NativeCharacterSnapshot(0, new nint(0x2000), new Vector3(1f, 2f, 3f)),
            NativeCharacterSnapshot(1, new nint(0x2000), new Vector3(1f, 2f, 3f)),
        ]);
    var ambiguous = TitleBackgroundCharacterSourceEvaluation.Evaluate(
        [
            NativeCharacterSnapshot(0, new nint(0x3000), new Vector3(1f, 2f, 3f)),
            NativeCharacterSnapshot(1, new nint(0x4000), new Vector3(1f, 2f, 3f)),
        ]);
    var postLogin = TitleBackgroundCharacterSourceEvaluation.Evaluate(
        [NativeCharacterSnapshot(0, new nint(0x5000), new Vector3(1f, 2f, 3f), captureContext: "post-login")]);

    return zero.Resolution == "found-but-no-transform"
        && stable.Resolution == "found-single"
        && stable.AddressStable == "true"
        && stable.ObservedFrameCount == 2
        && ambiguous.Resolution == "found-ambiguous"
        && ambiguous.AddressStable == "false"
        && postLogin.PostLoginReadAttempted;
});

Test(94, "title background composited character reports placement without claiming visibility", () =>
{
    var delivery = DeliveryFromRaw(
        "stub-only",
        "all-zero-transform",
        "not-observed",
        8,
        0,
        0,
        0,
        [],
        lastOverrideApplied: true,
        currentObjectTableValidForCharaSelect: false,
        characterCompositedApplied: true);

    return delivery.CharacterVisibilityObserved == "composited-experimental"
        && delivery.CharacterVisibilityBlocker == "visual-confirmation-required"
        && delivery.MvpStatus == "character-placement-applied-unverified"
        && delivery.MvpBlockingIssue == "visual-confirmation-required";
});

Test(96, "camera focus fallback is not treated as ground verified", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterExpectedVisible: false,
        characterCompositedApplied: true,
        characterPlacedViaCameraFocusFallback: true));

    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.CharacterStatus == "placed in frame / ground position not confirmed"
        && result.Warnings.Any(warning => warning.Contains("camera-focus fallback", StringComparison.Ordinal)
            && warning.Contains("visual confirmation required", StringComparison.Ordinal))
        && result.Reason == "background works but character visibility is not visually confirmed";
});

Test(97, "anchor verified placement is treated as ground placement success", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterExpectedVisible: false,
        characterCompositedApplied: true,
        characterGroundPlacementVerified: true));

    return result.CharacterStatus == "placement verified on ground anchor"
        && !result.Warnings.Any(warning => warning.Contains("camera-focus fallback", StringComparison.Ordinal));
});

Test(98, "passive observation suppresses unapplied camera profile warning", () =>
{
    // passive 観測中はカメラを書き換えない仕様。yaw/pitch/distance 未適用は失敗ではないので警告しない。
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        passiveCameraObservationActive: true,
        cameraVisibleProfileResolved: true,
        cameraYaw: "",
        cameraPitch: "",
        cameraDistance: ""));

    return !result.Warnings.Any(warning => warning.Contains("yaw/pitch/distance was not applied", StringComparison.Ordinal));
});

Test(99, "configured camera override still warns when profile not applied", () =>
{
    // passive OFF（override を適用する設定）なのに未適用なら、従来どおり警告し本当の失敗を隠さない。
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        passiveCameraObservationActive: false,
        cameraVisibleProfileResolved: true,
        cameraYaw: "",
        cameraPitch: "",
        cameraDistance: ""));

    return result.Warnings.Any(warning => warning.Contains("yaw/pitch/distance was not applied", StringComparison.Ordinal));
});

Test(103, "chara select fallback frame is placement-supported but lacks ground provenance", () =>
{
    // CharaSelectFallback は placement-supported だが、水上座標の再保存の可能性があり地面確認済みにしない。
    return TitleBackgroundCharaSelectAnchorFrame.IsPlacementSupported(
            TitleBackgroundCharaSelectAnchorFrame.CharaSelectFallback)
        && !TitleBackgroundCharaSelectAnchorFrame.HasGroundProvenance(
            TitleBackgroundCharaSelectAnchorFrame.CharaSelectFallback)
        && TitleBackgroundCharaSelectAnchorFrame.HasGroundProvenance(
            TitleBackgroundCharaSelectAnchorFrame.LobbyNative)
        && !TitleBackgroundCharaSelectAnchorFrame.HasGroundProvenance(
            TitleBackgroundCharaSelectAnchorFrame.World)
        && !TitleBackgroundCharaSelectAnchorFrame.HasGroundProvenance(
            TitleBackgroundCharaSelectAnchorFrame.Unknown);
});

Test(104, "fallback anchor placement is not treated as ground verified", () =>
{
    // anchor 由来でも frame が CharaSelectFallback なら地面確認済みにしない。
    var groundVerified = TitleBackgroundAutomaticCheckLogic.ResolveGroundPlacementVerified(
        placementApplied: true,
        placementSource: TitleBackgroundCharaSelectAnchorLogic.AnchorSource,
        anchorFrame: TitleBackgroundCharaSelectAnchorFrame.CharaSelectFallback);
    var lobbyNativeVerified = TitleBackgroundAutomaticCheckLogic.ResolveGroundPlacementVerified(
        placementApplied: true,
        placementSource: TitleBackgroundCharaSelectAnchorLogic.AnchorSource,
        anchorFrame: TitleBackgroundCharaSelectAnchorFrame.LobbyNative);

    // 評価器: provenance 不足の配置は WARN（地面位置未確認）として扱われ、false OK にならない。
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterExpectedVisible: false,
        characterCompositedApplied: true,
        characterPlacedViaCameraFocusFallback: false,
        characterGroundPlacementVerified: false));

    return !groundVerified
        && lobbyNativeVerified
        && result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.CharacterStatus == "placement applied / ground position not confirmed"
        && result.Warnings.Any(warning => warning.Contains("ground position is not verified", StringComparison.Ordinal));
});

Test(106, "post-login event anomaly is run-scoped: previous run anomaly does not fail current run", () =>
{
    // 前回 run で検出（lastEventSeq=5）、今回 run の開始 seq=10 → 今回の異常ではない。
    var previousRunAnomaly = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedEventAnomaly(
        runScoped: true,
        detected: true,
        lastEventSeq: 5,
        runStartEventSeq: 10);
    // 今回 run 内で発生（lastEventSeq=15 > 開始 seq=10）→ 今回の異常として維持する。
    var currentRunAnomaly = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedEventAnomaly(
        runScoped: true,
        detected: true,
        lastEventSeq: 15,
        runStartEventSeq: 10);
    // 通常の長期診断（run-scoped でない）は累積履歴を維持する。
    var longTerm = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedEventAnomaly(
        runScoped: false,
        detected: true,
        lastEventSeq: 5,
        runStartEventSeq: 10);

    return !previousRunAnomaly && currentRunAnomaly && longTerm;
});

Test(107, "post-login state anomaly is run-scoped: stale history does not fail current run", () =>
{
    // 前回 run の sticky 履歴あり・今回 run は正常状態 → 今回は異常としない。
    var staleHistoryOnly = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedStateAnomaly(
        runScoped: true,
        historicalDetected: true,
        freshDetected: false);
    // 今回 run の状態が異常 → 維持する。
    var currentStateAnomaly = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedStateAnomaly(
        runScoped: true,
        historicalDetected: false,
        freshDetected: true);
    // 通常の長期診断は累積履歴も含める。
    var longTerm = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedStateAnomaly(
        runScoped: false,
        historicalDetected: true,
        freshDetected: false);

    return !staleHistoryOnly && currentStateAnomaly && longTerm;
});

Test(108, "delivery diagnostic is wired to run-scoped anomaly and placement values", () =>
{
    // 配線回帰ガード: Delivery 判定が累積/sticky をそのまま受け取っていないことをソースで検証する。
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleScreenBackgroundService.Diagnostics.cs"));
    var callStart = source.IndexOf("TitleBackgroundDeliveryDiagnostic.BuildSummary(", StringComparison.Ordinal);
    var callEnd = callStart >= 0 ? source.IndexOf(");", callStart, StringComparison.Ordinal) : -1;
    var call = callStart >= 0 && callEnd > callStart ? source[callStart..callEnd] : string.Empty;

    return call.Length > 0
        // 自動確認用に run-scoped 解決した値を渡している。
        && call.Contains("deliveryPhase2GAppliedAfterLogin", StringComparison.Ordinal)
        && call.Contains("deliveryCharacterPlacementApplied", StringComparison.Ordinal)
        // 背景適用も run-scoped 値を渡している。
        && call.Contains("deliveryLastOverrideApplied", StringComparison.Ordinal)
        && call.Contains("deliveryHistoricalOverrideApplied", StringComparison.Ordinal)
        && call.Contains("deliveryHistoricalOverridePath", StringComparison.Ordinal)
        // 累積 placement count / sticky Phase2G / 累積 override 履歴をそのまま渡していない。
        && !call.Contains("_characterPlacement.CharaSelectCharacterPlacementCount > 0", StringComparison.Ordinal)
        && !call.Contains("_transitionDiagnostics.Phase2GAppliedAfterLogin", StringComparison.Ordinal)
        && !call.Contains("_lastOverrideApplied", StringComparison.Ordinal)
        && !call.Contains("_lastHistoricalOverridePath", StringComparison.Ordinal)
        // scene override leak も run-scoped state 解決を経由している。
        && source.Contains("phase2NSceneOverrideActiveAfterLoginDetected = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedStateAnomaly", StringComparison.Ordinal)
        && source.Contains("deliveryRunScoped", StringComparison.Ordinal);
});

Test(109, "delivery background applied is run-scoped: previous run success does not leak", () =>
{
    // 前回 run で1回適用（累積1）、今回 run の開始 baseline も1 → 今回は0回適用。
    var appliedThisRun = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedCount(
        runScoped: true,
        cumulativeCount: 1,
        runStartCount: 1) > 0;
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    // 今回0回なら historical flag/path も Delivery 判定へ入れない。
    var delivery = Delivery(
        summary,
        lastOverrideApplied: appliedThisRun,
        transitionSafety: "safe",
        historicalLastOverrideApplied: appliedThisRun,
        historicalLastOverridePath: appliedThisRun ? "ex3/01_nvt_n4/fld/n4f4/level/n4f4" : string.Empty);

    return !appliedThisRun
        && !delivery.BackgroundApplication.Observed
        && delivery.BackgroundDeliveryVerdict == "not-observed"
        && delivery.DeliveryVerdict != "working-background-only";
});

Test(110, "delivery background applied is run-scoped: current run success is observed", () =>
{
    // 今回 run で適用（累積2 - baseline1 = 1回）→ 背景適用を観測扱いにする。
    var appliedThisRun = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedCount(
        runScoped: true,
        cumulativeCount: 2,
        runStartCount: 1) > 0;
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(
        summary,
        lastOverrideApplied: appliedThisRun,
        transitionSafety: "safe",
        historicalLastOverrideApplied: appliedThisRun,
        historicalLastOverridePath: appliedThisRun ? "ex3/01_nvt_n4/fld/n4f4/level/n4f4" : string.Empty);

    return appliedThisRun
        && delivery.BackgroundApplication.Observed
        && delivery.BackgroundDeliveryVerdict == "working-background-only-observed";
});

Test(111, "automatic report selects run-scoped character placement evidence", () =>
{
    // 配線回帰ガード: 自動確認レポートが run-scoped の配置証拠を選択し、累積 last* を出さない。
    var root = FindRepositoryRoot();
    var serviceSource = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleScreenBackgroundService.cs"));
    var quickCheckSource = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleBackgroundQuickCheck.cs"));
    var includedStart = quickCheckSource.IndexOf("IncludedKeys", StringComparison.Ordinal);
    var includedEnd = includedStart >= 0 ? quickCheckSource.IndexOf("};", includedStart, StringComparison.Ordinal) : -1;
    var included = includedStart >= 0 && includedEnd > includedStart ? quickCheckSource[includedStart..includedEnd] : string.Empty;

    return serviceSource.Contains("characterPlace.runAppliedFrameCount=", StringComparison.Ordinal)
        && serviceSource.Contains("characterPlace.runSource=", StringComparison.Ordinal)
        && serviceSource.Contains("characterPlace.runAnchorFrame=", StringComparison.Ordinal)
        && included.Length > 0
        && included.Contains("\"characterPlace.runAppliedFrameCount\"", StringComparison.Ordinal)
        && included.Contains("\"characterPlace.runSource\"", StringComparison.Ordinal)
        && included.Contains("\"characterPlace.runAnchorFrame\"", StringComparison.Ordinal)
        // 累積 last* は自動レポートに含めない。
        && !included.Contains("\"characterPlace.lastSource\"", StringComparison.Ordinal)
        && !included.Contains("\"characterPlace.appliedFrameCount\"", StringComparison.Ordinal);
});

Test(113, "title background pre-login native source remains verifiable after login", () =>
{
    var nativeSummary = TitleBackgroundCharacterSourceEvaluation.Evaluate(
    [
        NativeCharacterSnapshot(0, new nint(0x6000), new Vector3(1f, 2f, 3f)),
        NativeCharacterSnapshot(1, new nint(0x6000), new Vector3(1f, 2f, 3f)),
    ]);
    var delivery = DeliveryFromRaw(
        "single",
        "valid-world-transform",
        "observed",
        0,
        1,
        1,
        0,
        [
            new TitleBackgroundCharacterPlacementSourceDiscovery(
                TitleBackgroundCharacterSourceEvaluation.SourceName,
                true,
                1,
                1,
                "none",
                1,
                1,
                0,
                "read",
                "pre-login",
                new nint(0x6000)),
        ],
        lastOverrideApplied: true,
        currentObjectTableValidForCharaSelect: false,
        nativeCharacterSource: nativeSummary);
    var lines = TitleBackgroundDeliveryDiagnostic.BuildLineList(delivery);

    return delivery.NativePreviewSourceResolution == "found-single"
        && delivery.NativePreviewSourceCaptureContext == "pre-login"
        && delivery.NativePreviewSourceCurrentObjectTableIgnored
        && delivery.ActorPlacementReady
        && delivery.MvpStatus == "complete-background-only"
        && !delivery.NativePreviewSourcePostLoginReadAttempted
        && lines.Contains("phase2N.nativePreviewSource.captureContext=pre-login")
        && lines.Contains("delivery.nativePreviewSource.captureContext=pre-login");
});

Test(114, "title background single native sample is not placement ready", () =>
{
    var nativeSummary = TitleBackgroundCharacterSourceEvaluation.Evaluate(
    [
        NativeCharacterSnapshot(0, new nint(0x6000), new Vector3(1f, 2f, 3f)),
    ]);
    var delivery = DeliveryFromRaw(
        "single",
        "valid-world-transform",
        "observed",
        0,
        1,
        1,
        0,
        [
            new TitleBackgroundCharacterPlacementSourceDiscovery(
                TitleBackgroundCharacterSourceEvaluation.SourceName,
                true,
                1,
                1,
                "none",
                1,
                1,
                0,
                "read",
                "pre-login",
                new nint(0x6000)),
        ],
        currentObjectTableValidForCharaSelect: false,
        nativeCharacterSource: nativeSummary);

    return nativeSummary.Resolution == "found-single"
        && nativeSummary.AddressStable == "single-sample"
        && !delivery.ActorPlacementReady;
});

Test(115, "title background native probe uses existing pipeline without object table stat contamination", () =>
{
    var root = FindRepositoryRoot();
    var timeline = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var probe = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundCharacterSourceProbe.cs"));
    var statsIndex = timeline.IndexOf("var stats = BuildCharacterPlacementObjectTableStats(scanned);", StringComparison.Ordinal);
    var nativeIndex = timeline.IndexOf("var nativeCandidate = TryCreateNativeCharacterPlacementActorCandidate", StringComparison.Ordinal);
    var gateIndex = timeline.IndexOf("TitleBackgroundCharacterSourceCaptureGate.Evaluate", StringComparison.Ordinal);
    var captureIndex = timeline.IndexOf("TitleBackgroundCharacterSourceProbe.Capture(frame)", StringComparison.Ordinal);

    // The pre-login diagnostic capture still flows through the existing pipeline
    // (stats computed before native candidate; gate before capture) and never uses
    // a signature resolver. Character placement/facing writes (the n4f4 compositing path) are
    // intentionally allowed only via their dedicated source-probe methods.
    return statsIndex >= 0 && nativeIndex > statsIndex
        && gateIndex >= 0 && captureIndex > gateIndex
        && probe.Contains("CharaSelectCharacterList.GetCurrentCharacter()", StringComparison.Ordinal)
        && !probe.Contains("TitleBackgroundAddressResolver", StringComparison.Ordinal)
        && probe.Contains("TrySetCurrentCharacterDrawPosition", StringComparison.Ordinal)
        && probe.Contains("TrySetCurrentCharacterDrawRotation", StringComparison.Ordinal);
});

Test(116, "title background simple auto setup configures n4f4 recommended route", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundOverrideEnabled = false,
        TitleBackgroundCameraOverrideEnabled = false,
        TitleBackgroundIntegratedCompositionEnabled = false,
        TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty,
        TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.ResolveOnly,
        TitleBackgroundCharaSelectCameraFramingMode = TitleBackgroundCharaSelectCameraFramingMode.Default,
    };

    TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(configuration);
    return configuration.TitleBackgroundOverrideEnabled
        && configuration.TitleBackgroundCameraOverrideEnabled
        && configuration.TitleBackgroundIntegratedCompositionEnabled
        // 恒久 baseline は verified V2（点8）。placement path は OneClick run が run-scoped に arm する。
        && configuration.TitleBackgroundV2Enabled
        && !configuration.TitleBackgroundCharaSelectPlacementEnabled
        && configuration.TitleBackgroundCharacterSelectOverrideCandidateId == "custom:n4f4"
        && configuration.TitleBackgroundCharaSelectCameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended
        && configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly;
});

Test(119, "title background simple missing captured profile does not show legacy capture procedure", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundOverrideEnabled = true,
        TitleBackgroundCameraOverrideEnabled = true,
        TitleBackgroundIntegratedCompositionEnabled = true,
        TitleBackgroundV2Enabled = true,
        TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.CharaSelectOnly,
        TitleBackgroundCharacterSelectOverrideCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectCameraFramingMode = TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        TitleBackgroundCapturedCameraProfileEnabled = false,
        TitleBackgroundLastQuickCheckResult = TitleBackgroundQuickCheckLevel.WARN,
        TitleBackgroundLastQuickCheckReason = "camera profile missing",
        TitleBackgroundLastQuickCheckNextAction = "Enable legacy shooting composition, confirm character is visible, then click Capture legacy visible camera.",
    };

    var summary = TitleBackgroundQuickCheckUiPresenter.BuildSimpleSummary(configuration);
    return !summary.NextActionLine.Contains("legacy", StringComparison.OrdinalIgnoreCase)
        && !summary.NextActionLine.Contains("Capture", StringComparison.Ordinal)
        && summary.NextActionLine.Contains("Advanced", StringComparison.Ordinal);
});

Test(137, "nearest level resolves by territory and xyz", () =>
{
    CharaSelectLevelCandidate[] candidates =
    [
        new(10, 100, 1, 0f, 0f, 0f),
        new(11, 100, 2, 10f, 0f, 0f),
        new(12, 101, 3, 1f, 0f, 0f),
    ];
    var resolved = CharaSelectLevelResolver.ResolveNearest(candidates, 100, 8f, 0f, 0f);
    return resolved.RowId == 11 && resolved.Type == 2;
});

Test(138, "nearest level ignores different territory", () =>
{
    CharaSelectLevelCandidate[] candidates =
    [
        new(10, 100, 1, 0f, 0f, 0f),
    ];
    return !CharaSelectLevelResolver.ResolveNearest(candidates, 101, 0f, 0f, 0f).IsValid;
});

Test(139, "lobby position resolves by territory param", () =>
{
    CharaSelectLobbyCandidate[] candidates =
    [
        new(1, 0, 100, 0),
        new(2, 1, 200, 0),
    ];
    return CharaSelectLobbyPositionResolver.ResolveByTerritory(candidates, 200, 9) == 2;
});

Test(140, "lobby position falls back when territory is missing", () =>
{
    CharaSelectLobbyCandidate[] candidates =
    [
        new(1, 0, 100, 0),
    ];
    return CharaSelectLobbyPositionResolver.ResolveByTerritory(candidates, 200, 9) == 9;
});

Test(141, "title background path normalizes bg lvb wrapper", () =>
{
    var normalized = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(
        @" bg\ex5\01_xkt_x6\fld\x6f3\level\x6f3.lvb ");
    return normalized == "ex5/01_xkt_x6/fld/x6f3/level/x6f3"
        && TitleBackgroundPathHelper.BuildLvbPath(normalized) == "bg/ex5/01_xkt_x6/fld/x6f3/level/x6f3.lvb"
        && TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath(normalized);
});

Test(142, "title background path validation accepts base and expansion pack roots", () =>
{
    return TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ffxiv/area/region/level/sample")
        && TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex5/01_xkt_x6/fld/x6f3/level/x6f3")
        && TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex6/foo/bar/level/baz")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("abc/foo/bar/level/baz")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ffxiv/area/region/sample")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("../ffxiv/area/region/level/sample")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("bg/ex5/01_xkt_x6/fld/x6f3/level/x6f3.lvb");
});

Test(143, "title background path validation rejects unsafe normalized paths", () =>
{
    return !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath(string.Empty)
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath(@"ex5\01_xkt_x6\fld\x6f3\level\x6f3")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex5/01_xkt_x6//fld/x6f3/level/x6f3")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex5/01_xkt_x6/../x6f3/level/x6f3")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex5:/01_xkt_x6/fld/x6f3/level/x6f3")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("/ex5/01_xkt_x6/fld/x6f3/level/x6f3")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex5/01_xkt_x6/fld/x6f3/level/x6f3/");
});

Test(144, "title background preset normalizes path and clamps fov", () =>
{
    var preset = new TitleBackgroundPreset
    {
        Name = "  test  ",
        TerritoryPath = "bg/ffxiv/area/region/level/sample.lvb",
        CameraX = float.PositiveInfinity,
        CameraY = -200000f,
        FocusZ = 200000f,
        FovY = 999f,
        BgmPath = "  bgm/test.scd  ",
    }.Normalize();

    return preset.Name == "test"
        && preset.TerritoryPath == "ffxiv/area/region/level/sample"
        && preset.CameraX == 0f
        && preset.CameraY == -100000f
        && preset.FocusZ == 100000f
        && preset.FovY == TitleBackgroundPreset.MaxFovY
        && preset.BgmPath == "bgm/test.scd";
});

Test(145, "title background preset validates normalized territory path", () =>
{
    var valid = new TitleBackgroundPreset
    {
        TerritoryPath = "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
    };
    var invalid = new TitleBackgroundPreset
    {
        TerritoryPath = "abc/foo/bar/level/baz",
    };

    return valid.Validate(out _)
        && !invalid.Validate(out var errorMessage)
        && errorMessage == "TerritoryPath は <pack>/.../level/... 形式で指定してください。";
});

Test(146, "title background built-in preset catalog ids are stable and unique", () =>
{
    var ids = TitleBackgroundBuiltInPresetCatalog.Presets
        .Select(entry => TitleBackgroundBuiltInPresetCatalog.NormalizeId(entry.Id))
        .ToList();

    return ids.All(id => !string.IsNullOrWhiteSpace(id))
        && ids.Count == ids.Distinct(StringComparer.Ordinal).Count()
        && TitleBackgroundBuiltInPresetCatalog.Presets.All(entry =>
            entry.Id == TitleBackgroundBuiltInPresetCatalog.NormalizeId(entry.Id)
            && !string.IsNullOrWhiteSpace(entry.DisplayName)
            && entry.Preset.Normalize().Validate(out _));
});

Test(147, "title background preset applicator expands selected preset atomically", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "old",
        TitleBackgroundTerritoryPath = "ffxiv/old/region/level/old",
        TitleBackgroundCameraX = 99f,
    };
    var preset = new TitleBackgroundPreset
    {
        TerritoryPath = "bg/ffxiv/area/region/level/sample.lvb",
        TerritoryTypeId = 777,
        LayoutTerritoryTypeId = 778,
        LayoutLayerFilterKey = 9,
        CharacterPosition = new Vector3(1f, 2f, 3f),
        CharacterRotation = 0.5f,
        CameraX = 4f,
        CameraY = 5f,
        CameraZ = 6f,
        FocusX = 7f,
        FocusY = 8f,
        FocusZ = 9f,
        FovY = 1.2f,
    };

    return TitleBackgroundPresetApplicator.TryApplyPreset(
            configuration,
            preset,
            "verified-a",
            path => path == "bg/ffxiv/area/region/level/sample.lvb",
            out _)
        && configuration.TitleBackgroundSelectedPresetId == "verified-a"
        && configuration.TitleBackgroundTerritoryPath == "ffxiv/area/region/level/sample"
        && configuration.TitleBackgroundTerritoryTypeId == 777
        && configuration.TitleBackgroundLayoutTerritoryTypeId == 778
        && configuration.TitleBackgroundLayoutLayerFilterKey == 9
        && configuration.TitleBackgroundCharacterPositionX == 1f
        && configuration.TitleBackgroundCameraX == 4f
        && configuration.TitleBackgroundFocusZ == 9f
        && Math.Abs(configuration.TitleBackgroundFovY - 1.2f) < 0.0001f;
});

Test(150, "title background unknown selected preset id falls back to custom", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "missing",
        TitleBackgroundTerritoryPath = "ffxiv/area/region/level/sample",
    };

    return TitleBackgroundPresetApplicator.ClearInvalidSelectedPreset(configuration)
        && configuration.TitleBackgroundSelectedPresetId == string.Empty
        && configuration.TitleBackgroundTerritoryPath == "ffxiv/area/region/level/sample";
});

Test(151, "title background debug capture clears selected preset id", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "verified-a",
        TitleBackgroundTerritoryPath = "ffxiv/old/region/level/old",
    };
    var preset = new TitleBackgroundPreset
    {
        TerritoryPath = "ffxiv/area/region/level/sample",
        CameraX = 1f,
    };

    TitleBackgroundPresetApplicator.ApplyDebugPreset(configuration, preset);
    return configuration.TitleBackgroundSelectedPresetId == string.Empty
        && configuration.TitleBackgroundTerritoryPath == "ffxiv/area/region/level/sample"
        && configuration.TitleBackgroundCameraX == 1f;
});

Test(152, "title background selected preset id is present in export import payload", () =>
{
    var configuration = new Configuration();
    var exported = configuration.ExportToBase64();
    var json = Encoding.UTF8.GetString(Convert.FromBase64String(exported));

    return json.Contains("\"TitleBackgroundSelectedPresetId\"", StringComparison.Ordinal)
        && configuration.TryParseImport(exported, out var imported, out _)
        && imported.TitleBackgroundSelectedPresetId == string.Empty;
});

Test(153, "title background fov clamp handles lower bound and non finite", () =>
{
    return TitleBackgroundPreset.ClampFovY(-1f) == TitleBackgroundPreset.MinFovY
        && TitleBackgroundPreset.ClampFovY(float.NaN) == TitleBackgroundPreset.DefaultFovY;
});

Test(154, "title background camera override plan uses focus fields", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundCameraX = 1f,
        TitleBackgroundCameraY = 2f,
        TitleBackgroundCameraZ = 3f,
        TitleBackgroundFocusX = 4f,
        TitleBackgroundFocusY = 5f,
        TitleBackgroundFocusZ = 6f,
        TitleBackgroundCharacterPositionX = 40f,
        TitleBackgroundCharacterPositionY = 50f,
        TitleBackgroundCharacterPositionZ = 60f,
        TitleBackgroundFovY = 1.2f,
    };

    var plan = TitleBackgroundCameraOverridePlan.FromConfiguration(configuration);
    return plan.Camera == new Vector3(1f, 2f, 3f)
        && plan.Focus == new Vector3(4f, 5f, 6f)
        && plan.Focus != new Vector3(40f, 50f, 60f)
        && Math.Abs(plan.FovY - 1.2f) < 0.0001f;
});

Test(155, "title background camera override plan clamps fov", () =>
{
    var plan = TitleBackgroundCameraOverridePlan.Create(
        new Vector3(1f, 2f, 3f),
        new Vector3(4f, 5f, 6f),
        999f);
    return plan.FovY == TitleBackgroundPreset.MaxFovY;
});

Test(156, "title background preset camera focus derives lobby pose", () =>
{
    var camera = new Vector3(0f, 1f, 0f);
    var focus = new Vector3(0f, 1f, 10f);
    return TitleBackgroundCharaSelectCameraLogic.TryBuildPoseFromCameraFocus(
            camera,
            focus,
            out var yaw,
            out var pitch,
            out var distance,
            out var lookAtY,
            out _)
        && Math.Abs(yaw) < 0.0001f
        && Math.Abs(pitch) < 0.0001f
        && Math.Abs(distance - 10f) < 0.0001f
        && Math.Abs(lookAtY - 1f) < 0.0001f;
});

Test(157, "title background preset camera focus rejects zero distance", () =>
{
    return !TitleBackgroundCharaSelectCameraLogic.TryBuildPoseFromCameraFocus(
        new Vector3(1f, 2f, 3f),
        new Vector3(1f, 2f, 3f),
        out _,
        out _,
        out _,
        out _,
        out var errorMessage)
        && errorMessage.Contains("distance", StringComparison.Ordinal);
});

Test(158, "title background legacy direct camera apply is disabled", () =>
{
    return !TitleBackgroundCameraOverridePlan.ShouldApply(
            cameraOverrideEnabled: true,
            isHookProbeMode: false,
            cameraApplyPending: true,
            stateReady: true,
            currentMapAvailable: true,
            currentMap: GameLobbyType.CharaSelect)
        && !TitleBackgroundCameraOverridePlan.ShouldApply(
            cameraOverrideEnabled: true,
            isHookProbeMode: true,
            cameraApplyPending: true,
            stateReady: true,
            currentMapAvailable: true,
            currentMap: GameLobbyType.CharaSelect)
        && !TitleBackgroundCameraOverridePlan.ShouldApply(
            cameraOverrideEnabled: true,
            isHookProbeMode: false,
            cameraApplyPending: false,
            stateReady: true,
            currentMapAvailable: true,
            currentMap: GameLobbyType.CharaSelect)
        && !TitleBackgroundCameraOverridePlan.ShouldApply(
            cameraOverrideEnabled: true,
            isHookProbeMode: false,
            cameraApplyPending: true,
            stateReady: true,
            currentMapAvailable: true,
            currentMap: GameLobbyType.Title);
});

Test(159, "title background chara select camera input uses character fields only", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundCharacterPositionX = 1f,
        TitleBackgroundCharacterPositionY = 2f,
        TitleBackgroundCharacterPositionZ = 3f,
        TitleBackgroundCharacterRotation = MathF.PI * 3f,
        TitleBackgroundCameraX = 100f,
        TitleBackgroundFocusY = 200f,
        TitleBackgroundFovY = 9f,
    };

    var input = TitleBackgroundCharaSelectCameraInput.FromConfiguration(configuration);
    return input.CharacterPosition == new Vector3(1f, 2f, 3f)
        && Math.Abs(input.CharacterRotation - MathF.PI) < 0.0001f;
});

Test(160, "title background chara select camera state machine follows phase one path", () =>
{
    var state = TitleBackgroundCharaSelectCameraAdapterState.Inactive;
    state = TitleBackgroundCharaSelectCameraLogic.Transition(state, TitleBackgroundCharaSelectCameraAdapterEvent.ConfigureEnabled);
    state = TitleBackgroundCharaSelectCameraLogic.Transition(state, TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoadStarted);
    state = TitleBackgroundCharaSelectCameraLogic.Transition(state, TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoaded);
    state = TitleBackgroundCharaSelectCameraLogic.Transition(state, TitleBackgroundCharaSelectCameraAdapterEvent.LobbyBecameActive);
    var stopping = TitleBackgroundCharaSelectCameraLogic.Transition(state, TitleBackgroundCharaSelectCameraAdapterEvent.StopRequested);
    var reset = TitleBackgroundCharaSelectCameraLogic.Transition(stopping, TitleBackgroundCharaSelectCameraAdapterEvent.Reset);

    return state == TitleBackgroundCharaSelectCameraAdapterState.Active
        && stopping == TitleBackgroundCharaSelectCameraAdapterState.Stopping
        && reset == TitleBackgroundCharaSelectCameraAdapterState.Armed;
});

Test(161, "title background chara select camera curve offsets magic values by character y", () =>
{
    var curve = TitleBackgroundCharaSelectCameraLogic.BuildCurve(2f);
    var negativeCurve = TitleBackgroundCharaSelectCameraLogic.BuildCurve(-10f);

    return Math.Abs(curve.Low - (TitleBackgroundCharaSelectCameraLogic.MagicLow + 2f)) < 0.0001f
        && Math.Abs(curve.Mid - (TitleBackgroundCharaSelectCameraLogic.MagicMid + 2f)) < 0.0001f
        && Math.Abs(curve.High - (TitleBackgroundCharaSelectCameraLogic.MagicHigh + 2f)) < 0.0001f
        && Math.Abs(negativeCurve.Low - (TitleBackgroundCharaSelectCameraLogic.MagicLow - 10f)) < 0.0001f
        && Math.Abs(negativeCurve.Mid - (TitleBackgroundCharaSelectCameraLogic.MagicMid - 10f)) < 0.0001f
        && Math.Abs(negativeCurve.High - (TitleBackgroundCharaSelectCameraLogic.MagicHigh - 10f)) < 0.0001f;
});

Test(162, "title background chara select camera adapter derives curve from input", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(new Vector3(1f, 4f, 3f), 0.25f));

    return Math.Abs(adapter.Curve.Low - (TitleBackgroundCharaSelectCameraLogic.MagicLow + 4f)) < 0.0001f
        && Math.Abs(adapter.Curve.Mid - (TitleBackgroundCharaSelectCameraLogic.MagicMid + 4f)) < 0.0001f
        && Math.Abs(adapter.Curve.High - (TitleBackgroundCharaSelectCameraLogic.MagicHigh + 4f)) < 0.0001f;
});

Test(163, "title background chara select camera adapter records runtime state without persistence", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(new Vector3(1f, 2f, 3f), 0.25f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(
        yaw: MathF.PI * 3f,
        pitch: MathF.PI,
        distance: -1f,
        lookAtY: float.PositiveInfinity,
        lookAt: new Vector3(1f, 2f, 3f));
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
        && adapter.RuntimeState.SceneGeneration == 1
        && Math.Abs(adapter.RuntimeState.Yaw!.Value - MathF.PI) < 0.0001f
        && Math.Abs(adapter.RuntimeState.YawOffset!.Value - (MathF.PI - 0.25f)) < 0.0001f
        && Math.Abs(adapter.RuntimeState.Pitch!.Value - (MathF.PI / 2f)) < 0.0001f
        && Math.Abs(adapter.RuntimeState.Distance!.Value - TitleBackgroundCharaSelectCameraLogic.MinDistance) < 0.0001f
        && adapter.RuntimeState.LookAtY == null
        && adapter.RuntimeState.LookAt == new Vector3(1f, 2f, 3f)
        && adapter.RuntimeState.HasLookAt
        && adapter.RuntimeState.CurveAtRecord == adapter.Curve
        && Math.Abs(adapter.RuntimeState.CharacterRotationAtRecord!.Value - 0.25f) < 0.0001f
        && adapter.ShouldRestoreRuntimeCameraState();
});

Test(164, "title background chara select camera load start does not mark scene loaded", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 1f);
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.SceneLoading
        && adapter.LastEvent == TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoadStarted.ToString()
        && !adapter.ShouldRestoreRuntimeCameraState();
});

Test(165, "title background chara select camera runtime restores yaw relative to current character rotation", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0.5f));
    adapter.SaveRuntimeCameraState(yaw: 1.5f, pitch: 0.25f, distance: 4f, lookAtY: 1f);
    var restoredAtInitialRotation = adapter.GetRestoredYaw();

    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 1.0f));
    var restoredAtNewRotation = adapter.GetRestoredYaw();

    return Math.Abs(adapter.RuntimeState.YawOffset!.Value - 1.0f) < 0.0001f
        && Math.Abs(restoredAtInitialRotation!.Value - 1.5f) < 0.0001f
        && Math.Abs(restoredAtNewRotation!.Value - 2.0f) < 0.0001f;
});

Test(166, "title background chara select camera marks LookAtY as one-shot after runtime restore", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.MarkRuntimeCameraStateRestored();
    var firstConsume = adapter.ConsumeShouldSetLookAtY();
    var secondConsume = adapter.ConsumeShouldSetLookAtY();

    return firstConsume
        && !secondConsume
        && !adapter.RuntimeState.ShouldSetLookAtY;
});

Test(167, "title background chara select camera does not mark LookAtY one-shot without observed LookAtY", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: float.NaN);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.MarkRuntimeCameraStateRestored();

    return !adapter.RuntimeState.ShouldSetLookAtY
        && !adapter.ConsumeShouldSetLookAtY();
});

Test(168, "title background chara select camera permits curve apply after scene loaded", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return adapter.ShouldApplyCurve();
});

Test(169, "title background chara select camera curve apply is generation one-shot", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    var first = adapter.ShouldApplyCurve();
    adapter.MarkCurveApplied();
    var second = adapter.ShouldApplyCurve();

    return first
        && !second
        && adapter.LastCurveAppliedSceneGeneration == adapter.RuntimeState.SceneGeneration;
});

Test(170, "title background phase2g generated curve override allows loaded and active states", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    var loaded = TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
        serviceReady: true,
        hookProbeMode: false,
        sceneOverrideEnabled: true,
        adapterArmed: adapter.IsArmed,
        isLoggedIn: false,
        activeCharaSelectSession: true,
        sceneGenerationMatchesActiveSession: true,
        adapter.State,
        adapter.RuntimeState,
        GameLobbyType.CharaSelect,
        GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.CharaSelect);
    var active = TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
        serviceReady: true,
        hookProbeMode: false,
        sceneOverrideEnabled: true,
        adapterArmed: adapter.IsArmed,
        isLoggedIn: false,
        activeCharaSelectSession: true,
        sceneGenerationMatchesActiveSession: true,
        adapter.State,
        adapter.RuntimeState,
        GameLobbyType.CharaSelect,
        GameLobbyType.CharaSelect);

    return loaded && active;
});

Test(171, "title background phase2g generated curve override accepts title or chara select context", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
        serviceReady: true,
        hookProbeMode: false,
        sceneOverrideEnabled: true,
        adapterArmed: adapter.IsArmed,
        isLoggedIn: false,
        activeCharaSelectSession: true,
        sceneGenerationMatchesActiveSession: true,
        adapter.State,
        adapter.RuntimeState,
        GameLobbyType.Title,
        GameLobbyType.None);
});

Test(172, "title background phase2g generated curve override rejects unsafe contexts", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: false,
            hookProbeMode: false,
            sceneOverrideEnabled: true,
            adapterArmed: adapter.IsArmed,
            isLoggedIn: false,
            activeCharaSelectSession: true,
            sceneGenerationMatchesActiveSession: true,
            adapter.State,
            adapter.RuntimeState,
            GameLobbyType.CharaSelect,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: true,
            sceneOverrideEnabled: true,
            adapterArmed: adapter.IsArmed,
            isLoggedIn: false,
            activeCharaSelectSession: true,
            sceneGenerationMatchesActiveSession: true,
            adapter.State,
            adapter.RuntimeState,
            GameLobbyType.CharaSelect,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: false,
            sceneOverrideEnabled: false,
            adapterArmed: adapter.IsArmed,
            isLoggedIn: false,
            activeCharaSelectSession: true,
            sceneGenerationMatchesActiveSession: true,
            adapter.State,
            adapter.RuntimeState,
            GameLobbyType.CharaSelect,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: false,
            sceneOverrideEnabled: true,
            adapterArmed: adapter.IsArmed,
            isLoggedIn: false,
            activeCharaSelectSession: true,
            sceneGenerationMatchesActiveSession: true,
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoading,
            adapter.RuntimeState,
            GameLobbyType.CharaSelect,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: false,
            sceneOverrideEnabled: true,
            adapterArmed: false,
            isLoggedIn: false,
            activeCharaSelectSession: true,
            sceneGenerationMatchesActiveSession: true,
            adapter.State,
            adapter.RuntimeState,
            GameLobbyType.CharaSelect,
            GameLobbyType.CharaSelect);
});

Test(173, "title background phase2g generated curve override rejects logged in context", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
        serviceReady: true,
        hookProbeMode: false,
        sceneOverrideEnabled: true,
        adapterArmed: adapter.IsArmed,
        isLoggedIn: true,
        activeCharaSelectSession: true,
        sceneGenerationMatchesActiveSession: true,
        adapter.State,
        adapter.RuntimeState,
        GameLobbyType.CharaSelect,
        GameLobbyType.CharaSelect);
});

Test(174, "title background phase2g generated curve override rejects inactive session", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
        serviceReady: true,
        hookProbeMode: false,
        sceneOverrideEnabled: true,
        adapterArmed: adapter.IsArmed,
        isLoggedIn: false,
        activeCharaSelectSession: false,
        sceneGenerationMatchesActiveSession: true,
        adapter.State,
        adapter.RuntimeState,
        GameLobbyType.CharaSelect,
        GameLobbyType.CharaSelect);
});

Test(175, "title background adapter end session clears active runtime state", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    adapter.EndSession();

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.Stopping
        && !adapter.RuntimeState.HasCameraPose
        && !adapter.ShouldApplyCurve()
        && !adapter.ShouldApplyLookAtY();
});

Test(177, "title background generated curve success requires counts and final look at y", () =>
{
    return TitleBackgroundCameraProbeReport.IsGeneratedCurveOverrideSuccess(
            setMidAttemptCount: 3,
            setMidAppliedCount: 3,
            lowHighAttemptCount: 3,
            lowHighAppliedCount: 3,
            finalLookAtYMatchesGeneratedCurveVerdict: "observed")
        && !TitleBackgroundCameraProbeReport.IsGeneratedCurveOverrideSuccess(
            setMidAttemptCount: 3,
            setMidAppliedCount: 2,
            lowHighAttemptCount: 3,
            lowHighAppliedCount: 3,
            finalLookAtYMatchesGeneratedCurveVerdict: "observed")
        && !TitleBackgroundCameraProbeReport.IsGeneratedCurveOverrideSuccess(
            setMidAttemptCount: 3,
            setMidAppliedCount: 3,
            lowHighAttemptCount: 3,
            lowHighAppliedCount: 3,
            finalLookAtYMatchesGeneratedCurveVerdict: "not-observed");
});

Test(178, "title background transition diagnostics retain last 128 monotonic events", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    for (var i = 0; i < 140; i++)
    {
        recorder.Record($"event-{i}");
    }

    var events = recorder.Events;
    return events.Count == TitleBackgroundTransitionDiagnosticRecorder.RingCapacity
        && events[0].Sequence == 13
        && events[^1].Sequence == 140
        && events.Zip(events.Skip(1)).All(pair => pair.First.Sequence < pair.Second.Sequence);
});

Test(179, "title background transition diagnostics flag repeated sceneReady acceptance", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.RecordSceneReadyAccepted(new Dictionary<string, string>(), "first", 1, isLoggedIn: false);
    recorder.RecordSceneReadyAccepted(new Dictionary<string, string>(), "second", 1, isLoggedIn: false);
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: false));

    return lines.Contains("transition.sceneReady.acceptedCount=2")
        && lines.Contains("transition.sceneReady.acceptedCount.suspicious=True")
        && lines.Contains("transition.verdict.sceneReadyAcceptedMultipleTimes=True");
});

Test(180, "title background transition diagnostics compute deltas since previous diag", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var first = recorder.ComputeDeltaSinceLastDiagnostic(new TitleBackgroundTransitionCounters(10, 20, 30, 40, 50, 2, 3));
    var second = recorder.ComputeDeltaSinceLastDiagnostic(new TitleBackgroundTransitionCounters(13, 22, 31, 41, 55, 2, 4));

    return first.FirstReport
        && !first.BaselineEstablished
        && first.Phase2ELookAtYCallCount == 0
        && first.Phase2GSetMidAttemptCount == 0
        && !second.FirstReport
        && second.BaselineEstablished
        && second.Phase2ELookAtYCallCount == 3
        && second.Phase2FSetMidCallCount == 2
        && second.Phase2FLowHighCallCount == 1
        && second.Phase2GSetMidAttemptCount == 1
        && second.Phase2GLowHighAttemptCount == 5
        && second.SceneReadyAcceptedCount == 0
        && second.SceneReadyRawCallCount == 1;
});

Test(181, "title background transition normal diagnostics include summary without full trace", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.Record("CreateSceneDetour entered");
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: false));

    return lines.Any(line => line.StartsWith("transition.eventCount=", StringComparison.Ordinal))
        && lines.Any(line => line.StartsWith("transition.verdict.loginTransitionSafety=", StringComparison.Ordinal))
        && !lines.Any(line => line.StartsWith("transition.event[", StringComparison.Ordinal));
});

Test(182, "title background transition detailed diagnostics include event trace", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.Record("CreateSceneDetour entered");
    return recorder.BuildTraceLines().Any(line => line.StartsWith("transition.event[0].seq=1; name=CreateSceneDetour entered", StringComparison.Ordinal));
});

Test(183, "title background transition diagnostics flag stale adapter after login", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.MarkPostLoginStaleState(new Dictionary<string, string>(), staleAdapter: true, staleCurrentLobbyMap: false, staleSceneOverride: false);
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: true,
        staleAdapter: true));

    return lines.Contains("transition.adapter.staleAfterLoginDetected=True")
        && lines.Contains("transition.verdict.staleCharaSelectStateAfterLogin=True")
        && lines.Contains("transition.verdict.loginTransitionSafety=unsafe");
});

Test(184, "title background historical scene override alone is safe after login", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: true,
        historicalLastOverrideApplied: true));

    return lines.Contains("transition.sceneOverride.active=False")
        && lines.Contains("transition.sceneOverride.historicalLastOverrideApplied=True")
        && lines.Contains("transition.sceneOverride.activeAfterLoginDetected=False")
        && lines.Contains("transition.verdict.staleCharaSelectStateAfterLogin=False")
        && lines.Contains("transition.verdict.loginTransitionSafety=safe");
});

Test(185, "title background active scene override after login is unsafe", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: true,
        activeSceneOverride: true,
        historicalLastOverrideApplied: true,
        activeSceneOverrideAfterLogin: true));

    return lines.Contains("transition.sceneOverride.active=True")
        && lines.Contains("transition.sceneOverride.activeAfterLoginDetected=True")
        && lines.Contains("transition.verdict.staleCharaSelectStateAfterLogin=True")
        && lines.Contains("transition.verdict.loginTransitionSafety=unsafe");
});

Test(186, "title background transition cleanup reason is reported", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var worldLoginLines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: true,
        cleanupReason: "world-login-transition"));
    var leavingLines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: false,
        cleanupReason: "leaving-chara-select-context"));

    return worldLoginLines.Contains("transition.sceneOverride.lastCurrentLobbyMapResetReason=world-login-transition")
        && worldLoginLines.Contains("transition.sceneOverride.cleanupReason=world-login-transition")
        && leavingLines.Contains("transition.sceneOverride.lastCurrentLobbyMapResetReason=leaving-chara-select-context")
        && leavingLines.Contains("transition.sceneOverride.cleanupReason=leaving-chara-select-context");
});

Test(187, "title background transition diagnostics flag Phase2G applied after login", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.RecordPhase2GApply(new Dictionary<string, string>(), isLoggedIn: true, isCharaSelectOrTitleBackground: false, "allowed");
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 1, 0, 0, 0),
        isLoggedIn: true,
        phase2GAppliedAfterLogin: true));

    return lines.Contains("transition.phase2G.appliedAfterLogin=True")
        && lines.Contains("transition.verdict.postLoginPhase2GStillApplying=True")
        && lines.Contains("transition.verdict.loginTransitionSafety=unsafe");
});

Test(188, "title background transition diagnostics flag sceneReady accepted after login", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.RecordSceneReadyAccepted(new Dictionary<string, string>(), "after-login", 2, isLoggedIn: true);
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 1, 0),
        isLoggedIn: true,
        sceneReadyAcceptedAfterLogin: true));

    return lines.Contains("transition.verdict.postLoginSceneReadyAccepted=True")
        && lines.Contains("transition.verdict.loginTransitionSafety=unsafe");
});

Test(189, "title background transition verdict ignores first diagnostic cumulative Phase2G delta", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var input = BuildTransitionSummaryInput(
        recorder,
        new TitleBackgroundTransitionDelta(false, true, 0, 0, 0, 1, 1, 0, 0),
        isLoggedIn: true);
    var verdicts = TitleBackgroundTransitionDiagnosticRecorder.BuildVerdicts(input);

    return !verdicts.PostLoginPhase2GStillApplying
        && verdicts.LoginTransitionSafety == "safe";
});

Test(190, "title background login transition safety is safe only after login", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var input = BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: false);
    var verdicts = TitleBackgroundTransitionDiagnosticRecorder.BuildVerdicts(input);

    return verdicts.LoginTransitionSafety == "unsafe";
});

Test(191, "title background transition spam does not evict important events", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.Record("CreateSceneDetour override applied");
    recorder.Record("CurrentLobbyMap reset");
    recorder.Record("CharaSelect title background session cleanup executed");
    var snapshot = new Dictionary<string, string>
    {
        ["isLoggedIn"] = "True",
        ["CurrentLobbyMap"] = "None",
        ["adapterState"] = "Stopping",
    };

    for (var i = 0; i < 500; i++)
    {
        recorder.RecordSceneReadyRaw(snapshot, "map=None; stateBefore=Stopping");
        recorder.RecordSceneReadyRejected(snapshot, "map=None; stateBefore=Stopping");
    }

    var names = recorder.Events.Select(item => item.Name).ToArray();
    return names.Contains("CreateSceneDetour override applied")
        && names.Contains("CurrentLobbyMap reset")
        && names.Contains("CharaSelect title background session cleanup executed")
        && recorder.SceneReadyRawCallCount == 500
        && recorder.SceneReadyRejectedCount == 500
        && recorder.EventCount < TitleBackgroundTransitionDiagnosticRecorder.RingCapacity;
});

Test(192, "title background yaw pitch distance not-observed is safe only after login transition safety", () =>
{
    return !TitleBackgroundTransitionDiagnosticRecorder.IsFinalYawPitchDistanceSafe("not-observed", "unsafe")
        && TitleBackgroundTransitionDiagnosticRecorder.IsFinalYawPitchDistanceSafe("not-observed", "safe")
        && TitleBackgroundTransitionDiagnosticRecorder.IsFinalYawPitchDistanceSafe("observed", "unsafe");
});

Test(194, "title background phase2m diagnostics retain scene-ready frames for post-login summary", () =>
{
    var frames = new[]
    {
        CharacterPlacementFrame(0, TitleBackgroundCharacterPlacementActorMatchKind.Single, visibleHint: true, withCameraDeltas: true),
        CharacterPlacementFrame(30, TitleBackgroundCharacterPlacementActorMatchKind.Single, visibleHint: true, withCameraDeltas: true),
        CharacterPlacementFrame(600, TitleBackgroundCharacterPlacementActorMatchKind.Single, visibleHint: true, withCameraDeltas: true),
    };
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(frames);

    return TitleBackgroundCharacterPlacementDiagnostic.ShouldCaptureFrame(0)
        && TitleBackgroundCharacterPlacementDiagnostic.ShouldCaptureFrame(30)
        && TitleBackgroundCharacterPlacementDiagnostic.ShouldCaptureFrame(600)
        && !TitleBackgroundCharacterPlacementDiagnostic.ShouldCaptureFrame(90)
        && summary.ActorDiagnosticStatus == "observed"
        && summary.ActorVisible == "observed";
});

Test(195, "title background phase2m ambiguous actor candidates prevent write-capable conclusions", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrame(0, TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous, candidateCount: 2),
    ]);

    return summary.ActorDiagnosticStatus == "ambiguous"
        && summary.VisualPlacementSafety == "unsafe";
});

Test(196, "title background phase2m ground height unavailable is unknown not failure", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrame(0, TitleBackgroundCharacterPlacementActorMatchKind.Single, visibleHint: true, withCameraDeltas: true, groundStatus: "unavailable"),
    ]);

    return summary.ActorGroundAligned == "unknown"
        && summary.CameraFramesActor == "observed"
        && summary.VisualPlacementSafety == "unknown";
});

Test(197, "title background phase2m visual placement is unsafe when actor is not observed", () =>
{
    var frame = CharacterPlacementFrame(0, TitleBackgroundCharacterPlacementActorMatchKind.None, candidateCount: 0);
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        frame,
    ]);

    return summary.ActorDiagnosticStatus == "not-observed"
        && summary.ActorVisible == "not-observed"
        && summary.VisualPlacementSafety == "unsafe"
        && frame.ActorCandidateStatus == "none"
        && frame.ActorSource == "objectTable-unavailable-or-not-exposed";
});

Test(198, "title background phase2m single stable candidate is observed but not automatically safe", () =>
{
    var frame = CharacterPlacementFrame(60, TitleBackgroundCharacterPlacementActorMatchKind.Single, visibleHint: true, withCameraDeltas: true);
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary([frame]);

    return summary.ActorDiagnosticStatus == "observed"
        && frame.ActorCandidateStatus == "single"
        && frame.ObjectTableStats.PlayerLikeCount == 1
        && frame.ObjectCandidates.Count == 1
        && summary.VisualPlacementSafety == "unknown";
});

Test(199, "title background phase2m visual placement safety is independent from login transition safety", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var transitionVerdicts = TitleBackgroundTransitionDiagnosticRecorder.BuildVerdicts(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: true,
        historicalLastOverrideApplied: true,
        cleanupReason: "world-login-transition"));
    var placementSummary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrame(0, TitleBackgroundCharacterPlacementActorMatchKind.None, candidateCount: 0),
    ]);

    return transitionVerdicts.LoginTransitionSafety == "safe"
        && placementSummary.VisualPlacementSafety == "unsafe";
});

Test(200, "title background phase2m resolves all zero candidates as stub only", () =>
{
    var candidates = Enumerable.Range(0, 8)
        .Select(index => CharacterPlacementCandidate(index, Vector3.Zero, named: false, drawObject: false, visible: false))
        .ToArray();
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary([CharacterPlacementFrameFromCandidates(candidates)]);

    return summary.Resolution == "stub-only"
        && summary.TransformValidity == "all-zero-transform"
        && summary.StubLikelihood == "high"
        && (summary.IdentityConfidence == "none" || summary.IdentityConfidence == "weak")
        && summary.NextAction == "inspect-native-source";
});

Test(201, "title background phase2m resolves single valid visible draw object candidate", () =>
{
    var candidates = new[]
    {
        CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
    };
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary([CharacterPlacementFrameFromCandidates(candidates, TitleBackgroundCharacterPlacementActorMatchKind.Single)]);

    return summary.Resolution == "single"
        && summary.TransformValidity == "valid-world-transform"
        && summary.IdentityConfidence is "medium" or "strong"
        && summary.BestScore > 0;
});

Test(202, "title background phase2m resolves multiple non-zero candidates as ambiguous", () =>
{
    var candidates = new[]
    {
        CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
        CharacterPlacementCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: true, visible: true),
    };
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary([CharacterPlacementFrameFromCandidates(candidates, TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous)]);

    return summary.Resolution == "ambiguous";
});

Test(203, "title background phase2m ambiguous object table without model evidence is not observed", () =>
{
    var candidates = new[]
    {
        CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
        CharacterPlacementCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
    };
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary([CharacterPlacementFrameFromCandidates(candidates, TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous)]);

    return summary.Resolution == "ambiguous"
        && summary.DrawObjectNonNullCount == 0
        && summary.ModelLikeNonNullCount == 0
        && !summary.BestCandidateStableAcrossFrames
        && summary.ActorDiagnosticStatus != "observed"
        && summary.ActorVisible != "observed"
        && summary.CameraFramesActor != "observed"
        && summary.VisualPlacementSafety != "safe"
        && summary.NextAction == "insufficient-data";
});

Test(204, "title background phase2m post login style object table candidate does not mark actor visible", () =>
{
    var candidates = new[]
    {
        CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
        CharacterPlacementCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
    };
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary([CharacterPlacementFrameFromCandidates(candidates, TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous)]);

    return summary.BestSource == "ObjectTable"
        && summary.ActorVisible != "observed"
        && summary.CameraFramesActor != "observed"
        && summary.ActorDiagnosticStatus != "observed";
});

Test(205, "title background phase2m resolves unavailable source as source missing", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary([CharacterPlacementFrameFromCandidates([])]);
    return summary.Resolution == "source-missing"
        && summary.NextAction == "inspect-native-source";
});

Test(206, "title background phase2m prelogin capture summary survives post-login style summary", () =>
{
    var frames = new[]
    {
        CharacterPlacementFrame(0, TitleBackgroundCharacterPlacementActorMatchKind.Single, visibleHint: true, withCameraDeltas: true),
        CharacterPlacementFrame(1200, TitleBackgroundCharacterPlacementActorMatchKind.Single, visibleHint: true, withCameraDeltas: true),
    };
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(frames);

    return TitleBackgroundCharacterPlacementDiagnostic.ShouldCaptureFrame(1200)
        && summary.ActorDiagnosticStatus == "observed";
});

Test(207, "title background phase2m experimental mode none never writes", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary([CharacterPlacementFrame(0, TitleBackgroundCharacterPlacementActorMatchKind.Single)]);
    return TitleBackgroundCharacterPlacementDiagnostic.EvaluateExperimentalApply(
        TitleBackgroundCharacterPlacementExperimentalApplyMode.None,
        summary,
        sceneGenerationMatches: true,
        isCharaSelectActive: true,
        isLoggedIn: false) == "skip:none-mode";
});

Test(208, "title background phase2m actor placement one shot requires single valid transform", () =>
{
    var ambiguous = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates(
        [
            CharacterPlacementCandidate(1, new Vector3(1f, 2f, 3f), named: true, drawObject: true, visible: true),
            CharacterPlacementCandidate(2, new Vector3(2f, 2f, 3f), named: true, drawObject: true, visible: true),
        ], TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous),
    ]);
    var stub = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([CharacterPlacementCandidate(1, Vector3.Zero, named: false, drawObject: false, visible: false)]),
    ]);

    return TitleBackgroundCharacterPlacementDiagnostic.EvaluateExperimentalApply(
            TitleBackgroundCharacterPlacementExperimentalApplyMode.ActorPlacementOneShot,
            ambiguous,
            sceneGenerationMatches: true,
            isCharaSelectActive: true,
            isLoggedIn: false).StartsWith("skip:resolution-", StringComparison.Ordinal)
        && TitleBackgroundCharacterPlacementDiagnostic.EvaluateExperimentalApply(
            TitleBackgroundCharacterPlacementExperimentalApplyMode.ActorPlacementOneShot,
            stub,
            sceneGenerationMatches: true,
            isCharaSelectActive: true,
            isLoggedIn: false).StartsWith("skip:resolution-", StringComparison.Ordinal);
});

Test(209, "title background phase2n object table all zero is stub only", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates(
        [
            CharacterPlacementCandidate(1, Vector3.Zero, named: false, drawObject: false, visible: false),
            CharacterPlacementCandidate(2, Vector3.Zero, named: false, drawObject: false, visible: false),
        ]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return summary.Resolution == "stub-only"
        && delivery.ObjectTableActorRejected
        && delivery.ObjectTableActorRejectedReason == "zero-transform-stub-only";
});

Test(210, "title background phase2n stub only blocks actor placement one shot", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([CharacterPlacementCandidate(1, Vector3.Zero, named: false, drawObject: false, visible: false)]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return !delivery.ActorPlacementReady
        && delivery.ActorPlacementBlocker == "stub-only-object-table"
        && TitleBackgroundDeliveryDiagnostic.EvaluateExperimentalActorPlacement(
            TitleBackgroundCharacterPlacementExperimentalApplyMode.ActorPlacementOneShot,
            summary,
            sceneGenerationMatches: true,
            isCharaSelectActive: true,
            isLoggedIn: false) == "skip:stub-only-object-table";
});

Test(211, "title background phase2n valid native single is ready", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates(
        [
            CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
        ], TitleBackgroundCharacterPlacementActorMatchKind.Single),
    ]);
    var delivery = Delivery(summary, TitleBackgroundCharacterSelectBackgroundMode.NativePreviewModelSource);

    return delivery.NativePreviewSourceResolution == "found-single"
        && delivery.ActorPlacementReady
        && delivery.NextAction == "try-native-preview-source";
});

Test(212, "title background phase2n multiple valid native candidates are ambiguous", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates(
        [
            CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
            CharacterPlacementCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: true, visible: true),
        ], TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous),
    ]);
    var delivery = Delivery(summary);

    return delivery.NativePreviewSourceResolution == "found-ambiguous"
        && !delivery.ActorPlacementReady;
});

Test(213, "title background phase2n no native source falls back to background only", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return delivery.NativePreviewSourceResolution == "not-found"
        && delivery.DeliveryVerdict == "working-background-only"
        && delivery.NextAction == "use-background-only";
});

Test(214, "title background phase2n custom n4f4 override warns and recommends bright candidate", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return delivery.PresetCompatibility.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
        && delivery.PresetCompatibility.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Dark
        && delivery.Lighting.RecommendedAction == "add-bright-override-candidate"
        && delivery.OverrideCompatibility.BackgroundUsable
        && delivery.PresetCompatibility.RecommendedMode == TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly;
});

Test(215, "title background phase2o custom n4f4 registry entry exists", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet("custom:n4f4", out var candidate)
        && candidate.Id == TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId
        && candidate.DisplayName == "Custom n4f4 override target"
        && candidate.TerritoryPath == "ex3/01_nvt_n4/fld/n4f4/level/n4f4"
        && candidate.TerritoryId == 816
        && candidate.LayerFilterKey == 51;
});

Test(216, "title background phase2o custom n4f4 is background only", () =>
{
    var candidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault();
    return candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
        && candidate.BackgroundUsable
        && !candidate.CharacterExpectedVisible;
});

Test(217, "title background phase2o custom n4f4 is dark", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault().ExpectedBrightness
        == TitleBackgroundCharacterSelectExpectedBrightness.Dark;
});

Test(218, "title background phase2o custom n4f4 is verified in game", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault().VerifiedInGame;
});

Test(219, "title background phase2q old sharlayan observed candidate exists", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet("custom:old-sharlayan-k5t1", out var candidate)
        && candidate.DisplayName == "Old Sharlayan outdoor test"
        && candidate.TerritoryPath == "ex4/03_kld_k5/twn/k5t1/level/k5t1"
        && candidate.TerritoryId == 962
        && candidate.LayerFilterKey == 8
        && candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
        && candidate.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Unknown
        && candidate.BackgroundUsable
        && !candidate.CharacterExpectedVisible
        && !candidate.VerifiedInGame
        && candidate.Source == "registry-observed";
});

Test(220, "title background phase2q old sharlayan does not replace default", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault().Id == "custom:n4f4"
        && TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId == "custom:n4f4";
});

Test(221, "title background phase2q old sharlayan dropdown label is unverified unknown background only", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet("custom:old-sharlayan-k5t1", out var candidate)
        && CandidateLabel(candidate) == "Old Sharlayan outdoor test [Unverified / Unknown / Background-only]";
});

Test(222, "title background phase2o candidate registry keeps selected preset separate", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "built-in-test",
    };
    TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(
        configuration,
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault());

    return configuration.TitleBackgroundSelectedPresetId == string.Empty
        && configuration.TitleBackgroundCharacterSelectOverrideCandidateId == "custom:n4f4";
});

Test(223, "title background phase2o selecting candidate updates override fields only", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "built-in-test",
        TitleBackgroundTerritoryPath = "ffxiv/old/region/level/old",
        TitleBackgroundTerritoryTypeId = 1,
        TitleBackgroundLayoutTerritoryTypeId = 2,
        TitleBackgroundLayoutLayerFilterKey = 3,
    };
    TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(
        configuration,
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault());

    return configuration.TitleBackgroundSelectedPresetId == string.Empty
        && configuration.TitleBackgroundTerritoryPath == "ex3/01_nvt_n4/fld/n4f4/level/n4f4"
        && configuration.TitleBackgroundTerritoryTypeId == 816
        && configuration.TitleBackgroundLayoutTerritoryTypeId == 816
        && configuration.TitleBackgroundLayoutLayerFilterKey == 51;
});

Test(224, "title background phase2o unknown custom override falls back to custom unknown", () =>
{
    var candidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
        string.Empty,
        "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
        1234,
        7);

    return candidate.Id == "custom"
        && candidate.DisplayName == "Custom override target"
        && candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.Unknown
        && candidate.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Unknown
        && !candidate.VerifiedInGame;
});

Test(225, "title background phase2o stale candidate id does not override custom values", () =>
{
    var candidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
        "custom:n4f4",
        "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
        1234,
        7);

    return candidate.Id == "custom"
        && candidate.DisplayName == "Custom override target"
        && candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.Unknown
        && !candidate.VerifiedInGame;
});

Test(226, "title background phase2o no bright candidate reports none", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildBrightLayerCandidateList(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.All) == "none"
        && TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildLightingRecommendedAction(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.All) == "add-bright-override-candidate";
});

Test(227, "title background phase2q old sharlayan delivery exposes observed unverified background only", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(
        summary,
        lastOverrideApplied: true,
        selectedOverrideCandidateId: "custom:old-sharlayan-k5t1",
        overrideTerritoryPath: "ex4/03_kld_k5/twn/k5t1/level/k5t1",
        overrideTerritoryId: 962,
        overrideLayerFilterKey: 8,
        historicalLastOverrideApplied: true,
        historicalLastOverridePath: "ex4/03_kld_k5/twn/k5t1/level/k5t1");

    return delivery.OverrideCandidate.Selected.Id == "custom:old-sharlayan-k5t1"
        && delivery.OverrideCandidate.Selected.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Unknown
        && !delivery.OverrideCandidate.Selected.VerifiedInGame
        && delivery.BackgroundApplication.Observed
        && delivery.BackgroundApplication.LastHistoricalOverrideApplied
        && delivery.BackgroundApplication.LastHistoricalOverridePath == "ex4/03_kld_k5/twn/k5t1/level/k5t1"
        && delivery.BackgroundApplication.CurrentCandidateId == "custom:old-sharlayan-k5t1"
        && delivery.BackgroundApplication.VisualConfirmationRequired
        && delivery.BackgroundApplication.UserVerdict == "background-applied-character-hidden"
        && delivery.BackgroundDeliveryVerdict == "working-background-only-observed"
        && delivery.CandidateHumanName == "Old Sharlayan outdoor test"
        && delivery.CandidateHumanStatus == "Observed / Unverified / Background-only"
        && delivery.UserMessage == "Background was applied as background-only. Selected character model is expected to remain hidden."
        && delivery.UserNextAction == "Run Automatic Check once and paste the copied report.";
});

Test(228, "title background phase2q background application survives transition safety warning", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(
        summary,
        lastOverrideApplied: true,
        transitionSafety: "unsafe",
        sceneReadyAcceptedMultipleTimes: true);

    return delivery.DeliveryVerdict == "unsafe"
        && delivery.BackgroundApplication.Observed
        && delivery.BackgroundDeliveryVerdict == "working-background-only-observed"
        && delivery.Safety.Verdict == "warning"
        && delivery.Safety.Reason == "scene-ready-accepted-multiple-times"
        && delivery.Safety.BlocksBackgroundCandidatePromotion
        && delivery.TransitionSafetyVerdict == "warning-scene-ready-accepted-multiple-times";
});

Test(229, "title background phase2q post login leak not observed without active override or phase2g", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(
        summary,
        lastOverrideApplied: true,
        transitionSafety: "unsafe",
        sceneReadyAcceptedMultipleTimes: true,
        activeAfterLoginDetected: false,
        phase2GAppliedAfterLogin: false);

    return delivery.PostLoginLeakVerdict == "not-observed"
        && delivery.TransitionUserMessage == "No post-login scene override leak observed, but sceneReady was accepted multiple times in this session.";
});

Test(230, "title background phase2q leak blocks candidate promotion", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(
        summary,
        lastOverrideApplied: true,
        activeAfterLoginDetected: true);

    return delivery.PostLoginLeakVerdict == "observed"
        && delivery.Safety.Verdict == "unsafe"
        && delivery.Safety.BlocksBackgroundCandidatePromotion;
});

Test(231, "title background phase2p manual candidate disabled is not available", () =>
{
    var slot = ManualSlot(enabled: false);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);

    return !slot.Valid
        && slot.ValidationError == "disabled"
        && candidates.All(candidate => candidate.Id != "manual:slot1");
});

Test(232, "title background phase2p manual candidate invalid path is rejected", () =>
{
    var slot = ManualSlot(path: "bad/path", territoryId: 900, enabled: true);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);

    return !slot.Valid
        && slot.ValidationError == "territory-path-invalid"
        && candidates.All(candidate => candidate.Id != "manual:slot1");
});

Test(233, "title background phase2p manual candidate territory id zero is rejected", () =>
{
    var slot = ManualSlot(territoryId: 0, enabled: true);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);

    return !slot.Valid
        && slot.ValidationError == "territory-id-zero"
        && candidates.All(candidate => candidate.Id != "manual:slot1");
});

Test(234, "title background phase2p valid manual candidate is available", () =>
{
    var slot = ManualSlot(enabled: true);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);
    var manual = candidates.FirstOrDefault(candidate => candidate.Id == "manual:slot1");

    return slot.Valid
        && manual.Id == "manual:slot1"
        && manual.Source == "manual"
        && manual.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly;
});

Test(235, "title background phase2p manual candidate is never verified by default", () =>
{
    var slot = ManualSlot(enabled: true);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);
    var manual = candidates.First(candidate => candidate.Id == "manual:slot1");

    return !manual.VerifiedInGame
        && manual.Warning.Contains("unverified", StringComparison.OrdinalIgnoreCase);
});

Test(236, "title background phase2p manual bright candidate contributes to bright list", () =>
{
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates(
        [ManualSlot(enabled: true, brightness: TitleBackgroundCharacterSelectExpectedBrightness.Bright)]);

    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildBrightLayerCandidateList(candidates) == "manual:slot1";
});

Test(237, "title background phase2p manual bright candidate recommends verification", () =>
{
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates(
        [ManualSlot(enabled: true, brightness: TitleBackgroundCharacterSelectExpectedBrightness.Bright)]);

    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildLightingRecommendedAction(candidates) == "verify-manual-bright-candidate";
});

Test(238, "title background phase2p selecting manual candidate updates override fields", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "built-in-test",
    };
    var slot = ManualSlot(enabled: true, brightness: TitleBackgroundCharacterSelectExpectedBrightness.Bright);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);
    var manual = candidates.First(candidate => candidate.Id == "manual:slot1");

    TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(configuration, manual);

    return configuration.TitleBackgroundSelectedPresetId == string.Empty
        && configuration.TitleBackgroundCharacterSelectOverrideCandidateId == "manual:slot1"
        && configuration.TitleBackgroundTerritoryPath == manual.TerritoryPath
        && configuration.TitleBackgroundTerritoryTypeId == manual.TerritoryId
        && configuration.TitleBackgroundLayoutTerritoryTypeId == manual.TerritoryId
        && configuration.TitleBackgroundLayoutLayerFilterKey == manual.LayerFilterKey;
});

Test(239, "title background phase2p delivery selects valid manual candidate", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var slot = ManualSlot(enabled: true, brightness: TitleBackgroundCharacterSelectExpectedBrightness.Bright);
    var delivery = Delivery(
        summary,
        lastOverrideApplied: true,
        selectedOverrideCandidateId: "manual:slot1",
        overrideTerritoryPath: slot.TerritoryPath,
        overrideTerritoryId: slot.TerritoryId,
        overrideLayerFilterKey: slot.LayerFilterKey,
        manualCandidateSlots: [slot]);

    return delivery.OverrideCandidate.Selected.Id == "manual:slot1"
        && delivery.OverrideCandidate.Selected.Source == "manual"
        && !delivery.OverrideCandidate.Selected.VerifiedInGame
        && delivery.OverrideCandidate.ManualSlots[0].Valid
        && delivery.Lighting.BrightLayerCandidates == "manual:slot1"
        && delivery.Lighting.RecommendedAction == "verify-manual-bright-candidate";
});

Test(240, "title background phase2p invalid manual candidate falls back safely", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var slot = ManualSlot(path: "bad/path", territoryId: 900, enabled: true);
    var delivery = Delivery(
        summary,
        lastOverrideApplied: true,
        selectedOverrideCandidateId: "manual:slot1",
        overrideTerritoryPath: "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
        overrideTerritoryId: 816,
        overrideLayerFilterKey: 51,
        manualCandidateSlots: [slot]);

    return delivery.OverrideCandidate.Selected.Id == "custom:n4f4"
        && delivery.OverrideCandidate.ManualSlots[0].ValidationError == "territory-path-invalid"
        && delivery.MvpStatus == "complete-background-only";
});

Test(241, "title background phase2o bright candidate list reports candidate id", () =>
{
    var candidates = new[]
    {
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault(),
        TestBrightCandidate(),
    };

    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildBrightLayerCandidateList(candidates) == "custom:test-bright";
});

Test(242, "title background phase2o bright candidate recommends trying custom target", () =>
{
    var candidates = new[]
    {
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault(),
        TestBrightCandidate(),
    };

    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildLightingRecommendedAction(candidates) == "try-bright-custom-target";
});

Test(243, "title background phase2o unverified bright candidate does not claim verified", () =>
{
    var candidate = TestBrightCandidate();
    return candidate.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Bright
        && !candidate.VerifiedInGame;
});

Test(244, "title background phase2o delivery exposes selected override candidate", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return delivery.OverrideCandidate.Selected.Id == "custom:n4f4"
        && delivery.OverrideCandidate.Selected.VerifiedInGame
        && delivery.OverrideCandidate.Available.Count == 4
        && delivery.OverrideCandidate.Available[0].Id == "custom:n4f4"
        && delivery.OverrideCandidate.Available.Any(c => c.Id == "custom:fru-clear-stage");
});

Test(245, "title background delivery diagnostics emit old and new key prefixes", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);
    var lines = TitleBackgroundDeliveryDiagnostic.BuildLineList(delivery);

    return lines.Any(line => line == $"phase2N.mvpStatus={delivery.MvpStatus}")
        && lines.Any(line => line == $"delivery.mvpStatus={delivery.MvpStatus}")
        && lines.Any(line => line == $"phase2N.nextAction={delivery.NextAction}")
        && lines.Any(line => line == $"delivery.nextAction={delivery.NextAction}");
});

Test(246, "title background character placement diagnostics keep old and new key prefixes", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));

    return serviceText.Contains("phase2M.", StringComparison.Ordinal)
        && serviceText.Contains("characterPlacement.", StringComparison.Ordinal)
        && serviceText.Contains("DiagnosticReportBuilder.AddPrefixAliasLines(lines, aliasStartIndex, \"phase2M.\", \"characterPlacement.\")", StringComparison.Ordinal);
});

Test(247, "title background phase2q docs use the automatic report after login", () =>
{
    var root = FindRepositoryRoot();
    var text = File.ReadAllText(Path.Combine(root, "docs", "title-background-character-select-bright-candidates.md"));

    return !text.Contains("/xmutbgdiag", StringComparison.Ordinal)
        && text.Contains("automatic report", StringComparison.Ordinal)
        && text.Contains("Capture a screenshot in Character Select", StringComparison.Ordinal);
});

Test(248, "title background phase2q implementation avoids prohibited write paths", () =>
{
    var root = FindRepositoryRoot();
    var changedFiles = new[]
    {
        Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundCharacterSelectOverrideCandidateRegistry.cs"),
        Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundDeliveryDiagnostic.cs"),
    }.Concat(Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components"), "SettingsTab*.cs")).ToArray();
    var titleBackgroundText = string.Join(
        "\n",
        changedFiles.Select(File.ReadAllText));
    var docs = File.ReadAllText(Path.Combine(root, "docs", "title-background-character-select-bright-candidates.md"));

    return !titleBackgroundText.Contains("SceneCamera.Position", StringComparison.Ordinal)
        && !titleBackgroundText.Contains("SceneCamera.LookAtVector", StringComparison.Ordinal)
        && !titleBackgroundText.Contains("SceneCamera.FoV", StringComparison.Ordinal)
        && !titleBackgroundText.Contains("Framework.Update", StringComparison.Ordinal)
        && !titleBackgroundText.Contains(".Position =", StringComparison.Ordinal)
        && !titleBackgroundText.Contains(".Rotation =", StringComparison.Ordinal)
        && !titleBackgroundText.Contains("light write", StringComparison.OrdinalIgnoreCase)
        && !titleBackgroundText.Contains("environment write", StringComparison.OrdinalIgnoreCase)
        && !docs.Contains("automatic map cycling", StringComparison.OrdinalIgnoreCase)
        && !docs.Contains("n4f4 " + "preset", StringComparison.OrdinalIgnoreCase);
});

Test(249, "title background phase2o docs and ui avoid n4f4 synthetic preset wording", () =>
{
    var root = FindRepositoryRoot();
    var paths = new[]
    {
        Path.Combine(root, "docs", "title-background-character-select-bright-candidates.md"),
        Path.Combine(root, "docs", "title-background-character-select-delivery-notes.md"),
        Path.Combine(root, "docs", "title-background-character-select-phase2n-plan.md"),
    }.Concat(Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components"), "SettingsTab*.cs")).ToArray();

    return paths.All(path => File.Exists(path)
        && !File.ReadAllText(path).Contains("n4f4 " + "preset", StringComparison.OrdinalIgnoreCase));
});

Test(250, "title background phase2n stub only never reports character observed", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([CharacterPlacementCandidate(1, Vector3.Zero, named: false, drawObject: false, visible: true)]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return delivery.ObjectTableActorRejected
        && delivery.NativePreviewSourceResolution == "not-found"
        && delivery.CharacterVisibilityObserved != "observed"
        && delivery.CharacterVisibilityObserved == "not-observed"
        && delivery.CharacterVisibilityBlocker == "stub-only-object-table";
});

Test(251, "title background phase2n native source not found never reports character observed", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return delivery.NativePreviewSourceResolution == "not-found"
        && delivery.CharacterVisibilityObserved != "observed";
});

Test(252, "title background phase2n background only keeps character expected hidden", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return delivery.DeliveryVerdict == "working-background-only"
        && !delivery.PresetCompatibility.CharacterExpectedVisible
        && !delivery.OverrideCompatibility.CharacterExpectedVisible;
});

Test(253, "title background phase2n custom override source keeps selected preset none", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(summary, selectedPresetId: string.Empty, lastOverrideApplied: true);

    return delivery.OverrideCompatibility.Source == "custom-override"
        && delivery.OverrideCompatibility.SelectedPresetId == "none"
        && delivery.OverrideCompatibility.CurrentOverrideId == "custom:n4f4";
});

Test(254, "title background phase2n custom n4f4 synthetic entry is not selected preset", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(summary, selectedPresetId: string.Empty, lastOverrideApplied: true);

    return delivery.PresetCompatibility.CurrentPresetId == "custom:n4f4"
        && delivery.OverrideCompatibility.Id == "custom:n4f4"
        && delivery.OverrideCompatibility.Source == "custom-override"
        && delivery.OverrideCompatibility.SelectedPresetId != "custom:n4f4";
});

Test(255, "title background phase2n custom n4f4 dark lighting has recommendation", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return delivery.OverrideCompatibility.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Dark
        && delivery.Lighting.CurrentLayerFilterKey == 51
        && delivery.Lighting.LayerBrightnessKnown
        && !string.IsNullOrEmpty(delivery.Lighting.RecommendedAction)
        && delivery.Lighting.RecommendedAction != "none";
});

Test(256, "title background phase2n background only safe compatibility delivers background only", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return delivery.PresetCompatibility.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
        && delivery.PresetCompatibility.SafeToUse
        && delivery.DeliveryVerdict == "working-background-only";
});

Test(257, "title background phase2n post login current object table is ignored", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates(
        [
            CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
            CharacterPlacementCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
        ], TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true, currentObjectTableValidForCharaSelect: false);

    return delivery.NativePreviewSourceCurrentObjectTableIgnored
        && delivery.NativePreviewSourceCurrentObjectTableIgnoredReason == "post-login-world-object-table-not-valid-for-chara-select"
        && delivery.NativePreviewSourceResolution == "not-verifiable-post-login"
        && delivery.CharacterVisibilityBlocker == "post-login-object-table-not-valid"
        && delivery.ObjectTableActorRejected
        && !delivery.ActorPlacementReady
        && delivery.DeliveryVerdict == "working-background-only"
        && delivery.ObjectTableActorRejectedReason == "post-login-world-object-table-not-valid-for-chara-select";
});

Test(258, "title background phase2n background only mvp is complete with known limitation", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates(
        [
            CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
            CharacterPlacementCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
        ], TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true, currentObjectTableValidForCharaSelect: false);

    return delivery.NativePreviewSourceCurrentObjectTableIgnored
        && delivery.NativePreviewSourceResolution == "not-verifiable-post-login"
        && delivery.ObjectTableActorRejected
        && !delivery.ActorPlacementReady
        && delivery.DeliveryVerdict == "working-background-only"
        && delivery.MvpStatus == "complete-background-only"
        && delivery.MvpBlockingIssue == "none"
        && delivery.MvpKnownLimitation == "selected-character-model-hidden";
});

Test(259, "title background phase2n post login ambiguous object table never readies actor placement", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates(
        [
            CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
            CharacterPlacementCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
        ], TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true, currentObjectTableValidForCharaSelect: false);

    return !delivery.ActorPlacementReady
        && delivery.ActorPlacementBlocker == "post-login-world-object-table-not-valid-for-chara-select"
        && delivery.CharacterVisibilityObserved != "observed";
});

Test(260, "title background phase2n draw object absent unstable ambiguous is not observed", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates(
        [
            CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
            CharacterPlacementCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
        ], TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return summary.DrawObjectNonNullCount == 0
        && summary.ModelLikeNonNullCount == 0
        && !summary.BestCandidateStableAcrossFrames
        && delivery.CharacterVisibilityObserved == "not-verifiable"
        && !delivery.ActorPlacementReady;
});

Test(261, "title background phase2n source local counts do not leak object table counts", () =>
{
    var sourceDiscovery = new[]
    {
        new TitleBackgroundCharacterPlacementSourceDiscovery("ObjectTable", true, 16, 16, string.Empty, 16, 0, 0),
        new TitleBackgroundCharacterPlacementSourceDiscovery("PlayerObjects", true, 0, 0, string.Empty, 0, 0, 0),
        new TitleBackgroundCharacterPlacementSourceDiscovery("CharacterManagerObjects", true, 0, 0, string.Empty, 0, 0, 0),
    };
    var delivery = DeliveryFromRaw(
        phase2MResolution: "ambiguous",
        phase2MTransformValidity: "valid-world-transform",
        phase2MActorVisible: "ambiguous",
        zeroPositionCandidateCount: 0,
        nonZeroPositionCandidateCount: 16,
        drawObjectNonNullCount: 0,
        modelLikeNonNullCount: 0,
        sourceDiscovery,
        lastOverrideApplied: true);
    var playerObjects = delivery.NativePreviewSources.First(source => source.Name == "PlayerObjects");
    var characterManagerObjects = delivery.NativePreviewSources.First(source => source.Name == "CharacterManagerObjects");

    return playerObjects.CandidateCount == 0
        && playerObjects.NonZeroTransformCount == 0
        && characterManagerObjects.CandidateCount == 0
        && characterManagerObjects.NonZeroTransformCount == 0;
});

Test(262, "title background phase2n object table ambiguous candidate never enables one shot readiness", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates(
        [
            CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
            CharacterPlacementCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: true, visible: true),
        ], TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true);

    return summary.Resolution == "ambiguous"
        && !delivery.ActorPlacementReady
        && TitleBackgroundDeliveryDiagnostic.EvaluateExperimentalActorPlacement(
            TitleBackgroundCharacterPlacementExperimentalApplyMode.ActorPlacementOneShot,
            summary,
            sceneGenerationMatches: true,
            isCharaSelectActive: true,
            isLoggedIn: false).StartsWith("skip:resolution-", StringComparison.Ordinal);
});

Test(263, "title background phase2n default mode does not enable actor or camera direct writes", () =>
{
    return TitleBackgroundDeliveryDiagnostic.IsMutationMode(TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly)
        && !TitleBackgroundDeliveryDiagnostic.IsMutationMode(TitleBackgroundCharacterSelectBackgroundMode.Disabled)
        && !TitleBackgroundDeliveryDiagnostic.IsMutationMode(TitleBackgroundCharacterSelectBackgroundMode.DiagnosticsOnly);
});

Test(264, "title background phase2n login transition unsafe stops delivery", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(summary, lastOverrideApplied: true, transitionSafety: "unsafe");

    return delivery.DeliveryVerdict == "unsafe"
        && delivery.NextAction == "unsafe-stop";
});

Test(265, "title background phase2n scene generation mismatch remains no-op for actor placement", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates(
        [
            CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
        ], TitleBackgroundCharacterPlacementActorMatchKind.Single),
    ]);

    return TitleBackgroundDeliveryDiagnostic.EvaluateExperimentalActorPlacement(
        TitleBackgroundCharacterPlacementExperimentalApplyMode.ActorPlacementOneShot,
        summary,
        sceneGenerationMatches: false,
        isCharaSelectActive: true,
        isLoggedIn: false) == "skip:scene-generation-mismatch";
});

Test(266, "title background phase2m next action selects visibility probe for valid invisible actor", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates(
        [
            CharacterPlacementCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: false),
        ], TitleBackgroundCharacterPlacementActorMatchKind.Single),
    ]);

    return summary.NextAction == "enable-visibility-probe" || summary.NextAction == "actor-placement-preview";
});

Test(269, "title background chara select camera LookAtY is consumed once per scene generation", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.MarkRuntimeCameraStateRestored();

    var firstShouldApply = adapter.ShouldApplyLookAtY();
    adapter.MarkLookAtYApplied();
    var secondShouldApply = adapter.ShouldApplyLookAtY();

    return firstShouldApply
        && !secondShouldApply
        && !adapter.RuntimeState.ShouldSetLookAtY
        && adapter.LastLookAtYAppliedSceneGeneration == adapter.RuntimeState.SceneGeneration;
});

Test(270, "title background chara select camera LookAtY remains pending until apply success is marked", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.MarkRuntimeCameraStateRestored();

    return adapter.ShouldApplyLookAtY()
        && adapter.ShouldApplyLookAtY()
        && adapter.RuntimeState.ShouldSetLookAtY
        && adapter.LastLookAtYAppliedSceneGeneration == 0;
});

Test(271, "title background chara select camera does not apply curve or LookAtY after stop requested", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.MarkRuntimeCameraStateRestored();
    adapter.NotifyLobbyUpdate(GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.Title);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.Stopping
        && !adapter.ShouldApplyCurve()
        && !adapter.ShouldApplyLookAtY();
});

Test(272, "title background chara select camera scene-ready signal handles only armed or loading chara select", () =>
{
    return TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: false,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.Armed,
            GameLobbyType.CharaSelect)
        && TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: false,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoading,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: false,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: false,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.Active,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: true,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoading,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: false,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoading,
            GameLobbyType.Title);
});

Test(273, "title background chara select camera does not stop while waiting for scene-ready signal", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.Title);
    adapter.NotifyLobbyUpdate(GameLobbyType.None);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.SceneLoading
        && adapter.LastEvent == TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoadStarted.ToString()
        && !TitleBackgroundCharaSelectCameraLogic.ShouldStopOnLobbyUpdate(
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoading,
            GameLobbyType.Title);
});

Test(274, "title background chara select camera stops after active scene leaves chara select", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.Title);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.Stopping
        && adapter.LastEvent == TitleBackgroundCharaSelectCameraAdapterEvent.StopRequested.ToString()
        && TitleBackgroundCharaSelectCameraLogic.ShouldStopOnLobbyUpdate(
            TitleBackgroundCharaSelectCameraAdapterState.Active,
            GameLobbyType.Title);
});

Test(275, "title background chara select camera adapter ignores runtime notifications while inactive", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.NotifySceneOverrideApplied(GameLobbyType.CharaSelect);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.CharaSelect);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.Inactive
        && adapter.RuntimeState.SceneGeneration == 0
        && adapter.LastEvent == "not-run";
});

Test(276, "title background chara select camera adapter stays armed until chara select scene starts", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifyLobbyUpdate(GameLobbyType.Title);
    adapter.NotifyLobbyUpdate(GameLobbyType.None);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.Armed;
});

Test(277, "title background chara select camera adapter arms only for chara select camera adaptation", () =>
{
    return TitleBackgroundCharaSelectCameraLogic.ShouldArmAdapter(
            overrideEnabled: true,
            cameraAdaptationEnabled: true,
            runtimeMode: TitleBackgroundRuntimeMode.CharaSelectOnly)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldArmAdapter(
            overrideEnabled: true,
            cameraAdaptationEnabled: true,
            runtimeMode: TitleBackgroundRuntimeMode.HookProbe)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldArmAdapter(
            overrideEnabled: true,
            cameraAdaptationEnabled: false,
            runtimeMode: TitleBackgroundRuntimeMode.CharaSelectOnly);
});

Test(278, "title background fix on invocation mode is explicit", () =>
{
    return TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(overrideApplied: false) == "passthrough"
        && TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(overrideApplied: true) == "override-applied";
});

Test(279, "title background fix on hook creation is disabled for phase one", () =>
{
    return Enum.GetValues<TitleBackgroundRuntimeMode>()
        .All(mode =>
            !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(mode, overrideEnabled: false, cameraOverrideEnabled: false)
            && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(mode, overrideEnabled: true, cameraOverrideEnabled: false)
            && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(mode, overrideEnabled: true, cameraOverrideEnabled: true));
});

Test(280, "title background camera math accepts finite vectors only", () =>
{
    return TitleBackgroundCameraMath.IsFiniteVector(new Vector3(1f, 2f, 3f))
        && !TitleBackgroundCameraMath.IsFiniteVector(new Vector3(float.NaN, 2f, 3f))
        && !TitleBackgroundCameraMath.IsFiniteVector(new Vector3(1f, float.PositiveInfinity, 3f));
});

Test(281, "title background camera math calculates nullable deltas", () =>
{
    return TitleBackgroundCameraMath.CalculateVectorDelta(
            new Vector3(5f, 3f, 1f),
            new Vector3(2f, 1f, 4f)) == new Vector3(3f, 2f, -3f)
        && TitleBackgroundCameraMath.CalculateVectorDelta(null, new Vector3(1f, 1f, 1f)) == null
        && TitleBackgroundCameraMath.CalculateFloatDelta(5f, 2.5f) == 2.5f
        && TitleBackgroundCameraMath.CalculateFloatDelta(null, 2.5f) == null;
});

Test(282, "title background camera probe detects reflected then overwritten camera y", () =>
{
    var result = TitleBackgroundCameraProbeReport.Evaluate(new TitleBackgroundCameraProbeReportInput(
        Armed: true,
        BaselineCamera: new Vector3(0f, 10f, 0f),
        BaselineFocus: new Vector3(0f, 20f, 0f),
        ProbeCamera: new Vector3(0f, 60f, 0f),
        ProbeFocus: new Vector3(0f, -30f, 0f),
        LastAppliedCamera: new Vector3(0f, 60f, 0f),
        PostFixOnSceneCameraPosition: new Vector3(0f, 60.2f, 0f),
        CurrentSceneCameraPosition: new Vector3(0f, 92f, 0f),
        LastAppliedFocus: new Vector3(0f, -30f, 0f),
        PostFixOnLookAtVector: new Vector3(0f, -30.2f, 0f),
        CurrentLookAtVector: new Vector3(0f, -30.4f, 0f)));

    return result.CameraYFixOnReflection == TitleBackgroundCameraProbeVerdict.Reflected
        && result.CameraYPostFixOnStability == TitleBackgroundCameraProbeVerdict.PossiblyOverwritten
        && result.FocusYFixOnReflection == TitleBackgroundCameraProbeVerdict.Reflected
        && result.FocusYPostFixOnStability == TitleBackgroundCameraProbeVerdict.Stable
        && result.LikelyConclusion.Contains("overwritten later", StringComparison.Ordinal);
});

Test(283, "title background camera probe detects focus reflection with missing camera y reflection", () =>
{
    var result = TitleBackgroundCameraProbeReport.Evaluate(new TitleBackgroundCameraProbeReportInput(
        Armed: true,
        BaselineCamera: new Vector3(0f, 10f, 0f),
        BaselineFocus: new Vector3(0f, 20f, 0f),
        ProbeCamera: new Vector3(0f, 60f, 0f),
        ProbeFocus: new Vector3(0f, -30f, 0f),
        LastAppliedCamera: new Vector3(0f, 60f, 0f),
        PostFixOnSceneCameraPosition: new Vector3(0f, 10f, 0f),
        CurrentSceneCameraPosition: new Vector3(0f, 10f, 0f),
        LastAppliedFocus: new Vector3(0f, -30f, 0f),
        PostFixOnLookAtVector: new Vector3(0f, -30f, 0f),
        CurrentLookAtVector: new Vector3(0f, -30f, 0f)));

    return result.CameraYFixOnReflection == TitleBackgroundCameraProbeVerdict.NotReflected
        && result.FocusYFixOnReflection == TitleBackgroundCameraProbeVerdict.Reflected
        && result.LikelyConclusion.Contains("FocusY reflects correctly", StringComparison.Ordinal);
});

Test(284, "title background camera probe does not evaluate when unarmed", () =>
{
    var result = TitleBackgroundCameraProbeReport.Evaluate(new TitleBackgroundCameraProbeReportInput(
        Armed: false,
        BaselineCamera: default,
        BaselineFocus: default,
        ProbeCamera: default,
        ProbeFocus: default,
        LastAppliedCamera: new Vector3(0f, 10f, 0f),
        PostFixOnSceneCameraPosition: new Vector3(0f, 10f, 0f),
        CurrentSceneCameraPosition: new Vector3(0f, 10f, 0f),
        LastAppliedFocus: new Vector3(0f, 20f, 0f),
        PostFixOnLookAtVector: new Vector3(0f, 20f, 0f),
        CurrentLookAtVector: new Vector3(0f, 20f, 0f)));

    return result.CameraYFixOnReflection == TitleBackgroundCameraProbeVerdict.Inconclusive
        && result.FocusYFixOnReflection == TitleBackgroundCameraProbeVerdict.Inconclusive
        && result.LikelyConclusion.Contains("arm the probe first", StringComparison.Ordinal);
});

Test(285, "title background camera probe timeline detects first overwrite frames", () =>
{
    var samples = new[]
    {
        new TitleBackgroundCameraProbeTimelineSample(0, new Vector3(0f, 60f, 0f), new Vector3(0f, -30f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(1, new Vector3(0f, 60.5f, 0f), new Vector3(0f, -29.5f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(2, new Vector3(0f, 40f, 0f), new Vector3(0f, -29f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(4, new Vector3(0f, 20f, 0f), new Vector3(0f, -18f, 0f)),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzeTimeline(
        samples,
        new Vector3(0f, 60f, 0f),
        new Vector3(0f, -30f, 0f));

    return result.CameraOverwriteFirstObservedFrame == 2
        && result.FocusOverwriteFirstObservedFrame == 4
        && result.CameraOverwritePattern == TitleBackgroundCameraOverwritePattern.Immediate
        && result.FocusOverwritePattern == TitleBackgroundCameraOverwritePattern.Gradual;
});

Test(286, "title background camera probe timeline classifies late overwrite", () =>
{
    var samples = new[]
    {
        new TitleBackgroundCameraProbeTimelineSample(0, new Vector3(0f, 60f, 0f), new Vector3(0f, -30f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(8, new Vector3(0f, 59f, 0f), new Vector3(0f, -30f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(16, new Vector3(0f, 40f, 0f), new Vector3(0f, -30f, 0f)),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzeTimeline(
        samples,
        new Vector3(0f, 60f, 0f),
        new Vector3(0f, -30f, 0f));

    return result.CameraOverwriteFirstObservedFrame == 16
        && result.FocusOverwriteFirstObservedFrame == null
        && result.CameraOverwritePattern == TitleBackgroundCameraOverwritePattern.Late
        && result.FocusOverwritePattern == TitleBackgroundCameraOverwritePattern.Inconclusive;
});

Test(287, "title background camera probe timeline summarizes coincident events", () =>
{
    var samples = new[]
    {
        new TitleBackgroundCameraProbeTimelineSample(0, new Vector3(0f, 60f, 0f), new Vector3(0f, -30f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(1, new Vector3(0f, 60f, 0f), new Vector3(0f, -30f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(2, new Vector3(0f, 40f, 0f), new Vector3(0f, -26f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(4, new Vector3(0f, 39f, 0f), new Vector3(0f, -18f, 0f)),
    };
    var events = new Dictionary<int, TitleBackgroundCameraProbeTimelineEventCounts>
    {
        [0] = new(1, 0, 1, 1),
        [2] = new(0, 1, 0, 0),
        [4] = new(0, 2, 0, 0),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzeTimeline(
        samples,
        new Vector3(0f, 60f, 0f),
        new Vector3(0f, -30f, 0f));
    var cameraEvents = TitleBackgroundCameraProbeReport.DescribeCoincidentEvents(
        result.CameraOverwriteFirstObservedFrame,
        events.GetValueOrDefault(result.CameraOverwriteFirstObservedFrame ?? -1));
    var focusDriftEvents = TitleBackgroundCameraProbeReport.DescribeFocusDriftEvents(
        samples,
        (startFrame, endFrame) => events
            .Where(entry => entry.Key >= startFrame && entry.Key <= endFrame)
            .Aggregate(
                new TitleBackgroundCameraProbeTimelineEventCounts(),
                (total, entry) => new TitleBackgroundCameraProbeTimelineEventCounts(
                    total.FixOnCalls + entry.Value.FixOnCalls,
                    total.LobbyUpdateCalls + entry.Value.LobbyUpdateCalls,
                    total.LoadLobbySceneCalls + entry.Value.LoadLobbySceneCalls,
                    total.CreateSceneCalls + entry.Value.CreateSceneCalls)),
        new Vector3(0f, -30f, 0f));

    return result.CameraOverwriteFirstObservedFrame == 2
        && cameraEvents == "fixOn=0,lobbyUpdate=1,loadLobbyScene=0,createScene=0"
        && focusDriftEvents == "fixOn=0,lobbyUpdate=2,loadLobbyScene=0,createScene=0";
});

Test(288, "title background phase2d analysis detects late transform and distance overwrite", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2DTimelineSample(0, new Vector3(0f, 0.8f, 3.3f), new Vector3(0f, 0.8f, 0f), 3.3f, 0.1f, 0.2f),
        new TitleBackgroundPhase2DTimelineSample(60, new Vector3(0f, 0.8f, 3.3f), new Vector3(0f, 0.8f, 0f), 3.3f, 0.1f, 0.2f),
        new TitleBackgroundPhase2DTimelineSample(300, new Vector3(-48.5f, 15.8f, 9.1f), new Vector3(-52.7f, 14.6f, 9.4f), 4.25f, 0.5f, 0.4f),
        new TitleBackgroundPhase2DTimelineSample(450, new Vector3(-48.5f, 15.8f, 9.1f), new Vector3(-52.7f, 14.6f, 9.4f), 4.25f, 0.5f, 0.4f),
        new TitleBackgroundPhase2DTimelineSample(600, new Vector3(-48.5f, 15.8f, 9.1f), new Vector3(-52.7f, 14.6f, 9.4f), 4.25f, 0.5f, 0.4f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2D(samples, restoredDistance: 3.3f);

    return result.SceneTransformShiftObserved == "observed"
        && result.DistanceEventuallyOverwritten == "observed"
        && result.FinalCameraStabilizationObserved == "observed";
});

Test(289, "title background phase2d analysis reports unstabilized late camera", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2DTimelineSample(300, new Vector3(10f, 10f, 10f), new Vector3(0f, 0f, 0f), 3.3f, 0.1f, 0.2f),
        new TitleBackgroundPhase2DTimelineSample(450, new Vector3(12f, 10f, 10f), new Vector3(0f, 2f, 0f), 3.4f, 0.2f, 0.2f),
        new TitleBackgroundPhase2DTimelineSample(600, new Vector3(14f, 10f, 10f), new Vector3(0f, 4f, 0f), 3.5f, 0.3f, 0.2f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2D(samples, restoredDistance: 3.3f);

    return result.FinalCameraStabilizationObserved == "not-observed"
        && result.DistanceEventuallyOverwritten == "observed";
});

Test(290, "title background phase2e detects native return matching active look at y", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2EProbeSample(1, 0, 10f, 9.8f),
        new TitleBackgroundPhase2EProbeSample(2, 1, 14.6f, 14.6f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2E(samples, finalStableLookAtY: 14.6f);
    return result.NativeReturnMatchesActiveLookAtY == "observed"
        && result.NativeReturnMatchesFinalStableLookAtY == "observed"
        && result.ComparedCallCount == 2;
});

Test(291, "title background phase2f accepts early stable curve timeline for one shot write", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2FCurveTimelineSample(0, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(16, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(300, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(450, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(600, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2F(samples);
    return result.CurveGeneratedEarly == "observed"
        && result.CurveStableByFinalWindow == "observed"
        && result.CurveRegeneratedAfterEarlyFrame == "not-observed"
        && result.OneShotWriteViability == "plausible"
        && result.CurvePointValuesChangedAfterEarlyFrame == "not-observed"
        && result.CameraCurveEnabledTransitionObserved == "not-observed"
        && result.OneShotCurvePointWriteValueStability == "observed"
        && result.OneShotCurvePointWriteTimingRisk == "not-observed";
});

Test(292, "title background phase2f flags late curve regeneration as one shot risk", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2FCurveTimelineSample(0, true, true, 1.1f, 1.1f, 3.3f, 0.7f, 5.5f, 0.5f),
        new TitleBackgroundPhase2FCurveTimelineSample(60, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(300, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(450, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(600, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2F(samples);
    return result.CurveGeneratedEarly == "observed"
        && result.CurveStableByFinalWindow == "observed"
        && result.CurveRegeneratedAfterEarlyFrame == "observed"
        && result.LastChangedFrame == 60
        && result.LastPointValueChangedFrame == 60
        && result.OneShotWriteViability == "risky";
});

Test(293, "title background phase2f separates camera curve enabled transition from point value changes", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2FCurveTimelineSample(0, true, false, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(16, true, false, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(30, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(300, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(450, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(600, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2F(samples);
    return result.CurvePointValuesChangedAfterEarlyFrame == "not-observed"
        && result.CurveRegeneratedAfterEarlyFrame == "not-observed"
        && result.CameraCurveEnabledTransitionObserved == "observed"
        && result.CameraCurveEnabledFirstObservedFrame == 30
        && result.OneShotCurvePointWriteValueStability == "observed"
        && result.OneShotCurvePointWriteTimingRisk == "observed"
        && result.OneShotWriteViability == "plausible";
});

Test(294, "title background capture preset builder keeps existing fov when unavailable", () =>
{
    var existing = new TitleBackgroundPreset
    {
        TerritoryPath = "ffxiv/old/region/level/old",
        FovY = 1.5f,
        CharacterPosition = new Vector3(9f, 9f, 9f),
        CharacterRotation = 0.25f,
    }.Normalize();
    var draft = new TitleBackgroundCameraCaptureDraft(
        "bg/ffxiv/area/region/level/sample.lvb",
        777,
        new Vector3(1f, 2f, 3f),
        new Vector3(4f, 5f, 6f),
        null,
        777,
        null,
        null,
        null);

    return TitleBackgroundCameraCapturePresetBuilder.TryBuild(
            draft,
            existing,
            out var preset,
            out var fovState,
            out _,
            out _)
        && preset.TerritoryPath == "ffxiv/area/region/level/sample"
        && preset.TerritoryTypeId == 777
        && preset.FovY == 1.5f
        && fovState == TitleBackgroundCaptureValueState.KeptExisting
        && preset.CharacterPosition == new Vector3(9f, 9f, 9f)
        && Math.Abs(preset.CharacterRotation - 0.25f) < 0.0001f;
});

Test(295, "title background capture preset builder accepts expansion pack bg path", () =>
{
    var existing = new TitleBackgroundPreset { FovY = 1f }.Normalize();
    var draft = new TitleBackgroundCameraCaptureDraft(
        "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
        1234,
        new Vector3(1f, 2f, 3f),
        new Vector3(4f, 5f, 6f),
        1.1f,
        1234,
        7,
        null,
        null);

    return TitleBackgroundCameraCapturePresetBuilder.TryBuild(
            draft,
            existing,
            out var preset,
            out var fovState,
            out _,
            out _)
        && preset.TerritoryPath == "ex5/01_xkt_x6/fld/x6f3/level/x6f3"
        && preset.TerritoryTypeId == 1234
        && preset.LayoutTerritoryTypeId == 1234
        && preset.LayoutLayerFilterKey == 7
        && fovState == TitleBackgroundCaptureValueState.Captured;
});

Test(296, "title background capture preset builder fails closed on invalid required values", () =>
{
    var existing = new TitleBackgroundPreset { FovY = 1f }.Normalize();
    var invalidPath = new TitleBackgroundCameraCaptureDraft(
        "../bad",
        777,
        new Vector3(1f, 2f, 3f),
        new Vector3(4f, 5f, 6f),
        1f,
        null,
        null,
        null,
        null);
    var invalidCamera = invalidPath with
    {
        TerritoryPath = "ffxiv/area/region/level/sample",
        Camera = new Vector3(float.NaN, 2f, 3f),
    };

    return !TitleBackgroundCameraCapturePresetBuilder.TryBuild(invalidPath, existing, out _, out _, out _, out var pathError)
        && !string.IsNullOrWhiteSpace(pathError)
        && !TitleBackgroundCameraCapturePresetBuilder.TryBuild(invalidCamera, existing, out _, out _, out _, out var cameraError)
        && cameraError.Contains("Camera", StringComparison.Ordinal);
});

Test(299, "title background session cleanup gate keeps non logged-in none map", () =>
{
    return !TitleBackgroundCharaSelectCameraLogic.ShouldEndCharaSelectTitleBackgroundSession(isLoggedIn: false, GameLobbyType.None)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldEndCharaSelectTitleBackgroundSession(isLoggedIn: false, GameLobbyType.Title)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldEndCharaSelectTitleBackgroundSession(isLoggedIn: false, GameLobbyType.CharaSelect)
        && TitleBackgroundCharaSelectCameraLogic.ShouldEndCharaSelectTitleBackgroundSession(isLoggedIn: false, GameLobbyType.LaNoscea)
        && TitleBackgroundCharaSelectCameraLogic.ShouldEndCharaSelectTitleBackgroundSession(isLoggedIn: true, GameLobbyType.None);
});

Test(300, "title background resolve only does not create hooks", () =>
{
    return !TitleBackgroundRuntimeModeHelper.ShouldCreateSceneHooks(TitleBackgroundRuntimeMode.ResolveOnly, overrideEnabled: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(TitleBackgroundRuntimeMode.ResolveOnly, overrideEnabled: true, cameraOverrideEnabled: true);
});

Test(301, "title background hook probe creates scene hooks only", () =>
{
    return TitleBackgroundRuntimeModeHelper.ShouldCreateSceneHooks(TitleBackgroundRuntimeMode.HookProbe, overrideEnabled: true)
        && TitleBackgroundRuntimeModeHelper.ShouldAllowDirectTextHookTargets(TitleBackgroundRuntimeMode.HookProbe, overrideEnabled: false)
        && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(TitleBackgroundRuntimeMode.HookProbe, overrideEnabled: true, cameraOverrideEnabled: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldValidateSceneOverrideConfiguration(TitleBackgroundRuntimeMode.HookProbe);
});

Test(302, "title background automatic probe counters require hook probe manual direct text", () =>
{
    return TitleBackgroundRuntimeModeHelper.ShouldCollectAutomaticProbeCounters(
            TitleBackgroundRuntimeMode.HookProbe,
            true,
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            TitleBackgroundResolverMode.ManualDirectTextProbe)
        && !TitleBackgroundRuntimeModeHelper.ShouldCollectAutomaticProbeCounters(
            TitleBackgroundRuntimeMode.HookProbe,
            false,
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            TitleBackgroundResolverMode.ManualDirectTextProbe)
        && !TitleBackgroundRuntimeModeHelper.ShouldCollectAutomaticProbeCounters(
            TitleBackgroundRuntimeMode.CharaSelectOnly,
            true,
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            TitleBackgroundResolverMode.ManualDirectTextProbe)
        && !TitleBackgroundRuntimeModeHelper.ShouldCollectAutomaticProbeCounters(
            TitleBackgroundRuntimeMode.HookProbe,
            true,
            TitleBackgroundResolverMode.AutoDiagnosticOnly,
            TitleBackgroundResolverMode.ManualDirectTextProbe)
        && !TitleBackgroundRuntimeModeHelper.ShouldCollectAutomaticProbeCounters(
            TitleBackgroundRuntimeMode.HookProbe,
            true,
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            TitleBackgroundResolverMode.AutoDiagnosticOnly);
});

Test(303, "title background probe report classifies complete observation", () =>
{
    var input = new TitleBackgroundProbeReportInput(
        ProbeActive: false,
        OverrideEnabled: true,
        RuntimeMode: TitleBackgroundRuntimeMode.HookProbe,
        CreateSceneResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        LobbyUpdateResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        AutomaticCountersEnabled: true,
        HooksEnabled: true,
        RuntimeError: false,
        ResolverError: string.Empty,
        LastError: string.Empty,
        CreateSceneCallCount: 2,
        LobbyUpdateCallCount: 120,
        LoadLobbySceneCallCount: 1,
        LastCreateScenePath: "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
        LastCreateSceneTerritoryId: 1192,
        LastCreateSceneLayerFilterKey: 0,
        LastLobbyUpdateMapId: GameLobbyType.None,
        LastLobbyUpdateTime: 0,
        LastLoadLobbySceneMapId: GameLobbyType.CharaSelect);

    return TitleBackgroundProbeReportHelper.GetModeStatus(input) == "ready"
        && TitleBackgroundProbeReportHelper.GetOverallStatus(input) == "observed"
        && TitleBackgroundProbeReportHelper.GetAttentionItems(input).Count == 0;
});

Test(304, "title background probe report classifies missing detours", () =>
{
    var input = new TitleBackgroundProbeReportInput(
        ProbeActive: false,
        OverrideEnabled: true,
        RuntimeMode: TitleBackgroundRuntimeMode.HookProbe,
        CreateSceneResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        LobbyUpdateResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        AutomaticCountersEnabled: true,
        HooksEnabled: true,
        RuntimeError: false,
        ResolverError: string.Empty,
        LastError: string.Empty,
        CreateSceneCallCount: 1,
        LobbyUpdateCallCount: 0,
        LoadLobbySceneCallCount: 0,
        LastCreateScenePath: "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
        LastCreateSceneTerritoryId: 1192,
        LastCreateSceneLayerFilterKey: 0,
        LastLobbyUpdateMapId: GameLobbyType.None,
        LastLobbyUpdateTime: 0,
        LastLoadLobbySceneMapId: GameLobbyType.None);

    var attentionItems = TitleBackgroundProbeReportHelper.GetAttentionItems(input);
    return TitleBackgroundProbeReportHelper.GetOverallStatus(input) == "partial"
        && attentionItems.Any(item => item.Contains("LobbyUpdate", StringComparison.Ordinal))
        && attentionItems.Any(item => item.Contains("LoadLobbyScene", StringComparison.Ordinal));
});

Test(305, "title background probe report flags wrong mode before counters", () =>
{
    var input = new TitleBackgroundProbeReportInput(
        ProbeActive: false,
        OverrideEnabled: true,
        RuntimeMode: TitleBackgroundRuntimeMode.CharaSelectOnly,
        CreateSceneResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        LobbyUpdateResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        AutomaticCountersEnabled: false,
        HooksEnabled: true,
        RuntimeError: false,
        ResolverError: string.Empty,
        LastError: string.Empty,
        CreateSceneCallCount: 0,
        LobbyUpdateCallCount: 0,
        LoadLobbySceneCallCount: 0,
        LastCreateScenePath: string.Empty,
        LastCreateSceneTerritoryId: 0,
        LastCreateSceneLayerFilterKey: 0,
        LastLobbyUpdateMapId: GameLobbyType.None,
        LastLobbyUpdateTime: 0,
        LastLoadLobbySceneMapId: GameLobbyType.None);

    return TitleBackgroundProbeReportHelper.GetModeStatus(input) == "attention"
        && TitleBackgroundProbeReportHelper.GetAttentionItems(input)
            .Any(item => item.Contains("HookProbe + ManualDirectTextProbe", StringComparison.Ordinal));
});

Test(306, "title background chara select scene readiness does not require fix on", () =>
{
    return TitleBackgroundRuntimeModeHelper.ShouldCreateSceneHooks(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: true)
        && TitleBackgroundRuntimeModeHelper.ShouldAllowDirectTextHookTargets(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldAllowDirectTextHookTargets(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: false)
        && TitleBackgroundRuntimeModeHelper.AreSceneHooksReady(createSceneReady: true, lobbyUpdateReady: true, loadLobbySceneReady: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: true, cameraOverrideEnabled: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: true, cameraOverrideEnabled: false);
});

Test(307, "title background update lobby ui stage failure does not block scene readiness", () =>
{
    var updateLobbyUiStageResolved = false;
    return !updateLobbyUiStageResolved
        && TitleBackgroundRuntimeModeHelper.AreNativeSceneAddressesReady(createSceneReady: true, lobbyUpdateReady: true, loadLobbySceneReady: true, currentMapReady: true);
});

Test(308, "title background implemented modes match selectable modes", () =>
{
    return TitleBackgroundRuntimeModeHelper.IsTitleOverrideImplemented(TitleBackgroundRuntimeMode.CharaSelectOnly)
        && !TitleBackgroundRuntimeModeHelper.IsTitleOverrideImplemented(TitleBackgroundRuntimeMode.TitleAndCharaSelect)
        && !TitleBackgroundRuntimeModeHelper.IsRuntimeModeSelectable(TitleBackgroundRuntimeMode.TitleAndCharaSelect);
});

Test(309, "title background focus fields are reserved while direct camera override is discarded", () =>
{
    return !TitleBackgroundRuntimeModeHelper.IsFocusUsed(cameraOverrideEnabled: false)
        && !TitleBackgroundRuntimeModeHelper.IsFocusUsed(cameraOverrideEnabled: true);
});

Test(311, "title background e8 callsite resolver rejects non e8 match", () =>
{
    return !TitleBackgroundAddressResolver.TryResolveE8CallTarget(0x90, new nint(0x1000), 0x20, out var rejectedTarget)
        && rejectedTarget == nint.Zero
        && TitleBackgroundAddressResolver.TryResolveE8CallTarget(0xE8, new nint(0x1000), 0x20, out var acceptedTarget)
        && acceptedTarget == new nint(0x1025);
});

Test(312, "title background e8 callsite resolver finds nearby forward callsite", () =>
{
    byte[] bytes = [0x48, 0x89, 0x5C, 0x24, 0x08, 0xE8, 0x11, 0x22, 0x33, 0x44];
    return TitleBackgroundAddressResolver.TryFindNearbyE8Callsite(bytes, 0, out var callsiteOffset)
        && callsiteOffset == 5;
});

Test(313, "title background e8 callsite resolver finds nearby backward callsite", () =>
{
    byte[] bytes = [0xE8, 0x11, 0x22, 0x33, 0x44, 0x48, 0x89, 0x5C, 0x24, 0x08];
    return TitleBackgroundAddressResolver.TryFindNearbyE8Callsite(bytes, 8, out var callsiteOffset)
        && callsiteOffset == 0;
});

Test(314, "title background e8 callsite resolver rejects window without callsite", () =>
{
    byte[] bytes = [0x48, 0x89, 0x5C, 0x24, 0x08, 0x90, 0x90, 0x90];
    return !TitleBackgroundAddressResolver.TryFindNearbyE8Callsite(bytes, 0, out var callsiteOffset)
        && callsiteOffset == -1;
});

Test(315, "title background direct text candidate requires nonzero match", () =>
{
    return TitleBackgroundAddressResolver.ShouldRecordDirectTextCandidate(new nint(0x1000))
        && !TitleBackgroundAddressResolver.ShouldRecordDirectTextCandidate(nint.Zero);
});

Test(316, "title background direct text hook target supports probe and chara select runtime", () =>
{
    return TitleBackgroundAddressResolver.ShouldPromoteDirectTextCandidateForProbe(
            new nint(0x1000),
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            allowDirectTextProbeTarget: true)
        && !TitleBackgroundAddressResolver.ShouldPromoteDirectTextCandidateForProbe(
            new nint(0x1000),
            TitleBackgroundResolverMode.AutoDiagnosticOnly,
            allowDirectTextProbeTarget: true)
        && TitleBackgroundAddressResolver.ShouldPromoteDirectTextCandidateForHook(
            new nint(0x1000),
            TitleBackgroundResolverMode.AutoDiagnosticOnly,
            allowDirectTextHookTarget: true)
        && !TitleBackgroundAddressResolver.ShouldPromoteDirectTextCandidateForHook(
            new nint(0x1000),
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            allowDirectTextHookTarget: false);
});

Test(317, "title background prologue hint classifies common msvc prologue", () =>
{
    byte[] bytes = [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83];
    return TitleBackgroundAddressResolver.ClassifyFunctionPrologue(bytes) == "likely-msvc-prologue";
});

Test(318, "title background prologue hint does not verify unknown bytes", () =>
{
    byte[] bytes = [0x8B, 0xD9, 0xE8, 0x11, 0x22, 0x33, 0x44];
    return TitleBackgroundAddressResolver.ClassifyFunctionPrologue(bytes) == "unknown";
});

Test(319, "title background fix case 1 normalize migrate flags auto enables camera and integrated when override on", () =>
{
    var changed = TitleBackgroundCharaSelectCameraLogic.NormalizeAndMigrateFlags(
        overrideEnabled: true,
        cameraOverrideEnabled: false,
        integratedCompositionEnabled: false,
        out var normalizedCamera,
        out var normalizedIntegrated);
    return changed && normalizedCamera && normalizedIntegrated;
});

Test(320, "title background fix case 2 normalize migrate flags no op when override off", () =>
{
    var changed = TitleBackgroundCharaSelectCameraLogic.NormalizeAndMigrateFlags(
        overrideEnabled: false,
        cameraOverrideEnabled: false,
        integratedCompositionEnabled: false,
        out var normalizedCamera,
        out var normalizedIntegrated);
    return !changed && !normalizedCamera && !normalizedIntegrated;
});

Test(321, "title background fix case 3 shouldArmAdapter false when integrated composition disabled", () =>
{
    var reason = TitleBackgroundCharaSelectCameraLogic.BuildShouldArmAdapterReason(
        overrideEnabled: true,
        cameraAdaptationEnabled: true,
        runtimeMode: TitleBackgroundRuntimeMode.CharaSelectOnly,
        integratedCompositionEnabled: false);
    var shouldArm = reason == "none";
    return !shouldArm && reason == "integratedCompositionDisabled";
});

Test(322, "title background fix case 4 ng integrated composition disabled beats camera framing ng", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        backgroundApplied: false,
        backgroundObserved: false,
        overrideAppliedCount: 0,
        integratedCompositionEnabled: false,
        shouldArmAdapter: false,
        shouldArmAdapterReason: "integratedCompositionDisabled",
        cameraFramingApplied: true,
        sceneOverrideApplyObserved: false));
    return result.Level == TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("integrated character composition is disabled", StringComparison.Ordinal);
});

Test(323, "title background fix case 5 happy path all correct is ok", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        titleBackgroundOverrideEnabled: true,
        titleBackgroundCameraOverrideEnabled: true,
        integratedCompositionEnabled: true,
        shouldArmAdapter: true,
        shouldArmAdapterReason: "",
        integratedCompositionRouteInvoked: true,
        integratedCompositionRouteReason: "reload requested",
        sceneOverrideApplyObserved: true,
        backgroundApplied: true,
        backgroundObserved: true,
        overrideAppliedCount: 1,
        cameraFramingApplied: true));
    return result.Level == TitleBackgroundQuickCheckLevel.OK;
});

Test(324, "title background case 1 integrated composition route required in detail lines when override not observed", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        integratedCompositionEnabled: true,
        sceneOverrideApplyObserved: false));
    return result.DetailLines.Any(l => l == "quickCheck.integratedCompositionRouteRequired=True");
});

Test(325, "title background case 2 legacy composition off is not adapter arm blocker when integrated enabled", () =>
{
    var shouldArm = TitleBackgroundCharaSelectCameraLogic.ShouldArmAdapter(
        overrideEnabled: true,
        cameraAdaptationEnabled: true,
        runtimeMode: TitleBackgroundRuntimeMode.CharaSelectOnly);
    var reason = TitleBackgroundCharaSelectCameraLogic.BuildShouldArmAdapterReason(
        overrideEnabled: true,
        cameraAdaptationEnabled: true,
        runtimeMode: TitleBackgroundRuntimeMode.CharaSelectOnly,
        integratedCompositionEnabled: true,
        candidateValid: true);
    // legacy composition is not a parameter → adapter arms regardless of legacy state
    return shouldArm && reason == "none";
});

Test(326, "title background case 3 integrated composition route not invoked with non-empty reason surfaces specific ng", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        backgroundApplied: false,
        backgroundObserved: false,
        overrideAppliedCount: 0,
        integratedCompositionEnabled: true,
        integratedCompositionRouteInvoked: false,
        integratedCompositionRouteReason: "available only in CharaSelect lobby"));
    return result.Level == TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("route was not invoked", StringComparison.Ordinal);
});

Test(327, "title background case 4 camera framing applied but scene override not observed surfaces specific ng", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        backgroundApplied: false,
        backgroundObserved: false,
        overrideAppliedCount: 0,
        cameraFramingApplied: true,
        sceneOverrideApplyObserved: false));
    return result.Level == TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("camera framing applied but scene override was not observed", StringComparison.Ordinal);
});

Test(333, "chara select service native hooks extracted to partial", () =>
{
    var root = FindRepositoryRoot();
    var hookFile = Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "CharaSelect", "CharaSelectService.NativeHooks.cs");
    var mainFile = Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "CharaSelect", "CharaSelectService.cs");
    var hookText = File.ReadAllText(hookFile);
    var mainText = File.ReadAllText(mainFile);
    return hookText.Contains("private void InitializeHooks()", StringComparison.Ordinal)
        && hookText.Contains("private bool UpdateCharaSelectDisplayDetour(", StringComparison.Ordinal)
        && hookText.Contains("private void OnFrameworkUpdate(", StringComparison.Ordinal)
        && hookText.Contains("private void DisposeHook<T>(", StringComparison.Ordinal)
        && !mainText.Contains("private void InitializeHooks()", StringComparison.Ordinal);
});

Test(336, "chara select anchor capture produces usable anchor", () =>
{
    var anchor = TitleBackgroundCharaSelectAnchorLogic.CaptureFromDrawPosition(
        "custom:n4f4", new Vector3(10f, 13.2f, -4f), 1.5f);
    return anchor.Enabled
        && anchor.HasUsableAnchor
        && anchor.CandidateId == "custom:n4f4"
        && anchor.Position == new Vector3(10f, 13.2f, -4f);
});

Test(438, "one-click known signatures fill only missing values without changing defaults", () =>
{
    var configuration = new Configuration();
    return TitleBackgroundKnownSignatures.ResolveMissing(
            string.Empty,
            TitleBackgroundKnownSignatures.CreateScene,
            useKnownWhenMissing: true) == configuration.TitleBackgroundCreateSceneSignature
        && TitleBackgroundKnownSignatures.ResolveMissing(
            string.Empty,
            TitleBackgroundKnownSignatures.CreateScene,
            useKnownWhenMissing: false) == string.Empty
        && TitleBackgroundKnownSignatures.ResolveMissing(
            "custom",
            TitleBackgroundKnownSignatures.CreateScene,
            useKnownWhenMissing: true) == "custom";
});

Test(337, "chara select anchor capture rejects non-finite position", () =>
{
    var anchor = TitleBackgroundCharaSelectAnchorLogic.CaptureFromDrawPosition(
        "custom:n4f4", new Vector3(float.NaN, 0f, 0f), 0f);
    return !anchor.Enabled && !anchor.HasUsableAnchor;
});

Test(338, "chara select placement uses anchor when enabled and candidate matches", () =>
{
    var anchor = TitleBackgroundCharaSelectAnchorLogic.CaptureFromDrawPosition(
        "custom:n4f4", new Vector3(5f, 2f, 7f), 0f);
    var resolution = TitleBackgroundCharaSelectAnchorLogic.ResolvePlacementTarget(
        anchor, "custom:n4f4", new Vector3(0f, 13f, 0f), 0.9f);
    return resolution.UsedAnchor
        && resolution.Source == "anchor"
        && resolution.Target == new Vector3(5f, 2f, 7f);
});

Test(339, "chara select placement falls back to camera focus when anchor disabled", () =>
{
    var resolution = TitleBackgroundCharaSelectAnchorLogic.ResolvePlacementTarget(
        TitleBackgroundCharaSelectAnchor.None, "custom:n4f4", new Vector3(0f, 13f, 0f), 0.9f);
    return !resolution.UsedAnchor
        && resolution.Source == "camera-focus"
        && resolution.Target == new Vector3(0f, 13f - 0.9f, 0f);
});

Test(340, "chara select placement falls back when anchor candidate mismatches", () =>
{
    var anchor = TitleBackgroundCharaSelectAnchorLogic.CaptureFromDrawPosition(
        "custom:n4f4", new Vector3(5f, 2f, 7f), 0f);
    var resolution = TitleBackgroundCharaSelectAnchorLogic.ResolvePlacementTarget(
        anchor, "manual:slot1", new Vector3(0f, 13f, 0f), 0.9f);
    return !resolution.UsedAnchor && resolution.Source == "camera-focus";
});

Test(341, "chara select anchor with empty candidate id applies to any candidate", () =>
{
    var anchor = new TitleBackgroundCharaSelectAnchor(true, string.Empty, new Vector3(1f, 2f, 3f), 0f);
    var resolution = TitleBackgroundCharaSelectAnchorLogic.ResolvePlacementTarget(
        anchor, "anything", new Vector3(0f, 13f, 0f), 0.9f);
    return resolution.UsedAnchor && resolution.Target == new Vector3(1f, 2f, 3f);
});

Test(342, "chara select anchor nudge adjusts requested axis only", () =>
{
    var anchor = TitleBackgroundCharaSelectAnchorLogic.CaptureFromDrawPosition(
        "custom:n4f4", new Vector3(5f, 2f, 7f), 0f);
    var nudged = TitleBackgroundCharaSelectAnchorLogic.ApplyNudge(
        anchor, TitleBackgroundCharaSelectAnchorAxis.Y, 0.5f);
    return nudged.Enabled
        && nudged.Position == new Vector3(5f, 2.5f, 7f);
});

Test(343, "chara select anchor nudge ignores non-finite delta", () =>
{
    var anchor = TitleBackgroundCharaSelectAnchorLogic.CaptureFromDrawPosition(
        "custom:n4f4", new Vector3(5f, 2f, 7f), 0f);
    var nudged = TitleBackgroundCharaSelectAnchorLogic.ApplyNudge(
        anchor, TitleBackgroundCharaSelectAnchorAxis.X, float.NaN);
    return nudged.Position == new Vector3(5f, 2f, 7f);
});

Test(345, "fixOn focus override raises focus to anchor body height when candidate matches", () =>
{
    var anchor = new TitleBackgroundCharaSelectAnchor(true, "custom:n4f4", new Vector3(1f, 14f, 3f), 0f);
    var resolution = TitleBackgroundFixOnFocusOverrideLogic.Resolve(
        true, anchor, "custom:n4f4", new Vector3(0f, 14.092f, 0f), 0.9f);
    // 足元(Y=14)を見下ろさないよう、焦点 Y は anchor.Y + bodyDrop(0.9)=14.9。X/Z はアンカーへ。
    return resolution.ShouldOverride
        && resolution.Source == "anchor"
        && resolution.Focus == new Vector3(1f, 14.9f, 3f);
});

Test(346, "fixOn focus override passes through when feature disabled", () =>
{
    var anchor = new TitleBackgroundCharaSelectAnchor(true, "custom:n4f4", new Vector3(1f, 14f, 3f), 0f);
    var observed = new Vector3(0f, 14.092f, 0f);
    var resolution = TitleBackgroundFixOnFocusOverrideLogic.Resolve(
        false, anchor, "custom:n4f4", observed, 0.9f);
    return !resolution.ShouldOverride
        && resolution.Source == "passthrough"
        && resolution.Focus == observed;
});

Test(347, "fixOn focus override passes through when candidate mismatches", () =>
{
    var anchor = new TitleBackgroundCharaSelectAnchor(true, "custom:n4f4", new Vector3(1f, 14f, 3f), 0f);
    var observed = new Vector3(0f, 14.092f, 0f);
    var resolution = TitleBackgroundFixOnFocusOverrideLogic.Resolve(
        true, anchor, "manual:slot1", observed, 0.9f);
    return !resolution.ShouldOverride && resolution.Focus == observed;
});

Test(348, "fixOn focus override passes through when anchor unusable", () =>
{
    var observed = new Vector3(0f, 14.092f, 0f);
    var resolution = TitleBackgroundFixOnFocusOverrideLogic.Resolve(
        true, TitleBackgroundCharaSelectAnchor.None, "custom:n4f4", observed, 0.9f);
    return !resolution.ShouldOverride && resolution.Source == "passthrough";
});

Test(349, "fixOn focus override rejects empty candidate id wildcard", () =>
{
    // カメラ焦点 override は安全側に倒し、空 CandidateId を全候補一致として扱わない。
    var anchor = new TitleBackgroundCharaSelectAnchor(true, string.Empty, new Vector3(1f, 14f, 3f), 0f);
    var observed = new Vector3(0f, 14.092f, 0f);
    var resolution = TitleBackgroundFixOnFocusOverrideLogic.Resolve(
        true, anchor, "anything", observed, 0.9f);
    return !resolution.ShouldOverride
        && resolution.Source == "passthrough"
        && resolution.Focus == observed;
});

Test(350, "fixOn focus override passes through when active candidate is empty", () =>
{
    var anchor = new TitleBackgroundCharaSelectAnchor(true, "custom:n4f4", new Vector3(1f, 14f, 3f), 0f);
    var observed = new Vector3(0f, 14.092f, 0f);
    var resolution = TitleBackgroundFixOnFocusOverrideLogic.Resolve(
        true, anchor, string.Empty, observed, 0.9f);
    return !resolution.ShouldOverride && resolution.Focus == observed;
});

Test(351, "fixOn focus override passive observation takes precedence over focus flag", () =>
{
    // passive ON は最優先 passthrough。passive と focus override の全 4 組合せを固定。
    return !TitleBackgroundFixOnFocusOverrideLogic.ShouldConsiderFocusOverride(true, true)
        && !TitleBackgroundFixOnFocusOverrideLogic.ShouldConsiderFocusOverride(true, false)
        && !TitleBackgroundFixOnFocusOverrideLogic.ShouldConsiderFocusOverride(false, false)
        && TitleBackgroundFixOnFocusOverrideLogic.ShouldConsiderFocusOverride(false, true);
});

Test(352, "fixOn detour gates focus override on passive precedence and fixOn-specific context", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var detour = ExtractMethodBody(hooksText, "private nint LobbyCameraFixOnDetour(nint self, float* cameraPos, float* focusPos, float fovY)");

    // 焦点 override 経路は passive 最優先判定と FixOn 専用の実行コンテキストゲートの双方を通す。
    // CurrentLobbyMap に依存する IsCharaSelectCharacterCompositionActive は使わない（読み込み中に弾かれるため）。
    return detour.Contains("ShouldConsiderFocusOverride(", StringComparison.Ordinal)
        && detour.Contains("IsFixOnFocusOverrideContextActive()", StringComparison.Ordinal)
        && !detour.Contains("IsCharaSelectCharacterCompositionActive()", StringComparison.Ordinal)
        && detour.Contains("CharaSelectCharacterFocusBodyDrop", StringComparison.Ordinal);
});

Test(353, "fixOn execution context ready during scene load without current lobby map", () =>
{
    // FixOn 発火時に CurrentLobbyMap が None でも、session active かつ scene generation 一致 +
    // CharaSelect セッションなら実行コンテキストは ready（タイミング問題の核心）。
    return TitleBackgroundFixOnFocusOverrideLogic.IsExecutionContextReady(
        isLoggedIn: false,
        serviceReady: true,
        bridgeActive: true,
        sessionActive: true,
        activeSceneGeneration: 3,
        currentSceneGeneration: 3,
        charaSelectSessionLobby: true);
});

Test(354, "fixOn execution context blocked when session inactive", () =>
{
    return !TitleBackgroundFixOnFocusOverrideLogic.IsExecutionContextReady(
        false, true, true, false, 3, 3, true);
});

Test(355, "fixOn execution context blocked when scene generation mismatches", () =>
{
    return !TitleBackgroundFixOnFocusOverrideLogic.IsExecutionContextReady(
        false, true, true, true, 3, 4, true)
        && !TitleBackgroundFixOnFocusOverrideLogic.IsExecutionContextReady(
            false, true, true, true, 0, 0, true);
});

Test(356, "fixOn execution context blocked when logged in or bridge off or not chara select", () =>
{
    return !TitleBackgroundFixOnFocusOverrideLogic.IsExecutionContextReady(
            true, true, true, true, 3, 3, true)
        && !TitleBackgroundFixOnFocusOverrideLogic.IsExecutionContextReady(
            false, false, true, true, 3, 3, true)
        && !TitleBackgroundFixOnFocusOverrideLogic.IsExecutionContextReady(
            false, true, false, true, 3, 3, true)
        && !TitleBackgroundFixOnFocusOverrideLogic.IsExecutionContextReady(
            false, true, true, true, 3, 3, false);
});

Test(357, "fixOn view override applies saved camera/focus/fov when candidate matches", () =>
{
    var view = new TitleBackgroundCharaSelectView(
        true, "custom:n4f4", new Vector3(1f, 14f, 3f), new Vector3(0f, 14.5f, 0f), 45f);
    var r = TitleBackgroundFixOnViewOverrideLogic.Resolve(
        true, view, "custom:n4f4", new Vector3(0f, 0f, 0f), new Vector3(0f, 14f, 0f), 60f);
    return r.ShouldOverride
        && r.Source == "view"
        && r.Camera == new Vector3(1f, 14f, 3f)
        && r.Focus == new Vector3(0f, 14.5f, 0f)
        && Math.Abs(r.FovY - 45f) < 0.0001f;
});

Test(358, "fixOn view override passes through when disabled / mismatch / empty / non-finite", () =>
{
    var view = new TitleBackgroundCharaSelectView(
        true, "custom:n4f4", new Vector3(1f, 14f, 3f), new Vector3(0f, 14.5f, 0f), 45f);
    var observedCam = new Vector3(2f, 2f, 2f);
    var observedFocus = new Vector3(0f, 14f, 0f);
    var disabled = TitleBackgroundFixOnViewOverrideLogic.Resolve(false, view, "custom:n4f4", observedCam, observedFocus, 60f);
    var mismatch = TitleBackgroundFixOnViewOverrideLogic.Resolve(true, view, "manual:slot1", observedCam, observedFocus, 60f);
    var emptyView = new TitleBackgroundCharaSelectView(true, string.Empty, new Vector3(1f, 14f, 3f), new Vector3(0f, 14.5f, 0f), 45f);
    var emptyId = TitleBackgroundFixOnViewOverrideLogic.Resolve(true, emptyView, "anything", observedCam, observedFocus, 60f);
    var nanView = new TitleBackgroundCharaSelectView(true, "custom:n4f4", new Vector3(float.NaN, 0f, 0f), new Vector3(0f, 0f, 0f), 45f);
    var nonFinite = TitleBackgroundFixOnViewOverrideLogic.Resolve(true, nanView, "custom:n4f4", observedCam, observedFocus, 60f);
    return !disabled.ShouldOverride && disabled.Camera == observedCam && disabled.Focus == observedFocus
        && !mismatch.ShouldOverride
        && !emptyId.ShouldOverride
        && !nonFinite.ShouldOverride;
});

Test(360, "fixOn detour applies view override with camera+focus+fov ahead of focus-only", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var detour = ExtractMethodBody(hooksText, "private nint LobbyCameraFixOnDetour(nint self, float* cameraPos, float* focusPos, float fovY)");
    var viewIndex = detour.IndexOf("TitleBackgroundFixOnViewOverrideLogic.Resolve", StringComparison.Ordinal);
    var focusIndex = detour.IndexOf("TitleBackgroundFixOnFocusOverrideLogic.Resolve", StringComparison.Ordinal);

    // view 経路は camera+focus+fov をまとめて上書きし、focus-only 経路より前に評価する。
    return viewIndex >= 0 && focusIndex >= 0 && viewIndex < focusIndex
        && detour.Contains("overrideFovY = viewResolution.FovY", StringComparison.Ordinal)
        && detour.Contains("invocationMode = \"view-override\"", StringComparison.Ordinal);
});

Test(362, "fixOn hook installs for view override", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground"), "TitleScreenBackgroundService*.cs").Select(File.ReadAllText));
    var body = ExtractMethodBody(serviceText, "private bool ShouldInstallFixOnHook()");
    return body.Contains("TitleBackgroundCharaSelectViewEnabled", StringComparison.Ordinal);
});

Test(363, "chara select view capture tags candidate and is gated to pre-login chara select", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var capture = ExtractMethodBody(timelineText, "public bool TryCaptureCharaSelectViewFromCurrentCamera(out string status)");
    var availability = ExtractMethodBody(timelineText, "internal bool IsCharaSelectViewCaptureAvailable()");
    return capture.Contains("skipped-post-login", StringComparison.Ordinal)
        && capture.Contains("skipped-not-chara-select", StringComparison.Ordinal)
        && capture.Contains("skipped-empty-candidate", StringComparison.Ordinal)
        && capture.Contains("TryCaptureActiveCameraSnapshot", StringComparison.Ordinal)
        && capture.Contains("ReloadNativeIntegration()", StringComparison.Ordinal)
        && availability.Contains("!_clientState.IsLoggedIn", StringComparison.Ordinal)
        && availability.Contains("lobbyMap == GameLobbyType.CharaSelect", StringComparison.Ordinal);
});

Test(364, "fixOn focus override gate reason maps feature/passive/context precedence", () =>
{
    // feature OFF が最優先、その次に passive、最後に実行コンテキスト理由。ready は全成立時のみ。
    return TitleBackgroundFixOnFocusOverrideLogic.DescribeGateReason(false, false, true, "ready") == "feature-off"
        && TitleBackgroundFixOnFocusOverrideLogic.DescribeGateReason(true, false, true, "ready") == "feature-off"
        && TitleBackgroundFixOnFocusOverrideLogic.DescribeGateReason(true, true, true, "ready") == "passive-precedence"
        && TitleBackgroundFixOnFocusOverrideLogic.DescribeGateReason(false, true, false, "bridge-off") == "bridge-off"
        && TitleBackgroundFixOnFocusOverrideLogic.DescribeGateReason(false, true, true, "ready") == "ready";
});

Test(365, "anchor frame constants are distinct provenance tags", () =>
{
    return TitleBackgroundCharaSelectAnchorFrame.World == "world"
        && TitleBackgroundCharaSelectAnchorFrame.LobbyNative == "lobby-native"
        && TitleBackgroundCharaSelectAnchorFrame.CharaSelectFallback == "chara-select-fallback"
        && TitleBackgroundCharaSelectAnchorFrame.Unknown == "unknown"
        && !TitleBackgroundCharaSelectAnchorFrame.IsPlacementSupported(string.Empty)
        && !TitleBackgroundCharaSelectAnchorFrame.IsPlacementSupported(TitleBackgroundCharaSelectAnchorFrame.World)
        && TitleBackgroundCharaSelectAnchorFrame.IsPlacementSupported(TitleBackgroundCharaSelectAnchorFrame.LobbyNative)
        && TitleBackgroundCharaSelectAnchorFrame.IsPlacementSupported(TitleBackgroundCharaSelectAnchorFrame.CharaSelectFallback);
});

Test(367, "logged-in capture tags anchor frame as world, chara-select capture as fallback", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var loggedIn = ExtractMethodBody(timelineText, "public bool TryCaptureLoggedInPositionAsAnchor(out string status)");
    var charaSelect = ExtractMethodBody(timelineText, "public bool TryCaptureCharaSelectAnchorFromCurrentCharacter(out string status)");

    return loggedIn.Contains("TitleBackgroundCharaSelectAnchorFrame.World", StringComparison.Ordinal)
        && charaSelect.Contains("TitleBackgroundCharaSelectAnchorFrame.CharaSelectFallback", StringComparison.Ordinal);
});

Test(368, "world experimental gate is eligible when flag/frame/candidate/territory all match", () =>
{
    var gate = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
        true, true, new Vector3(1f, 2f, 3f), "world", "custom:n4f4", 816, "custom:n4f4", 816);
    return gate == TitleBackgroundExperimentalWorldPlacementGate.Eligible
        && TitleBackgroundExperimentalWorldPlacementLogic.IsEligible(gate)
        && TitleBackgroundExperimentalWorldPlacementLogic.DescribeReason(gate) == "eligible";
});

Test(369, "world experimental gate rejects when flag disabled", () =>
{
    var gate = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
        false, true, new Vector3(1f, 2f, 3f), "world", "custom:n4f4", 816, "custom:n4f4", 816);
    return gate == TitleBackgroundExperimentalWorldPlacementGate.Disabled
        && !TitleBackgroundExperimentalWorldPlacementLogic.IsEligible(gate);
});

Test(370, "world experimental gate rejects non-finite anchor position", () =>
{
    var gate = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
        true, true, new Vector3(float.NaN, 0f, 0f), "world", "custom:n4f4", 816, "custom:n4f4", 816);
    return gate == TitleBackgroundExperimentalWorldPlacementGate.AnchorUnusable;
});

Test(371, "world experimental gate rejects non-world frame", () =>
{
    var gate = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
        true, true, new Vector3(1f, 2f, 3f), "lobby-native", "custom:n4f4", 816, "custom:n4f4", 816);
    return gate == TitleBackgroundExperimentalWorldPlacementGate.NotWorldFrame;
});

Test(372, "world experimental gate rejects empty/unknown candidate", () =>
{
    var emptyAnchor = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
        true, true, new Vector3(1f, 2f, 3f), "world", "", 816, "custom:n4f4", 816);
    var emptyActive = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
        true, true, new Vector3(1f, 2f, 3f), "world", "custom:n4f4", 816, "", 816);
    return emptyAnchor == TitleBackgroundExperimentalWorldPlacementGate.CandidateUnknownOrEmpty
        && emptyActive == TitleBackgroundExperimentalWorldPlacementGate.CandidateUnknownOrEmpty;
});

Test(373, "world experimental gate rejects candidate mismatch", () =>
{
    var gate = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
        true, true, new Vector3(1f, 2f, 3f), "world", "custom:n4f4", 816, "manual:slot1", 816);
    return gate == TitleBackgroundExperimentalWorldPlacementGate.CandidateMismatch;
});

Test(374, "world experimental gate rejects missing saved territory (legacy config)", () =>
{
    var gate = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
        true, true, new Vector3(1f, 2f, 3f), "world", "custom:n4f4", 0, "custom:n4f4", 816);
    return gate == TitleBackgroundExperimentalWorldPlacementGate.NoSavedTerritory;
});

Test(375, "world experimental gate rejects territory mismatch and unknown active territory", () =>
{
    var mismatch = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
        true, true, new Vector3(1f, 2f, 3f), "world", "custom:n4f4", 816, "custom:n4f4", 962);
    var unknownActive = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
        true, true, new Vector3(1f, 2f, 3f), "world", "custom:n4f4", 816, "custom:n4f4", 0);
    return mismatch == TitleBackgroundExperimentalWorldPlacementGate.TerritoryMismatch
        && unknownActive == TitleBackgroundExperimentalWorldPlacementGate.TerritoryMismatch;
});

Test(376, "placement precedence: world > supported anchor > camera-focus", () =>
{
    var supported = new TitleBackgroundCharaSelectAnchor(true, "custom:n4f4", new Vector3(5f, 2f, 7f), 0f);

    var world = TitleBackgroundCharaSelectAnchorLogic.ResolvePlacementWithExperimentalWorld(
        true, new Vector3(-1f, -2f, -3f), supported, "lobby-native", "custom:n4f4", new Vector3(0f, 13f, 0f), 0.9f);
    var supportedOnly = TitleBackgroundCharaSelectAnchorLogic.ResolvePlacementWithExperimentalWorld(
        false, Vector3.Zero, supported, "lobby-native", "custom:n4f4", new Vector3(0f, 13f, 0f), 0.9f);
    var fallback = TitleBackgroundCharaSelectAnchorLogic.ResolvePlacementWithExperimentalWorld(
        false, Vector3.Zero, TitleBackgroundCharaSelectAnchor.None, "lobby-native", "custom:n4f4", new Vector3(0f, 13f, 0f), 0.9f);

    return world.Source == "world-experimental"
        && world.EffectiveFrame == "world"
        && world.Target == new Vector3(-1f, -2f, -3f)
        && supportedOnly.Source == "anchor"
        && supportedOnly.EffectiveFrame == "lobby-native"
        && supportedOnly.Target == new Vector3(5f, 2f, 7f)
        && fallback.Source == "camera-focus"
        && fallback.EffectiveFrame == "unknown"
        && fallback.Target == new Vector3(0f, 13f - 0.9f, 0f);
});

Test(377, "world experimental placement is never ground verified", () =>
{
    return !TitleBackgroundCharaSelectAnchorFrame.HasGroundProvenance("world")
        && !TitleBackgroundAutomaticCheckLogic.ResolveGroundPlacementVerified(true, "world-experimental", "world")
        && !TitleBackgroundAutomaticCheckLogic.ResolveGroundPlacementVerified(true, "anchor", "world");
});

Test(379, "ApplyFrom keeps world experimental fields and normalize fails closed on territory 0", () =>
{
    var kept = new Configuration();
    kept.ApplyFrom(new Configuration
    {
        TitleBackgroundCharaSelectAnchorEnabled = true,
        TitleBackgroundCharaSelectAnchorCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectAnchorFrame = "world",
        TitleBackgroundCharaSelectAnchorTerritoryTypeId = 816,
        TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = true,
        TitleBackgroundCharaSelectAnchorX = 1f,
        TitleBackgroundCharaSelectAnchorY = 2f,
        TitleBackgroundCharaSelectAnchorZ = 3f,
    });

    var failClosed = new Configuration();
    failClosed.ApplyFrom(new Configuration
    {
        TitleBackgroundCharaSelectAnchorEnabled = true,
        TitleBackgroundCharaSelectAnchorCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectAnchorFrame = "world",
        TitleBackgroundCharaSelectAnchorTerritoryTypeId = 0,
        TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = true,
    });

    return kept.TitleBackgroundCharaSelectAnchorTerritoryTypeId == 816
        && kept.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled
        && !failClosed.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled;
});

Test(381, "auto-check diagnostic selector includes world experimental keys", () =>
{
    var input = new[]
    {
        "characterPlace.worldExperimentalSource=config",
        "characterPlace.savedTerritoryTypeId=816",
        "characterPlace.activeCandidateTerritoryId=816",
        "characterPlace.candidateMatch=True",
        "characterPlace.territoryMatch=True",
        "characterPlace.worldExperimentalEnabled=False",
        "characterPlace.worldExperimentalConfiguredEnabled=True",
        "characterPlace.persistentApplyEnabled=True",
        "characterPlace.worldExperimentalGate=disabled",
        "characterPlace.worldExperimentalApplicable=False",
        "characterPlace.unrelatedKey=should-drop",
    };
    var selected = TitleBackgroundAutomaticCheckDiagnosticSelector.Select(input);
    return selected.Count == 10
        && selected.Any(line => line.StartsWith("characterPlace.worldExperimentalSource=", StringComparison.Ordinal))
        && selected.Any(line => line.StartsWith("characterPlace.worldExperimentalConfiguredEnabled=", StringComparison.Ordinal))
        && selected.Any(line => line.StartsWith("characterPlace.persistentApplyEnabled=", StringComparison.Ordinal))
        && selected.Any(line => line.StartsWith("characterPlace.worldExperimentalApplicable=", StringComparison.Ordinal))
        && !selected.Any(line => line.StartsWith("characterPlace.unrelatedKey=", StringComparison.Ordinal));
});

Test(382, "world experimental persistent apply is unlocked and standing button stays hidden (auto-persist only)", () =>
{
    var root = FindRepositoryRoot();
    var normal = ExtractMethodBody(
        string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components"), "SettingsTab*.cs").Select(File.ReadAllText)),
        "private void DrawTitleBackgroundSettings()");
    var quickCheckText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.QuickCheck.cs"));
    var completeBody = ExtractMethodBody(quickCheckText, "private void CompleteAutomaticQuickCheck(bool partial)");
    // 2026-07-03: 実機3点検証(残差0.002、world/lobby恒等)を経て PersistentApplyEnabled は解禁(true)。
    // Evaluate gate を通った場合のみ通常セッションでも適用される。手動の「立ち位置保存」ボタン(UI操作)は
    // 依然として出さない(UI操作数4の契約維持)。永続化は run 成功時の自動保存のみが経路。
    // 保存処理は settings snapshot 復元の後・reload の前に走ること、保存条件に run-scoped placement count と
    // source 検証が含まれることをソース文字列検査でロックする。
    return TitleBackgroundExperimentalWorldPlacementLogic.PersistentApplyEnabled
        && !normal.Contains("DrawTitleBackgroundSimpleStandingPositionButton", StringComparison.Ordinal)
        && completeBody.Contains("afterRestoreBeforeReload: () =>", StringComparison.Ordinal)
        && completeBody.Contains("persistedThisRun = TryPersistRunAnchorFromCandidate(persistenceCandidate)", StringComparison.Ordinal)
        && completeBody.IndexOf("RestoreAutomaticCheckSettingsOnce(", StringComparison.Ordinal)
            < completeBody.IndexOf("TryPersistRunAnchorFromCandidate(persistenceCandidate)", StringComparison.Ordinal)
        // 保存条件(ShouldPersistRunAnchor 呼び出し)が run-scoped placement 適用実績と
        // world-experimental(probe) source の両方を検証していることをソース文字列で確認する。
        && quickCheckText.Contains("TitleBackgroundAutomaticCheckLogic.ShouldPersistRunAnchor(", StringComparison.Ordinal)
        && quickCheckText.Contains("runPlacementApplied,", StringComparison.Ordinal)
        && quickCheckText.Contains("_characterPlacement.LastCharaSelectCharacterPlacementSource,", StringComparison.Ordinal)
        && quickCheckText.Contains("worldResolution.Eligible,", StringComparison.Ordinal)
        && quickCheckText.Contains("worldResolution.Source,", StringComparison.Ordinal)
        && quickCheckText.Contains("WorldExperimentalSourceProbe);", StringComparison.Ordinal);
});

Test(383, "simple reset clears session probe as well as config", () =>
{
    var root = FindRepositoryRoot();
    var quickCheckText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.QuickCheck.cs"));
    var reset = ExtractMethodBody(quickCheckText, "internal bool ResetSimpleTitleBackgroundSettings()");
    return reset.Contains("ClearWorldProbeAnchor()", StringComparison.Ordinal);
});

Test(384, "world experimental diagnostics derive all fields from resolver result", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    var diag = ExtractMethodBody(serviceText, "private void AddWorldExperimentalPlacementLines(List<string> lines)");
    // 選択元・候補・territory・enabled をすべて resolved（同一源）から取る（混在防止）。
    return diag.Contains("resolved.Source", StringComparison.Ordinal)
        && diag.Contains("resolved.AnchorCandidateId", StringComparison.Ordinal)
        && diag.Contains("resolved.SavedTerritoryTypeId", StringComparison.Ordinal)
        && diag.Contains("resolved.ExperimentalEnabled", StringComparison.Ordinal)
        && diag.Contains("resolved.ConfiguredEnabled", StringComparison.Ordinal)
        && !diag.Contains("_worldProbeState", StringComparison.Ordinal);
});

Test(385, "probe capture is non-persistent (no Save/Store/config writes)", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var body = ExtractMethodBody(timelineText, "public bool CaptureWorldProbeAnchorInMemory(out string status)");
    return body.Length > 0
        && !body.Contains(".Save(", StringComparison.Ordinal)
        && !body.Contains("StoreCharaSelectAnchor", StringComparison.Ordinal)
        && !body.Contains("_configuration.", StringComparison.Ordinal)
        && body.Contains("_worldProbeState.Position", StringComparison.Ordinal)
        && body.Contains("_clientState.TerritoryType", StringComparison.Ordinal);
});

Test(386, "fixOn focus anchor builder never admits world frame", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var nativeText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var fixOnBuilder = ExtractMethodBody(timelineText, "private TitleBackgroundCharaSelectAnchor BuildFixOnFocusAnchor()");
    var supportedBuilder = ExtractMethodBody(timelineText, "private TitleBackgroundCharaSelectAnchor BuildSupportedFrameAnchor()");
    return fixOnBuilder.Contains("BuildSupportedFrameAnchor()", StringComparison.Ordinal)
        && supportedBuilder.Contains("IsPlacementSupported", StringComparison.Ordinal)
        && !supportedBuilder.Contains("WorldExperimental", StringComparison.Ordinal)
        && nativeText.Contains("BuildFixOnFocusAnchor()", StringComparison.Ordinal)
        && !nativeText.Contains("BuildCharaSelectAnchor(", StringComparison.Ordinal);
});

Test(387, "placement records effective frame from decision and saves measured territory", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var placement = ExtractMethodBody(timelineText, "private void MaintainCharaSelectCharacterPlacement()");
    var capture = ExtractMethodBody(timelineText, "public bool TryCaptureLoggedInPositionAsAnchor(out string status)");
    return placement.Contains("_characterPlacement.LastCharaSelectCharacterPlacementAnchorFrame = decision.EffectiveFrame", StringComparison.Ordinal)
        && capture.Contains("_configuration.TitleBackgroundCharaSelectAnchorTerritoryTypeId = _clientState.TerritoryType", StringComparison.Ordinal);
});

Test(388, "simple reset clears world experimental fields", () =>
{
    var root = FindRepositoryRoot();
    var quickCheckText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.QuickCheck.cs"));
    var reset = ExtractMethodBody(quickCheckText, "internal bool ResetSimpleTitleBackgroundSettings()");
    return reset.Contains("TitleBackgroundCharaSelectAnchorTerritoryTypeId = 0", StringComparison.Ordinal)
        && reset.Contains("TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = false", StringComparison.Ordinal);
});

Test(398, "automatic completion integrates phase 0C and auto-copies the unified report", () =>
{
    var body = ReadServiceMethodBody("TitleScreenBackgroundService.QuickCheck.cs", "private void CompleteAutomaticQuickCheck(bool partial)");
    var addIndex = body.IndexOf("TryAddWorldCoordinateSampleFromRun(", StringComparison.Ordinal);
    var reportIndex = body.IndexOf("var report =", StringComparison.Ordinal);
    return addIndex >= 0 && reportIndex >= 0 && addIndex < reportIndex
        && body.Contains("World/Lobby Coordinate Correspondence", StringComparison.Ordinal)
        && body.Contains("PublishAutomaticCheckReport(report, \"complete\")", StringComparison.Ordinal);
});

Test(403, "phase 0C sample collection rejects empty and duplicate run ids", () =>
{
    var body = ReadServiceMethodBody(
        "TitleScreenBackgroundService.TimelineDiagnostics.cs",
        "public bool TryAddWorldCoordinateSampleFromRun(string runId, string completedAt)");
    return body.Contains("string.IsNullOrWhiteSpace(runId)", StringComparison.Ordinal)
        && body.Contains("string.IsNullOrWhiteSpace(completedAt)", StringComparison.Ordinal)
        && body.Contains("sample.RunId, runId", StringComparison.Ordinal);
});

Test(407, "world coordinate sample accepts a valid probe run", () =>
{
    var world = new Vector3(-353.786f, 47.989f, 502.939f);
    return TitleBackgroundWorldCoordinateCorrespondenceLogic.IsAcceptableRun(
        true, "probe", "world-experimental", 2225, true,
        "custom:n4f4", "custom:n4f4", 816, 816, "world",
        world, world,
        new Vector3(-353.786f, 48.823f, 502.939f),
        new Vector3(-353.786f, 14.159f, 506.239f),
        new Vector3(-353.786f, 14.159f, 502.939f));
});

Test(408, "world coordinate sample rejects ineligible / mismatched / non-finite / displaced run", () =>
{
    var world = new Vector3(1f, 2f, 3f);
    var focus = new Vector3(1f, 2f, 3f);
    var cam = new Vector3(1f, 14f, 3f);
    var look = new Vector3(1f, 14f, 3f);
    bool Accept(bool eligible, string src, string runSrc, int applied, bool gen, string aCand, string actCand, uint sTerr, uint aTerr, string frame, Vector3 w, Vector3 target)
        => TitleBackgroundWorldCoordinateCorrespondenceLogic.IsAcceptableRun(
            eligible, src, runSrc, applied, gen, aCand, actCand, sTerr, aTerr, frame, w, target, focus, cam, look);

    var baseline = Accept(true, "probe", "world-experimental", 5, true, "custom:n4f4", "custom:n4f4", 816, 816, "world", world, world);
    var ineligible = !Accept(false, "probe", "world-experimental", 5, true, "custom:n4f4", "custom:n4f4", 816, 816, "world", world, world);
    var nonProbe = !Accept(true, "config", "world-experimental", 5, true, "custom:n4f4", "custom:n4f4", 816, 816, "world", world, world);
    var wrongSource = !Accept(true, "probe", "anchor", 5, true, "custom:n4f4", "custom:n4f4", 816, 816, "world", world, world);
    var applied0 = !Accept(true, "probe", "world-experimental", 0, true, "custom:n4f4", "custom:n4f4", 816, 816, "world", world, world);
    var genMismatch = !Accept(true, "probe", "world-experimental", 5, false, "custom:n4f4", "custom:n4f4", 816, 816, "world", world, world);
    var candMismatch = !Accept(true, "probe", "world-experimental", 5, true, "custom:n4f4", "manual:slot1", 816, 816, "world", world, world);
    var emptyCand = !Accept(true, "probe", "world-experimental", 5, true, "", "", 816, 816, "world", world, world);
    var territoryZero = !Accept(true, "probe", "world-experimental", 5, true, "custom:n4f4", "custom:n4f4", 0, 0, "world", world, world);
    var territoryMismatch = !Accept(true, "probe", "world-experimental", 5, true, "custom:n4f4", "custom:n4f4", 816, 962, "world", world, world);
    var wrongFrame = !Accept(true, "probe", "world-experimental", 5, true, "custom:n4f4", "custom:n4f4", 816, 816, "lobby-native", world, world);
    var nonFinite = !Accept(true, "probe", "world-experimental", 5, true, "custom:n4f4", "custom:n4f4", 816, 816, "world", new Vector3(float.NaN, 0f, 0f), world);
    var displaced = !Accept(true, "probe", "world-experimental", 5, true, "custom:n4f4", "custom:n4f4", 816, 816, "world", world, new Vector3(99f, 2f, 3f));
    return baseline && ineligible && nonProbe && wrongSource && applied0 && genMismatch
        && candMismatch && emptyCand && territoryZero && territoryMismatch && wrongFrame && nonFinite && displaced;
});

Test(409, "world coordinate analysis needs at least 2 samples", () =>
{
    var one = TitleBackgroundWorldCoordinateCorrespondenceLogic.Analyze(
        new[] { MakeWorldSample(0, new Vector3(0f, 40f, 0f), new Vector3(0f, 14f, 0f)) });
    return one.Verdict == TitleBackgroundWorldCoordinateVerdict.InsufficientSamples;
});

Test(410, "world coordinate analysis flags same-elevation without dividing", () =>
{
    var samples = new[]
    {
        MakeWorldSample(0, new Vector3(0f, 40f, 0f), new Vector3(0f, 14f, 0f)),
        MakeWorldSample(1, new Vector3(0f, 40f, 0f), new Vector3(0f, 14f, 0f)),
    };
    var analysis = TitleBackgroundWorldCoordinateCorrespondenceLogic.Analyze(samples);
    return analysis.Verdict == TitleBackgroundWorldCoordinateVerdict.InsufficientElevationVariance
        && !analysis.HasElevationVariance;
});

Test(411, "world coordinate analysis computes safe diff at two elevations (lobby Y stuck => slope ~0)", () =>
{
    var samples = new[]
    {
        MakeWorldSample(0, new Vector3(-353f, 47.989f, 502f), new Vector3(-353f, 14.159f, 502f)),
        MakeWorldSample(1, new Vector3(-353f, 40.0f, 502f), new Vector3(-353f, 14.159f, 502f)),
    };
    var analysis = TitleBackgroundWorldCoordinateCorrespondenceLogic.Analyze(samples);
    return analysis.Verdict == TitleBackgroundWorldCoordinateVerdict.LinearYCandidate
        && analysis.HasElevationVariance
        && analysis.XOffsetConstant
        && analysis.ZOffsetConstant
        && MathF.Abs(analysis.YLinearSlope) < 0.05f
        && !analysis.ResidualComputed; // 2 件では残差を出さない
});

Test(412, "world coordinate analysis computes residual at three elevations", () =>
{
    var samples = new[]
    {
        MakeWorldSample(0, new Vector3(0f, 40f, 0f), new Vector3(0f, 22f, 0f)),
        MakeWorldSample(1, new Vector3(0f, 30f, 0f), new Vector3(0f, 17f, 0f)),
        MakeWorldSample(2, new Vector3(0f, 20f, 0f), new Vector3(0f, 12f, 0f)),
    };
    var analysis = TitleBackgroundWorldCoordinateCorrespondenceLogic.Analyze(samples);
    return analysis.Verdict == TitleBackgroundWorldCoordinateVerdict.LinearYCandidate
        && analysis.ResidualComputed
        && MathF.Abs(analysis.YLinearSlope - 0.5f) < 0.01f
        && analysis.MaxResidual < 0.01f;
});

Test(413, "world coordinate analysis flags non-linear Y as inconsistent via residual", () =>
{
    var samples = new[]
    {
        MakeWorldSample(0, new Vector3(0f, 40f, 0f), new Vector3(0f, 22f, 0f)),
        MakeWorldSample(1, new Vector3(0f, 30f, 0f), new Vector3(0f, 17f, 0f)),
        MakeWorldSample(2, new Vector3(0f, 20f, 0f), new Vector3(0f, 30f, 0f)),
    };
    var analysis = TitleBackgroundWorldCoordinateCorrespondenceLogic.Analyze(samples);
    return analysis.Verdict == TitleBackgroundWorldCoordinateVerdict.Inconsistent
        && analysis.ResidualComputed
        && analysis.MaxResidual > TitleBackgroundWorldCoordinateCorrespondenceLogic.YResidualTolerance;
});

Test(414, "world coordinate analysis flags inconsistent X/Z translation", () =>
{
    var samples = new[]
    {
        MakeWorldSample(0, new Vector3(0f, 40f, 0f), new Vector3(0f, 14f, 0f)),
        MakeWorldSample(1, new Vector3(0f, 30f, 0f), new Vector3(5f, 14f, 0f)),
    };
    var analysis = TitleBackgroundWorldCoordinateCorrespondenceLogic.Analyze(samples);
    return analysis.Verdict == TitleBackgroundWorldCoordinateVerdict.Inconsistent
        && !analysis.XOffsetConstant;
});

Test(415, "world coordinate report contains samples and analysis verdict", () =>
{
    var samples = new[]
    {
        MakeWorldSample(0, new Vector3(0f, 47f, 0f), new Vector3(0f, 14f, 0f)),
        MakeWorldSample(1, new Vector3(0f, 40f, 0f), new Vector3(0f, 14f, 0f)),
    };
    var report = string.Join("\n", TitleBackgroundWorldCoordinateCorrespondenceLogic.BuildReport(samples));
    return report.Contains("sampleCount=2", StringComparison.Ordinal)
        && report.Contains("verdict=linear-y-candidate", StringComparison.Ordinal)
        && report.Contains("focus-world=", StringComparison.Ordinal)
        && report.Contains("camera-focus=", StringComparison.Ordinal);
});

Test(417, "phase 0C samples auto-added on completion and cleared by reset", () =>
{
    var root = FindRepositoryRoot();
    var quickCheckText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.QuickCheck.cs"));
    var complete = ExtractMethodBody(quickCheckText, "private void CompleteAutomaticQuickCheck(bool partial)");
    var reset = ExtractMethodBody(quickCheckText, "internal bool ResetSimpleTitleBackgroundSettings()");
    return complete.Contains("TryAddWorldCoordinateSampleFromRun(", StringComparison.Ordinal)
        && reset.Contains("ClearWorldCoordinateSamples()", StringComparison.Ordinal);
});

Test(418, "phase 0C: persistent apply unlocked, fixOn stays supported-only, world still not ground verified", () =>
{
    // 2026-07-03: PersistentApplyEnabled は解禁(true)。fixOn の frame 対応判定と
    // ground provenance 判定は Evaluate gate 同様に不変であることをロックする。
    return TitleBackgroundExperimentalWorldPlacementLogic.PersistentApplyEnabled
        && !TitleBackgroundCharaSelectAnchorFrame.IsPlacementSupported(TitleBackgroundCharaSelectAnchorFrame.World)
        && !TitleBackgroundCharaSelectAnchorFrame.HasGroundProvenance("world")
        && !TitleBackgroundAutomaticCheckLogic.ResolveGroundPlacementVerified(true, "world-experimental", "world");
});

Test(419, "fixOn experiment block surfaces observed/override/pre-login diagnostics in summary", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground"), "TitleScreenBackgroundService*.cs").Select(File.ReadAllText));
    var body = ExtractMethodBody(serviceText, "private void AddCharacterPlacementPreLoginCaptureLines(List<string> lines)");

    // R0/R1/R2 で必要な比較値が要約に出ること。pre-login カメラ・observed・post-FixOn・generation 整合フラグ。
    return body.Contains("fixOn.exp.gateReason=", StringComparison.Ordinal)
        && body.Contains("fixOn.exp.observedCamera=", StringComparison.Ordinal)
        && body.Contains("fixOn.exp.observedFocus=", StringComparison.Ordinal)
        && body.Contains("fixOn.exp.observedCameraToFocus=", StringComparison.Ordinal)
        && body.Contains("fixOn.exp.anchorFrame=", StringComparison.Ordinal)
        && body.Contains("fixOn.exp.postFixOnCamera=", StringComparison.Ordinal)
        && body.Contains("fixOn.exp.preLoginCamera=", StringComparison.Ordinal)
        && body.Contains("fixOn.exp.preLoginCameraFrame=", StringComparison.Ordinal)
        && body.Contains("fixOn.exp.preLoginCameraGenerationMatchesFixOn=", StringComparison.Ordinal)
        && body.Contains("fixOn.exp.preLoginVsPostFixOnLookAt=", StringComparison.Ordinal);
});

Test(420, "fixOn experiment generation and context are held at capture time, not report time", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground"), "TitleScreenBackgroundService*.cs").Select(File.ReadAllText));
    var summary = ExtractMethodBody(serviceText, "private void AddCharacterPlacementPreLoginCaptureLines(List<string> lines)");

    // sceneGeneration / captureContext / charaSelectSession は発火時保持フィールド由来。
    // 報告時の active generation / IsLoggedIn / live session は charaSelectSession には使わない。
    return summary.Contains("fixOn.exp.sceneGeneration={_cameraObservation.FixOnExperimentSceneGeneration}", StringComparison.Ordinal)
        && summary.Contains("fixOn.exp.captureContext={FormatNone(_cameraObservation.FixOnExperimentCaptureContext)}", StringComparison.Ordinal)
        && summary.Contains("fixOn.exp.charaSelectSession={_cameraObservation.FixOnExperimentCharaSelectSession}", StringComparison.Ordinal)
        && !summary.Contains("fixOn.exp.charaSelectSession={_charaSelectTitleBackgroundSessionActive}", StringComparison.Ordinal)
        && !summary.Contains("fixOn.exp.sceneGeneration={_activeCharaSelectSceneGeneration}", StringComparison.Ordinal);
});

Test(421, "pre-login camera captured per frame; load resets experiment snapshot; detour holds gen/context", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var update = ExtractMethodBody(hooksText, "private void OnFrameworkUpdate(IFramework _)");
    var loadLobby = ExtractMethodBody(hooksText, "private void LoadLobbySceneDetour(GameLobbyType mapId)");
    var detour = ExtractMethodBody(hooksText, "private nint LobbyCameraFixOnDetour(nint self, float* cameraPos, float* focusPos, float fovY)");

    return update.Contains("CapturePreLoginCameraOnFrameworkUpdate()", StringComparison.Ordinal)
        && loadLobby.Contains("ResetFixOnExperimentSnapshot()", StringComparison.Ordinal)
        && detour.Contains("ComputeFixOnFocusOverrideGateReason()", StringComparison.Ordinal)
        && detour.Contains("_cameraObservation.FixOnExperimentSceneGeneration = _activeCharaSelectSceneGeneration", StringComparison.Ordinal);
});

Test(422, "pre-login camera capture gates on matching scene generation", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var capture = ExtractMethodBody(timelineText, "private void CapturePreLoginCameraOnFrameworkUpdate()");

    // active generation 正値 + adapter generation 一致のフレームのみ採用（別ロード値の混入防止）。
    return capture.Contains("_activeCharaSelectSceneGeneration <= 0", StringComparison.Ordinal)
        && capture.Contains("_charaSelectCameraAdapter.RuntimeState.SceneGeneration != _activeCharaSelectSceneGeneration", StringComparison.Ordinal);
});

Test(425, "brightness exploration classifies daylight time as daylight", () =>
{
    var snapshot = new TitleBackgroundEnvironmentSnapshot(true, "read", 12f * 3600f, 1, 0f);
    var result = TitleBackgroundBrightnessExplorationLogic.Evaluate(snapshot);
    return result.Daylight == TitleBackgroundEnvironmentDaylight.Daylight
        && result.BrightnessHint == "daylight"
        && !result.Rainy;
});

Test(426, "brightness exploration flags rainy daytime", () =>
{
    var snapshot = new TitleBackgroundEnvironmentSnapshot(true, "read", 12f * 3600f, 2, 0.5f);
    var result = TitleBackgroundBrightnessExplorationLogic.Evaluate(snapshot);
    return result.Daylight == TitleBackgroundEnvironmentDaylight.Daylight
        && result.Rainy
        && result.BrightnessHint == "daylight-but-rainy";
});

Test(427, "brightness exploration classifies midnight as night", () =>
{
    var snapshot = new TitleBackgroundEnvironmentSnapshot(true, "read", 0f, 0, 0f);
    var result = TitleBackgroundBrightnessExplorationLogic.Evaluate(snapshot);
    return result.Daylight == TitleBackgroundEnvironmentDaylight.Night
        && result.BrightnessHint == "night-dark"
        && result.ExplorationHint.Contains("layerFilterKey", StringComparison.Ordinal);
});

Test(428, "brightness exploration classifies dusk as twilight", () =>
{
    var snapshot = new TitleBackgroundEnvironmentSnapshot(true, "read", 18f * 3600f, 0, 0f);
    var result = TitleBackgroundBrightnessExplorationLogic.Evaluate(snapshot);
    return result.Daylight == TitleBackgroundEnvironmentDaylight.Twilight
        && result.BrightnessHint == "twilight-dim";
});

Test(429, "brightness exploration reports unavailable environment", () =>
{
    var result = TitleBackgroundBrightnessExplorationLogic.Evaluate(
        TitleBackgroundEnvironmentSnapshot.Unavailable("env-manager-null"));
    return result.Daylight == TitleBackgroundEnvironmentDaylight.Unknown
        && result.BrightnessHint == "unknown"
        && result.ExplorationHint.Contains("unavailable", StringComparison.Ordinal);
});

Test(430, "anchor capture gate is available only pre-login in chara select", () =>
{
    return TitleBackgroundAnchorCaptureGate.Evaluate(isLoggedIn: false, isCharaSelect: true)
            == TitleBackgroundAnchorCaptureAvailability.Available
        && TitleBackgroundAnchorCaptureGate.Evaluate(isLoggedIn: true, isCharaSelect: true)
            == TitleBackgroundAnchorCaptureAvailability.LoggedIn
        && TitleBackgroundAnchorCaptureGate.Evaluate(isLoggedIn: false, isCharaSelect: false)
            == TitleBackgroundAnchorCaptureAvailability.NotCharaSelect;
});

Test(431, "anchor capture gate enables only the available state", () =>
{
    return TitleBackgroundAnchorCaptureGate.IsCaptureEnabled(TitleBackgroundAnchorCaptureAvailability.Available)
        && !TitleBackgroundAnchorCaptureGate.IsCaptureEnabled(TitleBackgroundAnchorCaptureAvailability.LoggedIn)
        && !TitleBackgroundAnchorCaptureGate.IsCaptureEnabled(TitleBackgroundAnchorCaptureAvailability.NotCharaSelect);
});

Test(432, "layer step increments and decrements with zero floor", () =>
{
    return TitleBackgroundLayerStepLogic.Step(51, 1) == 52
        && TitleBackgroundLayerStepLogic.Step(51, -1) == 50
        && TitleBackgroundLayerStepLogic.Step(0, -1) == 0
        && TitleBackgroundLayerStepLogic.Step(7, 0) == 7;
});

Test(440, "environment noon override gate requires logged-out chara select session and ready hook state", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var body = ExtractMethodBody(timelineText, "private void MaintainCharaSelectEnvironmentNoon()");
    return body.Contains("TitleBackgroundEnvironmentNoonEnabled", StringComparison.Ordinal)
        && body.Contains("|| _clientState.IsLoggedIn", StringComparison.Ordinal)
        && body.Contains("_charaSelectTitleBackgroundSessionActive", StringComparison.Ordinal)
        && body.Contains("_hookLifecycle.State != TitleBackgroundServiceState.Ready", StringComparison.Ordinal)
        // candidate-specific 時刻ポリシー（FRU 白飛び対策）で解決した秒数を書く。
        && body.Contains("TitleBackgroundEnvironmentTimePolicy.ResolveDayTimeSeconds(", StringComparison.Ordinal)
        && body.Contains("TitleBackgroundEnvironmentNoonWriter.TryApplyDayTimeSeconds(", StringComparison.Ordinal);
});

Test(441, "environment noon toggle is absent from the normal title background screen", () =>
{
    var root = FindRepositoryRoot();
    var normalText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.TitleBackground.cs"));
    return !normalText.Contains("TitleBackgroundEnvironmentNoonEnabled", StringComparison.Ordinal)
        && !normalText.Contains("TitleBackgroundEnvironmentClearSkyEnabled", StringComparison.Ordinal);
});

Test(442, "environment time writer only writes DayTimeSeconds and never touches weather/exposure", () =>
{
    var root = FindRepositoryRoot();
    var writerText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundEnvironmentNoonWriter.cs"));
    var noonBody = ExtractMethodBody(writerText, "public static bool TryApplyNoon()");
    var dayTimeBody = ExtractMethodBody(writerText, "public static bool TryApplyDayTimeSeconds(float dayTimeSeconds)");
    return noonBody.Contains("TryApplyDayTimeSeconds(NoonDayTimeSeconds)", StringComparison.Ordinal)
        && dayTimeBody.Contains("manager->DayTimeSeconds = dayTimeSeconds", StringComparison.Ordinal)
        && dayTimeBody.Contains("dayTimeSeconds >= 86400f", StringComparison.Ordinal)
        && !dayTimeBody.Contains("ActiveWeather", StringComparison.Ordinal)
        && !dayTimeBody.Contains("EnvState", StringComparison.Ordinal)
        && !dayTimeBody.Contains("Exposure", StringComparison.Ordinal);
});

Test(444, "environment clear-sky override gate requires logged-out chara select session and ready hook state", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var body = ExtractMethodBody(timelineText, "private void MaintainCharaSelectEnvironmentClearSky()");
    return body.Contains("TitleBackgroundEnvironmentClearSkyEnabled", StringComparison.Ordinal)
        && body.Contains("|| _clientState.IsLoggedIn", StringComparison.Ordinal)
        && body.Contains("_charaSelectTitleBackgroundSessionActive", StringComparison.Ordinal)
        && body.Contains("_hookLifecycle.State != TitleBackgroundServiceState.Ready", StringComparison.Ordinal)
        && body.Contains("TitleBackgroundEnvironmentClearSkyWriter.TryApplyClearSky()", StringComparison.Ordinal);
});

Test(445, "environment clear-sky toggle is absent from the normal title background screen", () =>
{
    var root = FindRepositoryRoot();
    var normalText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.TitleBackground.cs"));
    return !normalText.Contains("TitleBackgroundEnvironmentClearSkyEnabled", StringComparison.Ordinal)
        && !normalText.Contains("TitleBackgroundEnvironmentNoonEnabled", StringComparison.Ordinal);
});

Test(446, "environment weather writers only write ActiveWeather to source-backed weather constants", () =>
{
    var root = FindRepositoryRoot();
    var writerText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundEnvironmentClearSkyWriter.cs"));
    var body = ExtractMethodBody(writerText, "public static bool TryApplyClearSky()");
    var fairBody = ExtractMethodBody(writerText, "public static bool TryApplyFairSky()");
    return body.Contains("manager->ActiveWeather = ClearSkiesWeatherId", StringComparison.Ordinal)
        && fairBody.Contains("manager->ActiveWeather = FairSkiesWeatherId", StringComparison.Ordinal)
        && writerText.Contains("public const byte ClearSkiesWeatherId = 1", StringComparison.Ordinal)
        && writerText.Contains("public const byte FairSkiesWeatherId = 2", StringComparison.Ordinal)
        && !body.Contains("DayTimeSeconds", StringComparison.Ordinal)
        && !body.Contains("EnvState", StringComparison.Ordinal)
        && !body.Contains("Exposure", StringComparison.Ordinal)
        && !body.Contains("TransitionTime", StringComparison.Ordinal)
        && !fairBody.Contains("DayTimeSeconds", StringComparison.Ordinal)
        && !fairBody.Contains("EnvState", StringComparison.Ordinal)
        && !fairBody.Contains("Exposure", StringComparison.Ordinal)
        && !fairBody.Contains("TransitionTime", StringComparison.Ordinal);
});

Test(447, "automatic diagnostic allowlist includes both noon and clear-sky environment override keys", () =>
{
    var selected = TitleBackgroundAutomaticCheckDiagnosticSelector.Select(
    [
        "environment.noonOverrideEnabled=True",
        "environment.noonOverrideAppliedFrameCount=3",
        "environment.noonOverrideLastStatus=applied",
        "environment.weatherCandidate=custom:ultima-thule-elpis",
        "environment.weatherRequestedId=2",
        "environment.weatherAppliedId=2",
        "environment.clearSkyOverrideEnabled=True",
        "environment.clearSkyOverrideAppliedFrameCount=3",
        "environment.clearSkyOverrideLastStatus=applied",
    ]);

    return selected.Count == 9
        && selected.Contains("environment.noonOverrideEnabled=True")
        && selected.Contains("environment.noonOverrideAppliedFrameCount=3")
        && selected.Contains("environment.noonOverrideLastStatus=applied")
        && selected.Contains("environment.weatherCandidate=custom:ultima-thule-elpis")
        && selected.Contains("environment.weatherRequestedId=2")
        && selected.Contains("environment.weatherAppliedId=2")
        && selected.Contains("environment.clearSkyOverrideEnabled=True")
        && selected.Contains("environment.clearSkyOverrideAppliedFrameCount=3")
        && selected.Contains("environment.clearSkyOverrideLastStatus=applied");
});

Test(570, "Elpis alone selects Fair Skies while existing candidates keep Clear Skies", () =>
{
    return TitleBackgroundEnvironmentWeatherPolicy.ResolveRequestedWeatherId(
               TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId)
               == TitleBackgroundEnvironmentClearSkyWriter.FairSkiesWeatherId
        && TitleBackgroundEnvironmentWeatherPolicy.ResolveRequestedWeatherId(
               TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId)
               == TitleBackgroundEnvironmentClearSkyWriter.ClearSkiesWeatherId
        && TitleBackgroundEnvironmentWeatherPolicy.ResolveRequestedWeatherId("custom:old-sharlayan")
               == TitleBackgroundEnvironmentClearSkyWriter.ClearSkiesWeatherId;
});

Test(571, "environment maintenance routes candidate weather through the existing writers", () =>
{
    var root = FindRepositoryRoot();
    var timelinePath = Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs");
    var timelineText = File.ReadAllText(timelinePath);
    var body = ExtractMethodBody(timelineText, "private void MaintainCharaSelectEnvironmentClearSky()");
    return body.Contains("TitleBackgroundEnvironmentWeatherPolicy.ResolveRequestedWeatherId(candidate.Id)", StringComparison.Ordinal)
        && body.Contains("TitleBackgroundEnvironmentClearSkyWriter.TryApplyFairSky()", StringComparison.Ordinal)
        && body.Contains("TitleBackgroundEnvironmentClearSkyWriter.TryApplyClearSky()", StringComparison.Ordinal)
        && body.Contains("_environmentClearSky.LastRequestedWeatherId", StringComparison.Ordinal)
        && body.Contains("_environmentClearSky.LastAppliedWeatherId", StringComparison.Ordinal);
});

Test(448, "runtime restore decision defers saved view pose to FixOn when pose is captured and candidate matches", () =>
{
    // trace実測: scene-readyでは焦点が原点のため書かず、自然FixOnが配置キャラへ焦点を確定した後へ委ねる。
    var poseView = new TitleBackgroundCharaSelectView(
        true, "custom:n4f4", new Vector3(1f, 14f, 3f), new Vector3(0f, 14.5f, 0f), 45f,
        PoseCaptured: true, DirH: 1.2f, DirV: -0.3f, Distance: 3.3f);
    var noPoseView = new TitleBackgroundCharaSelectView(
        true, "custom:n4f4", new Vector3(1f, 14f, 3f), new Vector3(0f, 14.5f, 0f), 45f);

    return TitleBackgroundFixOnViewOverrideLogic.ResolveRuntimeCameraRestoreDecision(false, true, poseView, "custom:n4f4")
            == TitleBackgroundFixOnViewOverrideLogic.RestoreDecisionDeferSavedViewPoseToFixOn
        // pose 無しの旧保存 view は従来どおり譲る（後方互換。再保存で pose 付きへ昇格）。
        && TitleBackgroundFixOnViewOverrideLogic.ResolveRuntimeCameraRestoreDecision(false, true, noPoseView, "custom:n4f4")
            == TitleBackgroundFixOnViewOverrideLogic.RestoreDecisionYieldNoPose
        && poseView.HasUsablePose
        && !noPoseView.HasUsablePose;
});

Test(449, "runtime restore decision proceeds with runtime restore when view disabled, mismatched, unusable, or suppressed", () =>
{
    var poseView = new TitleBackgroundCharaSelectView(
        true, "custom:n4f4", new Vector3(1f, 14f, 3f), new Vector3(0f, 14.5f, 0f), 45f,
        PoseCaptured: true, DirH: 1.2f, DirV: -0.3f, Distance: 3.3f);
    var disabledView = poseView with { Enabled = false };
    var nanView = new TitleBackgroundCharaSelectView(true, "custom:n4f4", new Vector3(float.NaN, 0f, 0f), new Vector3(0f, 0f, 0f), 45f);
    var emptyIdView = poseView with { CandidateId = string.Empty };
    var nanPoseView = poseView with { DirH = float.NaN };
    var zeroDistancePoseView = poseView with { Distance = 0f };
    const string Proceed = TitleBackgroundFixOnViewOverrideLogic.RestoreDecisionProceedRuntimeRestore;

    string Decide(bool suppressed, bool enabled, TitleBackgroundCharaSelectView view, string? candidateId) =>
        TitleBackgroundFixOnViewOverrideLogic.ResolveRuntimeCameraRestoreDecision(suppressed, enabled, view, candidateId);

    return Decide(false, false, poseView, "custom:n4f4") == Proceed
        && Decide(false, true, poseView, "manual:slot1") == Proceed
        && Decide(false, true, disabledView, "custom:n4f4") == Proceed
        && Decide(false, true, nanView, "custom:n4f4") == Proceed
        && Decide(false, true, emptyIdView, "anything") == Proceed
        // 自動確認 run 中の抑止は成立している view でも proceed（自然 FixOn でその場を写す）。
        && Decide(true, true, poseView, "custom:n4f4") == Proceed
        // run 外（suppressed=false）では遅延poseが予約される＝抑止がrunの外へ漏れないことの裏面。
        && Decide(false, true, poseView, "custom:n4f4") == TitleBackgroundFixOnViewOverrideLogic.RestoreDecisionDeferSavedViewPoseToFixOn
        // pose だけが壊れている view は yield へフォールバック（view 自体は成立している）。
        && Decide(false, true, nanPoseView, "custom:n4f4") == TitleBackgroundFixOnViewOverrideLogic.RestoreDecisionYieldNoPose
        && Decide(false, true, zeroDistancePoseView, "custom:n4f4") == TitleBackgroundFixOnViewOverrideLogic.RestoreDecisionYieldNoPose;
});

Test(450, "runtime restore view involvement matches FixOn view override success condition exactly", () =>
{
    // restore 決定が「view 関与（pose 適用 or yield）」になる条件と、FixOn 側 Resolve の
    // ShouldOverride は、同じ入力（抑止なし）に対して常に一致しなければならない（非対称禁止）。
    var matchingView = new TitleBackgroundCharaSelectView(
        true, "custom:n4f4", new Vector3(1f, 14f, 3f), new Vector3(0f, 14.5f, 0f), 45f,
        PoseCaptured: true, DirH: 1.2f, DirV: -0.3f, Distance: 3.3f);
    var observedCam = new Vector3(2f, 2f, 2f);
    var observedFocus = new Vector3(0f, 14f, 0f);

    bool Matches(bool enabled, TitleBackgroundCharaSelectView view, string? candidateId)
    {
        var decision = TitleBackgroundFixOnViewOverrideLogic.ResolveRuntimeCameraRestoreDecision(false, enabled, view, candidateId);
        var viewInvolved = decision != TitleBackgroundFixOnViewOverrideLogic.RestoreDecisionProceedRuntimeRestore;
        var resolve = TitleBackgroundFixOnViewOverrideLogic.Resolve(enabled, view, candidateId, observedCam, observedFocus, 60f);
        return viewInvolved == resolve.ShouldOverride;
    }

    return Matches(true, matchingView, "custom:n4f4")
        && Matches(false, matchingView, "custom:n4f4")
        && Matches(true, matchingView, "manual:slot1")
        && Matches(true, matchingView with { Enabled = false }, "custom:n4f4")
        && Matches(true, matchingView with { PoseCaptured = false }, "custom:n4f4")
        && Matches(true, new TitleBackgroundCharaSelectView(true, string.Empty, matchingView.Camera, matchingView.Focus, matchingView.FovY), "anything");
});

Test(451, "runtime restore defers saved pose before runtime restore without writing at scene-ready", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    var body = ExtractMethodBody(serviceText, "private void RestoreCharaSelectRuntimeCameraStateAfterSceneLoad()");
    var decisionIndex = body.IndexOf("TitleBackgroundFixOnViewOverrideLogic.ResolveRuntimeCameraRestoreDecision", StringComparison.Ordinal);
    var applyIndex = body.IndexOf("TryApplyRuntimeCameraPose(", StringComparison.Ordinal);
    var deferIndex = body.IndexOf("\"deferred-to-fixon-pose\"", StringComparison.Ordinal);

    // saved-view 決定は先頭近く（attempt カウント直後）にあり、runtime state の pose 書込みより必ず前に
    // 評価される。pose付きはFixOnへ委ね、scene-readyでは保存poseを書かない。
    return decisionIndex >= 0
        && applyIndex >= 0
        && decisionIndex < applyIndex
        && deferIndex >= 0
        && deferIndex < applyIndex
        && !body.Contains("ApplySavedViewCameraPoseAfterFixOn(", StringComparison.Ordinal)
        && body.Contains("_configuration.TitleBackgroundCharaSelectViewEnabled", StringComparison.Ordinal)
        && body.Contains("BuildCharaSelectView()", StringComparison.Ordinal)
        && body.Contains("ResolveCurrentOverrideCandidate().Id", StringComparison.Ordinal)
        && body.Contains("IsSavedViewSuppressedByAutomaticRun()", StringComparison.Ordinal)
        && body.Contains("\"yielded-to-saved-view-no-pose\"", StringComparison.Ordinal);
});

Test(452, "runtime restore saved-view decision uses identical arguments as FixOn view override gate", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var restoreBody = ExtractMethodBody(serviceText, "private void RestoreCharaSelectRuntimeCameraStateAfterSceneLoad()");
    var fixOnBody = ExtractMethodBody(hooksText, "private nint LobbyCameraFixOnDetour(nint self, float* cameraPos, float* focusPos, float fovY)");

    // 両経路とも同じ3要素（view有効フラグ / BuildCharaSelectView() / ResolveCurrentOverrideCandidate().Id）と
    // 同じ run 抑止判定（IsSavedViewSuppressedByAutomaticRun）を使う。
    return restoreBody.Contains("_configuration.TitleBackgroundCharaSelectViewEnabled", StringComparison.Ordinal)
        && restoreBody.Contains("BuildCharaSelectView()", StringComparison.Ordinal)
        && restoreBody.Contains("ResolveCurrentOverrideCandidate().Id", StringComparison.Ordinal)
        && restoreBody.Contains("IsSavedViewSuppressedByAutomaticRun()", StringComparison.Ordinal)
        && fixOnBody.Contains("_configuration.TitleBackgroundCharaSelectViewEnabled", StringComparison.Ordinal)
        && fixOnBody.Contains("BuildCharaSelectView()", StringComparison.Ordinal)
        && fixOnBody.Contains("ResolveCurrentOverrideCandidate().Id", StringComparison.Ordinal)
        && fixOnBody.Contains("IsSavedViewSuppressedByAutomaticRun()", StringComparison.Ordinal);
});

Test(453, "view replay trace sampling frames cover dense 1-10 then sparse checkpoints up to a fixed cap", () =>
{
    var frames = TitleBackgroundViewReplayTraceLogic.SamplingFrames;
    var dense = Enumerable.Range(1, 10).ToArray();
    var sparse = new[] { 15, 20, 30, 45, 60, 90, 120 };
    return frames.Length == dense.Length + sparse.Length
        && dense.All(frame => Array.IndexOf(frames, frame) >= 0)
        && sparse.All(frame => Array.IndexOf(frames, frame) >= 0)
        && frames.SequenceEqual(frames.OrderBy(frame => frame))
        && TitleBackgroundViewReplayTraceLogic.IsTraceComplete(frames[^1])
        && !TitleBackgroundViewReplayTraceLogic.IsTraceComplete(frames[^1] - 1);
});

Test(454, "view replay trace should-capture gate accepts frame 0 and only listed sampling frames", () =>
{
    return TitleBackgroundViewReplayTraceLogic.ShouldCaptureAtFrame(0)
        && TitleBackgroundViewReplayTraceLogic.ShouldCaptureAtFrame(1)
        && TitleBackgroundViewReplayTraceLogic.ShouldCaptureAtFrame(10)
        && TitleBackgroundViewReplayTraceLogic.ShouldCaptureAtFrame(15)
        && TitleBackgroundViewReplayTraceLogic.ShouldCaptureAtFrame(120)
        && !TitleBackgroundViewReplayTraceLogic.ShouldCaptureAtFrame(11)
        && !TitleBackgroundViewReplayTraceLogic.ShouldCaptureAtFrame(14)
        && !TitleBackgroundViewReplayTraceLogic.ShouldCaptureAtFrame(121)
        && !TitleBackgroundViewReplayTraceLogic.ShouldCaptureAtFrame(-1);
});

Test(455, "view replay trace divergence reports none when every sample matches target within tolerance", () =>
{
    var target = new Vector3(-389.386f, 57.06f, 509.873f);
    var focus = new Vector3(-388.334f, 56.921f, 513.781f);
    var samples = new[]
    {
        new TitleBackgroundViewReplayTraceSample(0, true, target, focus, 0.78f, "success", string.Empty),
        new TitleBackgroundViewReplayTraceSample(1, true, target, focus, 0.78f, "success", string.Empty),
        new TitleBackgroundViewReplayTraceSample(2, true, target + new Vector3(0.01f, 0f, 0f), focus, 0.78f, "success", string.Empty),
    };

    var result = TitleBackgroundViewReplayTraceLogic.EvaluateFirstDivergence(target, focus, 0.78f, samples);
    return !result.Diverged
        && result.DivergedAtFrame == null
        && result.Component == TitleBackgroundViewReplayTraceComponent.None;
});

Test(456, "view replay trace divergence finds first frame and camera component when camera drifts beyond tolerance", () =>
{
    var target = new Vector3(-389.386f, 57.06f, 509.873f);
    var focus = new Vector3(-388.334f, 56.921f, 513.781f);
    var samples = new[]
    {
        new TitleBackgroundViewReplayTraceSample(0, true, target, focus, 0.78f, "success", string.Empty),
        new TitleBackgroundViewReplayTraceSample(1, true, target, focus, 0.78f, "success", string.Empty),
        new TitleBackgroundViewReplayTraceSample(2, true, target + new Vector3(1.5f, 0f, 0f), focus, 0.78f, "success", string.Empty),
        new TitleBackgroundViewReplayTraceSample(3, true, target + new Vector3(3f, 0f, 0f), focus, 0.78f, "success", string.Empty),
    };

    var result = TitleBackgroundViewReplayTraceLogic.EvaluateFirstDivergence(target, focus, 0.78f, samples);
    return result.Diverged
        && result.DivergedAtFrame == 2
        && result.Component == TitleBackgroundViewReplayTraceComponent.Camera
        && result.Magnitude > 1f;
});

Test(457, "view replay trace divergence finds lookAt-only drift when camera stays fixed", () =>
{
    var target = new Vector3(-389.386f, 57.06f, 509.873f);
    var focus = new Vector3(-388.334f, 56.921f, 513.781f);
    var samples = new[]
    {
        new TitleBackgroundViewReplayTraceSample(0, true, target, focus, 0.78f, "success", string.Empty),
        new TitleBackgroundViewReplayTraceSample(1, true, target, focus + new Vector3(0f, 2f, 0f), 0.78f, "success", string.Empty),
    };

    var result = TitleBackgroundViewReplayTraceLogic.EvaluateFirstDivergence(target, focus, 0.78f, samples);
    return result.Diverged
        && result.DivergedAtFrame == 1
        && result.Component == TitleBackgroundViewReplayTraceComponent.LookAt;
});

Test(458, "view replay trace divergence finds fovY-only drift when camera and lookAt stay fixed", () =>
{
    var target = new Vector3(-389.386f, 57.06f, 509.873f);
    var focus = new Vector3(-388.334f, 56.921f, 513.781f);
    var samples = new[]
    {
        new TitleBackgroundViewReplayTraceSample(0, true, target, focus, 0.78f, "success", string.Empty),
        new TitleBackgroundViewReplayTraceSample(1, true, target, focus, 1.2f, "success", string.Empty),
    };

    var result = TitleBackgroundViewReplayTraceLogic.EvaluateFirstDivergence(target, focus, 0.78f, samples);
    return result.Diverged
        && result.DivergedAtFrame == 1
        && result.Component == TitleBackgroundViewReplayTraceComponent.FovY;
});

Test(459, "view replay trace divergence skips uncaptured samples and non-finite targets safely", () =>
{
    var target = new Vector3(-389.386f, 57.06f, 509.873f);
    var focus = new Vector3(-388.334f, 56.921f, 513.781f);
    var samples = new[]
    {
        new TitleBackgroundViewReplayTraceSample(0, false, null, null, null, "failed", "active camera unavailable"),
        new TitleBackgroundViewReplayTraceSample(1, true, target, focus, 0.78f, "success", string.Empty),
    };

    // captured=false のサンプルは判定から除外され、null target では例外にならず「乖離なし」を返す。
    var resultWithGoodSamples = TitleBackgroundViewReplayTraceLogic.EvaluateFirstDivergence(target, focus, 0.78f, samples);
    var resultWithNullTarget = TitleBackgroundViewReplayTraceLogic.EvaluateFirstDivergence(null, null, null, samples);
    return !resultWithGoodSamples.Diverged
        && !resultWithNullTarget.Diverged;
});

Test(460, "view replay trace runtime state resets all fields including active samples", () =>
{
    var state = new TitleBackgroundViewReplayTraceRuntimeState
    {
        TraceSceneGeneration = 4,
        Source = "saved-view-pose",
        StartedDuringAutomaticRun = true,
        TargetCamera = new Vector3(1f, 2f, 3f),
        TargetFocus = new Vector3(4f, 5f, 6f),
        TargetFovY = 0.78f,
        TargetDirH = 1.2f,
        TargetDirV = -0.3f,
        TargetDistance = 3.3f,
        RelativeFrameCounter = 3,
        StartAbsoluteFrame = 42,
        StartLookAtYCallCount = 7,
        StartCurveSetMidCallCount = 5,
        StartCurveLowHighCallCount = 5,
        PoseApplyAbsoluteFrame = 42,
        FixOnApplyAbsoluteFrame = 67,
        Status = "collecting",
    };
    state.Samples[0] = new TitleBackgroundViewReplayTraceSample(0, true, Vector3.Zero, Vector3.Zero, 0.78f, "success", string.Empty);

    state.Reset();

    return state.TraceSceneGeneration == 0
        && state.Source == "not-run"
        && !state.StartedDuringAutomaticRun
        && state.TargetCamera == null
        && state.TargetFocus == null
        && state.TargetFovY == null
        && state.TargetDirH == null
        && state.TargetDirV == null
        && state.TargetDistance == null
        && state.RelativeFrameCounter == -1
        && state.StartAbsoluteFrame == null
        && state.StartLookAtYCallCount == 0
        && state.StartCurveSetMidCallCount == 0
        && state.StartCurveLowHighCallCount == 0
        && state.PoseApplyAbsoluteFrame == null
        && state.FixOnApplyAbsoluteFrame == null
        && state.Samples.Count == 0
        && state.Status == "not-run"
        && !state.IsActive;
});

Test(461, "FixOn detour starts view replay trace only when view override was applied this invocation, after post-FixOn capture", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var body = ExtractMethodBody(hooksText, "private nint LobbyCameraFixOnDetour(nint self, float* cameraPos, float* focusPos, float fovY)");
    var captureIndex = body.IndexOf("CapturePostFixOnCameraState();", StringComparison.Ordinal);
    var startTraceIndex = body.IndexOf("StartViewReplayTraceIfApplicable(viewOverrideAppliedThisInvocation);", StringComparison.Ordinal);
    var viewOverrideFlagSetIndex = body.IndexOf("viewOverrideAppliedThisInvocation = true;", StringComparison.Ordinal);

    return captureIndex >= 0
        && startTraceIndex >= 0
        && captureIndex < startTraceIndex
        && viewOverrideFlagSetIndex >= 0
        && body.Contains("viewOverrideAppliedThisInvocation = false;", StringComparison.Ordinal);
});

Test(462, "view replay trace framework-update capture is gated identically to pre-login camera capture", () =>
{
    var root = FindRepositoryRoot();
    var traceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.ViewReplayTrace.cs"));
    var body = ExtractMethodBody(traceText, "private void CaptureViewReplayTraceOnFrameworkUpdate()");

    // 既存 CapturePreLoginCameraOnFrameworkUpdate と同じゲート要素（pre-login, session active, generation一致）を使う。
    return body.Contains("_clientState.IsLoggedIn", StringComparison.Ordinal)
        && body.Contains("_charaSelectTitleBackgroundSessionActive", StringComparison.Ordinal)
        && body.Contains("_viewReplayTrace.TraceSceneGeneration <= 0", StringComparison.Ordinal)
        && body.Contains("_charaSelectCameraAdapter.RuntimeState.SceneGeneration != _viewReplayTrace.TraceSceneGeneration", StringComparison.Ordinal);
});

Test(463, "OnFrameworkUpdate invokes view replay trace capture alongside existing pre-login camera capture", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var body = ExtractMethodBody(hooksText, "private void OnFrameworkUpdate(IFramework _)");
    var preLoginIndex = body.IndexOf("CapturePreLoginCameraOnFrameworkUpdate();", StringComparison.Ordinal);
    var traceIndex = body.IndexOf("CaptureViewReplayTraceOnFrameworkUpdate();", StringComparison.Ordinal);
    return preLoginIndex >= 0 && traceIndex >= 0 && preLoginIndex < traceIndex;
});

Test(464, "load-scoped reset delegates view trace preservation and collision handling to runtime state", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    var body = ExtractMethodBody(serviceText, "private void ResetFixOnExperimentSnapshot()");
    return body.Contains("_viewReplayTrace.PrepareForSceneLoad(", StringComparison.Ordinal)
        && body.Contains("_activeCharaSelectSceneGeneration", StringComparison.Ordinal)
        && body.Contains("preserveSameGenerationTraceForSuppressedRun: _automaticCheck.Requested", StringComparison.Ordinal)
        && !body.Contains("_viewReplayTrace.Reset();", StringComparison.Ordinal);
});

Test(465, "automatic diagnostic allowlist includes view replay trace summary keys and dynamic per-frame sample keys", () =>
{
    var selected = TitleBackgroundAutomaticCheckDiagnosticSelector.Select(
    [
        "view.trace.status=complete",
        "view.trace.sceneGeneration=4",
        "view.trace.fromCurrentRun=False",
        "view.trace.source=saved-view-pose",
        "view.trace.poseApplyAbsoluteFrame=12",
        "view.trace.diverged=True",
        "view.trace.divergedAtFrame=2",
        "view.trace.divergedComponent=LookAt",
        "view.trace.divergedMagnitude=1.2",
        "view.trace.sample[0].status=success",
        "view.trace.sample[0].camera=(1.000, 2.000, 3.000)",
        "view.trace.sample[0].dirH=2.793",
        "view.trace.sample[0].dirV=0.053",
        "view.trace.sample[0].distance=5.5",
        "view.trace.sample[120].fovY=0.78",
        "view.trace.sample[999].status=success",
        "unrelated.key=value",
    ]);

    return selected.Count == 15
        && selected.Contains("view.trace.status=complete")
        && selected.Contains("view.trace.fromCurrentRun=False")
        && selected.Contains("view.trace.source=saved-view-pose")
        && selected.Contains("view.trace.poseApplyAbsoluteFrame=12")
        && selected.Contains("view.trace.diverged=True")
        && selected.Contains("view.trace.sample[0].status=success")
        && selected.Contains("view.trace.sample[0].camera=(1.000, 2.000, 3.000)")
        && selected.Contains("view.trace.sample[0].dirH=2.793")
        && selected.Contains("view.trace.sample[0].dirV=0.053")
        && selected.Contains("view.trace.sample[0].distance=5.5")
        && selected.Contains("view.trace.sample[120].fovY=0.78")
        && !selected.Contains("view.trace.sample[999].status=success")
        && !selected.Contains("unrelated.key=value");
});

Test(466, "automatic diagnostic allowlist includes trace start baseline keys and per-sample absolute-frame and hook-counter keys", () =>
{
    var selected = TitleBackgroundAutomaticCheckDiagnosticSelector.Select(
    [
        "view.trace.startAbsoluteFrame=12",
        "view.trace.startLookAtYCallCount=3",
        "view.trace.startCurveSetMidCallCount=2",
        "view.trace.startCurveLowHighCallCount=2",
        "view.trace.sample[0].absoluteFrame=12",
        "view.trace.sample[0].lookAtYCalls=3",
        "view.trace.sample[0].curveSetMidCalls=2",
        "view.trace.sample[0].curveLowHighCalls=2",
        "view.trace.sample[120].absoluteFrame=132",
        "view.trace.sample[120].lookAtYCalls=45",
        "view.trace.sample[999].absoluteFrame=1000",
        "view.trace.sample[999].lookAtYCalls=99",
    ]);

    return selected.Count == 10
        && selected.Contains("view.trace.startAbsoluteFrame=12")
        && selected.Contains("view.trace.startLookAtYCallCount=3")
        && selected.Contains("view.trace.startCurveSetMidCallCount=2")
        && selected.Contains("view.trace.startCurveLowHighCallCount=2")
        && selected.Contains("view.trace.sample[0].absoluteFrame=12")
        && selected.Contains("view.trace.sample[0].lookAtYCalls=3")
        && selected.Contains("view.trace.sample[0].curveSetMidCalls=2")
        && selected.Contains("view.trace.sample[0].curveLowHighCalls=2")
        && selected.Contains("view.trace.sample[120].absoluteFrame=132")
        && selected.Contains("view.trace.sample[120].lookAtYCalls=45")
        && !selected.Contains("view.trace.sample[999].absoluteFrame=1000")
        && !selected.Contains("view.trace.sample[999].lookAtYCalls=99");
});

Test(467, "view replay trace records absolute frame and hook-counter baselines at start and stamps every sample with counters", () =>
{
    var root = FindRepositoryRoot();
    var traceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.ViewReplayTrace.cs"));
    var startBody = ExtractMethodBody(traceText, "private void InitializeViewReplayTrace(");
    var sampleBody = ExtractMethodBody(traceText, "private TitleBackgroundViewReplayTraceSample CaptureViewReplayTraceSample(int relativeFrame)");

    // trace開始時: 絶対フレーム（Phase2C基準）とフックカウンタのベースラインを記録する。
    // 各サンプル採取時: 採取時点の絶対フレームと累計カウンタをサンプルへ格納する（区間差分ブラケット用）。
    return startBody.Contains("_viewReplayTrace.StartAbsoluteFrame = GetCurrentPhase2CFrame();", StringComparison.Ordinal)
        && startBody.Contains("_viewReplayTrace.StartLookAtYCallCount = _phaseRecording.Phase2ECalculateLookAtYCallCount;", StringComparison.Ordinal)
        && startBody.Contains("_viewReplayTrace.StartCurveSetMidCallCount = _phaseRecording.Phase2FSetCameraCurveMidPointCallCount;", StringComparison.Ordinal)
        && startBody.Contains("_viewReplayTrace.StartCurveLowHighCallCount = _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointCallCount;", StringComparison.Ordinal)
        && sampleBody.Contains("var absoluteFrame = GetCurrentPhase2CFrame();", StringComparison.Ordinal)
        && sampleBody.Contains("var lookAtYCalls = _phaseRecording.Phase2ECalculateLookAtYCallCount;", StringComparison.Ordinal)
        && sampleBody.Contains("var curveSetMidCalls = _phaseRecording.Phase2FSetCameraCurveMidPointCallCount;", StringComparison.Ordinal)
        && sampleBody.Contains("var curveLowHighCalls = _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointCallCount;", StringComparison.Ordinal);
});

Test(468, "configuration saved view pose fields default off and round-trip through both serializers", () =>
{
    // Dalamud SavePluginConfig は Newtonsoft.Json、ExportToBase64 は System.Text.Json を使うため、
    // 新規 pose 4 項目が両シリアライザで round-trip することを固定する。既定は PoseCaptured=false（挙動不変）。
    var defaultConfiguration = new Configuration();
    var configuration = new Configuration
    {
        TitleBackgroundCharaSelectViewPoseCaptured = true,
        TitleBackgroundCharaSelectViewDirH = 1.25f,
        TitleBackgroundCharaSelectViewDirV = -0.375f,
        TitleBackgroundCharaSelectViewDistance = 3.3f,
    };

    var systemJson = JsonSerializer.Serialize(configuration);
    var systemRestored = JsonSerializer.Deserialize<Configuration>(systemJson);
    var newtonsoftJson = Newtonsoft.Json.JsonConvert.SerializeObject(configuration);
    var newtonsoftRestored = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(newtonsoftJson);

    return !defaultConfiguration.TitleBackgroundCharaSelectViewPoseCaptured
        && defaultConfiguration.TitleBackgroundCharaSelectViewDistance == 0f
        && systemJson.Contains("\"TitleBackgroundCharaSelectViewPoseCaptured\"", StringComparison.Ordinal)
        && systemJson.Contains("\"TitleBackgroundCharaSelectViewDirH\"", StringComparison.Ordinal)
        && systemJson.Contains("\"TitleBackgroundCharaSelectViewDirV\"", StringComparison.Ordinal)
        && systemJson.Contains("\"TitleBackgroundCharaSelectViewDistance\"", StringComparison.Ordinal)
        && systemRestored != null
        && systemRestored.TitleBackgroundCharaSelectViewPoseCaptured
        && Math.Abs(systemRestored.TitleBackgroundCharaSelectViewDirH - 1.25f) < 0.0001f
        && Math.Abs(systemRestored.TitleBackgroundCharaSelectViewDirV - (-0.375f)) < 0.0001f
        && Math.Abs(systemRestored.TitleBackgroundCharaSelectViewDistance - 3.3f) < 0.0001f
        && newtonsoftJson.Contains("\"TitleBackgroundCharaSelectViewPoseCaptured\"", StringComparison.Ordinal)
        && newtonsoftRestored != null
        && newtonsoftRestored.TitleBackgroundCharaSelectViewPoseCaptured
        && Math.Abs(newtonsoftRestored.TitleBackgroundCharaSelectViewDirH - 1.25f) < 0.0001f
        && Math.Abs(newtonsoftRestored.TitleBackgroundCharaSelectViewDirV - (-0.375f)) < 0.0001f
        && Math.Abs(newtonsoftRestored.TitleBackgroundCharaSelectViewDistance - 3.3f) < 0.0001f;
});

Test(469, "configuration normalization disables saved view pose on non-finite or non-positive values without dropping the view itself", () =>
{
    // 正規化は ApplyFrom の末尾（NormalizeAndMigrate）で走る（Test 7 と同じ経路）。
    Configuration NormalizeVia(Configuration source)
    {
        var target = new Configuration();
        target.ApplyFrom(source);
        return target;
    }

    // Distance=NaN は ApplyFrom コピー時に SanitizeCoordinate で 0 へ丸まり、normalize の
    // 「Distance <= 0 → pose 無効化」で確実に落ちる。DirH/DirV の NaN は同じ Sanitize で 0（正当な
    // 有限値）へ丸まるため pose は残るが、非有限値が LobbyCamera へ書かれない防御は
    // HasUsablePose（Test 449 の nanPoseView）が最終ゲートとして担う（多層防御）。
    var nanDistancePose = NormalizeVia(new Configuration
    {
        TitleBackgroundCharaSelectViewEnabled = true,
        TitleBackgroundCharaSelectViewCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectViewCameraX = 1f,
        TitleBackgroundCharaSelectViewCameraY = 14f,
        TitleBackgroundCharaSelectViewCameraZ = 3f,
        TitleBackgroundCharaSelectViewFocusX = 0f,
        TitleBackgroundCharaSelectViewFocusY = 14.5f,
        TitleBackgroundCharaSelectViewFocusZ = 0f,
        TitleBackgroundCharaSelectViewFovY = 0.78f,
        TitleBackgroundCharaSelectViewPoseCaptured = true,
        TitleBackgroundCharaSelectViewDirH = 1f,
        TitleBackgroundCharaSelectViewDirV = 0f,
        TitleBackgroundCharaSelectViewDistance = float.NaN,
    });
    var zeroDistancePose = NormalizeVia(new Configuration
    {
        TitleBackgroundCharaSelectViewPoseCaptured = true,
        TitleBackgroundCharaSelectViewDirH = 1f,
        TitleBackgroundCharaSelectViewDirV = 0f,
        TitleBackgroundCharaSelectViewDistance = 0f,
    });
    var validPose = NormalizeVia(new Configuration
    {
        TitleBackgroundCharaSelectViewPoseCaptured = true,
        TitleBackgroundCharaSelectViewDirH = 1.2f,
        TitleBackgroundCharaSelectViewDirV = -0.3f,
        TitleBackgroundCharaSelectViewDistance = 3.3f,
    });

    // pose が壊れていても view 本体（camera/focus 形式）は独立して残る（enabled は落ちない）。
    return !nanDistancePose.TitleBackgroundCharaSelectViewPoseCaptured
        && nanDistancePose.TitleBackgroundCharaSelectViewEnabled
        && !zeroDistancePose.TitleBackgroundCharaSelectViewPoseCaptured
        && validPose.TitleBackgroundCharaSelectViewPoseCaptured
        && Math.Abs(validPose.TitleBackgroundCharaSelectViewDirH - 1.2f) < 0.0001f
        && Math.Abs(validPose.TitleBackgroundCharaSelectViewDistance - 3.3f) < 0.0001f;
});

Test(470, "recovery snapshot round-trips saved view pose fields and old journals default to no pose", () =>
{
    var source = new Configuration
    {
        TitleBackgroundCharaSelectViewPoseCaptured = true,
        TitleBackgroundCharaSelectViewDirH = 1.2f,
        TitleBackgroundCharaSelectViewDirV = -0.3f,
        TitleBackgroundCharaSelectViewDistance = 3.3f,
    };
    var snapshot = TitleBackgroundAutomaticCheckSettingsSnapshot.Capture(source);
    var dest = new Configuration();
    snapshot.ApplyTo(dest);

    // 旧 journal（pose キー無し）は既定値（PoseCaptured=false）で復元される（fail-closed）。
    var oldJournalJson = """
        {
          "SchemaVersion": 1,
          "RunId": "old-run",
          "StartedAt": "2026-07-03T12:00:00+09:00",
          "OriginalSettings": { "ViewEnabled": true, "ViewCandidateId": "custom:n4f4" }
        }
        """;
    var oldJournal = TitleBackgroundAutomaticCheckRecoveryJournal.Deserialize(oldJournalJson);

    return dest.TitleBackgroundCharaSelectViewPoseCaptured
        && Math.Abs(dest.TitleBackgroundCharaSelectViewDirH - 1.2f) < 0.0001f
        && Math.Abs(dest.TitleBackgroundCharaSelectViewDirV - (-0.3f)) < 0.0001f
        && Math.Abs(dest.TitleBackgroundCharaSelectViewDistance - 3.3f) < 0.0001f
        && oldJournal != null
        && !oldJournal.OriginalSettings.ViewPoseCaptured
        && oldJournal.OriginalSettings.ViewEnabled;
});

Test(471, "saved view capture reads native pose from the shared camera snapshot without trigonometric conversion", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var body = ExtractMethodBody(timelineText, "public bool TryCaptureCharaSelectViewFromCurrentCamera(out string status)");

    // pose は TryCaptureActiveCameraSnapshot（runtime restore の書込先 LobbyCamera->DirH/DirV/Distance と
    // 同一フィールド群の read 経路）から取り、自前の三角関数変換（規約推定）は行わない。
    return body.Contains("TryCaptureActiveCameraSnapshot(out var snapshot", StringComparison.Ordinal)
        && body.Contains("snapshot.DirH", StringComparison.Ordinal)
        && body.Contains("snapshot.DirV", StringComparison.Ordinal)
        && body.Contains("snapshot.Distance", StringComparison.Ordinal)
        && !body.Contains("Math.Atan", StringComparison.Ordinal)
        && !body.Contains("MathF.Atan", StringComparison.Ordinal)
        && !body.Contains("Math.Asin", StringComparison.Ordinal)
        && !body.Contains("MathF.Asin", StringComparison.Ordinal)
        && !body.Contains("TryBuildPoseFromCameraFocus", StringComparison.Ordinal);
});

Test(472, "delayed saved view pose shares the lobby camera write core and stays outside framework update", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var runtimeApplyBody = ExtractMethodBody(serviceText, "private bool TryApplyRuntimeCameraPose(");
    var coreBody = ExtractMethodBody(serviceText, "private bool TryApplyLobbyCameraPose(");
    var savedViewApplyBody = ExtractMethodBody(serviceText, "private bool ApplySavedViewCameraPoseAfterFixOn(TitleBackgroundCharaSelectView savedView)");
    var frameworkUpdateBody = ExtractMethodBody(hooksText, "private void OnFrameworkUpdate(IFramework _)");

    // runtime restoreと保存poseが同じ入力param書込コアを使い、無差別なFramework.Update経路には接続しない。
    return runtimeApplyBody.Contains("TryApplyLobbyCameraPose(", StringComparison.Ordinal)
        && savedViewApplyBody.Contains("TryApplyLobbyCameraPose(", StringComparison.Ordinal)
        && coreBody.Contains("lobbyCamera->DirH = dirH;", StringComparison.Ordinal)
        && coreBody.Contains("lobbyCamera->DirV = dirV;", StringComparison.Ordinal)
        && coreBody.Contains("lobbyCamera->Distance = distance;", StringComparison.Ordinal)
        && coreBody.Contains("lobbyCamera->InterpDistance = distance;", StringComparison.Ordinal)
        && coreBody.Contains("lobbyCamera->FoV = fovY;", StringComparison.Ordinal)
        && !frameworkUpdateBody.Contains("TryApplyLobbyCameraPose", StringComparison.Ordinal)
        && !frameworkUpdateBody.Contains("ApplySavedViewCameraPoseAfterFixOn", StringComparison.Ordinal)
        && !frameworkUpdateBody.Contains("TryApplyRuntimeCameraPose", StringComparison.Ordinal);
});

Test(473, "simple reset clears saved view pose fields together with the view itself", () =>
{
    var root = FindRepositoryRoot();
    var quickCheckText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.QuickCheck.cs"));
    var body = ExtractMethodBody(quickCheckText, "internal bool ResetSimpleTitleBackgroundSettings()");
    return body.Contains("TitleBackgroundCharaSelectViewPoseCaptured = false", StringComparison.Ordinal)
        && body.Contains("TitleBackgroundCharaSelectViewDirH = 0f", StringComparison.Ordinal)
        && body.Contains("TitleBackgroundCharaSelectViewDirV = 0f", StringComparison.Ordinal)
        && body.Contains("TitleBackgroundCharaSelectViewDistance = 0f", StringComparison.Ordinal);
});

Test(474, "automatic diagnostic allowlist includes saved view pose keys and run suppression flag", () =>
{
    var selected = TitleBackgroundAutomaticCheckDiagnosticSelector.Select(
    [
        "view.poseCaptured=True",
        "view.poseDirH=1.2",
        "view.poseDirV=-0.3",
        "view.poseDistance=3.3",
        "view.poseRestoreStatus=applied-saved-view-pose-after-fixon",
        "view.poseAppliedCount=1",
        "view.poseAppliedDirH=1.2",
        "view.poseAppliedDirV=-0.3",
        "view.poseAppliedDistance=3.3",
        "view.poseAppliedFovY=0.78",
        "view.poseLastRestoreStatus=applied-saved-view-pose-after-fixon",
        "view.poseLastRestoreSceneGeneration=4",
        "view.suppressedByRun=False",
        "unrelated.key=value",
    ]);

    return selected.Count == 13
        && selected.Contains("view.poseCaptured=True")
        && selected.Contains("view.poseRestoreStatus=applied-saved-view-pose-after-fixon")
        && selected.Contains("view.poseLastRestoreStatus=applied-saved-view-pose-after-fixon")
        && selected.Contains("view.poseLastRestoreSceneGeneration=4")
        && selected.Contains("view.poseAppliedCount=1")
        && selected.Contains("view.poseAppliedFovY=0.78")
        && selected.Contains("view.suppressedByRun=False")
        && !selected.Contains("unrelated.key=value");
});

Test(475, "view replay trace samples retain native DirH DirV and Distance values", () =>
{
    var sample = new TitleBackgroundViewReplayTraceSample(
        0,
        true,
        Vector3.Zero,
        Vector3.UnitZ,
        0.78f,
        "success",
        string.Empty,
        AbsoluteFrame: 12,
        DirH: 2.793f,
        DirV: 0.053f,
        Distance: 5.5f);

    return Math.Abs(sample.DirH!.Value - 2.793f) < 0.0001f
        && Math.Abs(sample.DirV!.Value - 0.053f) < 0.0001f
        && Math.Abs(sample.Distance!.Value - 5.5f) < 0.0001f;
});

Test(476, "saved view pose apply after FixOn starts trace and records durable restore status", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    var body = ExtractMethodBody(serviceText, "private bool ApplySavedViewCameraPoseAfterFixOn(TitleBackgroundCharaSelectView savedView)");
    var applyIndex = body.IndexOf("TryApplyLobbyCameraPose(", StringComparison.Ordinal);
    var traceIndex = body.IndexOf("StartViewReplayTraceForSavedPose(savedView);", StringComparison.Ordinal);

    return applyIndex >= 0
        && traceIndex > applyIndex
        && body.Contains("SavedViewPoseLastRestoreStatus = \"applied-saved-view-pose-after-fixon\"", StringComparison.Ordinal)
        && body.Contains("SavedViewPoseLastRestoreSceneGeneration", StringComparison.Ordinal)
        && body.Contains("SavedViewPoseLastRestoreStatus = \"saved-view-pose-after-fixon-failed\"", StringComparison.Ordinal);
});

Test(477, "view replay trace starts at delayed pose write and records the same frame for FixOn and pose", () =>
{
    var root = FindRepositoryRoot();
    var traceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.ViewReplayTrace.cs"));
    var poseBody = ExtractMethodBody(traceText, "private void StartViewReplayTraceForSavedPose(TitleBackgroundCharaSelectView savedView)");

    return poseBody.Contains("\"saved-view-pose-after-fixon\"", StringComparison.Ordinal)
        && poseBody.Contains("_viewReplayTrace.PoseApplyAbsoluteFrame = absoluteFrame;", StringComparison.Ordinal)
        && poseBody.Contains("_viewReplayTrace.FixOnApplyAbsoluteFrame = absoluteFrame;", StringComparison.Ordinal)
        && poseBody.Contains("savedView.DirH", StringComparison.Ordinal)
        && poseBody.Contains("savedView.DirV", StringComparison.Ordinal)
        && poseBody.Contains("savedView.Distance", StringComparison.Ordinal);
});

Test(478, "view replay trace unrelated scene generation preserves prior samples and source metadata", () =>
{
    var state = new TitleBackgroundViewReplayTraceRuntimeState
    {
        TraceSceneGeneration = 4,
        Source = "saved-view-pose",
        RelativeFrameCounter = 10,
        Status = "collecting",
        PoseApplyAbsoluteFrame = 12,
    };
    state.Samples[0] = new TitleBackgroundViewReplayTraceSample(0, true, Vector3.Zero, Vector3.UnitZ, 0.78f, "success", string.Empty);

    state.PrepareForSceneLoad(activeSceneGeneration: 5);

    return !state.IsActive
        && state.Status == "interrupted-scene-change"
        && state.TraceSceneGeneration == 4
        && state.Source == "saved-view-pose"
        && state.PoseApplyAbsoluteFrame == 12
        && state.Samples.Count == 1;
});

Test(479, "view replay report emits pose timing native pose samples and source-run metadata", () =>
{
    var root = FindRepositoryRoot();
    var traceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.ViewReplayTrace.cs"));
    var body = ExtractMethodBody(traceText, "private void AddViewReplayTraceLines(List<string> lines)");

    return body.Contains("view.trace.fromCurrentRun=", StringComparison.Ordinal)
        && body.Contains("view.trace.poseApplyAbsoluteFrame=", StringComparison.Ordinal)
        && body.Contains("view.trace.fixOnApplyAbsoluteFrame=", StringComparison.Ordinal)
        && body.Contains("view.trace.targetDirH=", StringComparison.Ordinal)
        && body.Contains("].dirH=", StringComparison.Ordinal)
        && body.Contains("].dirV=", StringComparison.Ordinal)
        && body.Contains("].distance=", StringComparison.Ordinal);
});

Test(480, "view replay trace resets stale data when a reset adapter reuses the same scene generation", () =>
{
    var state = new TitleBackgroundViewReplayTraceRuntimeState
    {
        TraceSceneGeneration = 1,
        Source = "saved-view-pose",
        RelativeFrameCounter = -1,
        Status = "complete",
        PoseApplyAbsoluteFrame = 12,
        FixOnApplyAbsoluteFrame = 67,
    };
    state.Samples[0] = new TitleBackgroundViewReplayTraceSample(0, true, Vector3.Zero, Vector3.UnitZ, 0.78f, "success", string.Empty);

    // load開始時点では同じgenerationのtraceはまだ作成され得ない。同値なら旧epochとの番号衝突。
    state.PrepareForSceneLoad(activeSceneGeneration: 1);

    return state.TraceSceneGeneration == 0
        && state.Source == "not-run"
        && state.RelativeFrameCounter == -1
        && state.Status == "not-run"
        && state.PoseApplyAbsoluteFrame == null
        && state.FixOnApplyAbsoluteFrame == null
        && state.Samples.Count == 0;
});

Test(481, "FixOn view resolution selects delayed pose, legacy absolute, and passthrough modes explicitly", () =>
{
    var poseView = new TitleBackgroundCharaSelectView(
        true, "custom:n4f4", new Vector3(1f, 14f, 3f), new Vector3(0f, 14.5f, 0f), 0.78f,
        PoseCaptured: true, DirH: 1.2f, DirV: -0.3f, Distance: 3.3f);
    var legacyView = poseView with { PoseCaptured = false };

    var pose = TitleBackgroundFixOnViewOverrideLogic.Resolve(
        true, poseView, "custom:n4f4", Vector3.Zero, Vector3.UnitY, 1f);
    var legacy = TitleBackgroundFixOnViewOverrideLogic.Resolve(
        true, legacyView, "custom:n4f4", Vector3.Zero, Vector3.UnitY, 1f);
    var mismatch = TitleBackgroundFixOnViewOverrideLogic.Resolve(
        true, poseView, "manual:slot1", Vector3.Zero, Vector3.UnitY, 1f);

    return pose.ShouldOverride
        && pose.ApplicationMode == TitleBackgroundFixOnViewOverrideLogic.ApplicationModeDelayedPose
        && legacy.ShouldOverride
        && legacy.ApplicationMode == TitleBackgroundFixOnViewOverrideLogic.ApplicationModeLegacyAbsolute
        && !mismatch.ShouldOverride
        && mismatch.ApplicationMode == TitleBackgroundFixOnViewOverrideLogic.ApplicationModePassthrough;
});

Test(482, "delayed saved pose is applied only after the natural FixOn original call", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var body = ExtractMethodBody(hooksText, "private nint LobbyCameraFixOnDetour(nint self, float* cameraPos, float* focusPos, float fovY)");
    var lastOriginalIndex = body.LastIndexOf("_hookLifecycle.CameraFixOnHook?.Original", StringComparison.Ordinal);
    var delayedApplyIndex = body.IndexOf("ApplySavedViewCameraPoseAfterFixOn(savedViewPoseToApply.Value)", StringComparison.Ordinal);

    return lastOriginalIndex >= 0
        && delayedApplyIndex > lastOriginalIndex
        && body.IndexOf("CapturePostFixOnCameraState();", StringComparison.Ordinal) > delayedApplyIndex;
});

Test(483, "pose view does not build absolute camera focus overrides or allow anchor focus override", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var body = ExtractMethodBody(hooksText, "private nint LobbyCameraFixOnDetour(nint self, float* cameraPos, float* focusPos, float fovY)");
    var delayedStart = body.IndexOf(
        "if (viewResolution.ApplicationMode == TitleBackgroundFixOnViewOverrideLogic.ApplicationModeDelayedPose)",
        StringComparison.Ordinal);
    var legacyStart = body.IndexOf("else", delayedStart, StringComparison.Ordinal);
    var focusOverrideSection = body.IndexOf("// 焦点 override", legacyStart, StringComparison.Ordinal);
    var delayedBlock = delayedStart >= 0 && legacyStart > delayedStart
        ? body[delayedStart..legacyStart]
        : string.Empty;
    var legacyBlock = legacyStart >= 0 && focusOverrideSection > legacyStart
        ? body[legacyStart..focusOverrideSection]
        : string.Empty;

    return delayedBlock.Contains("savedViewPoseToApply = savedView;", StringComparison.Ordinal)
        && !delayedBlock.Contains("cameraOverride =", StringComparison.Ordinal)
        && !delayedBlock.Contains("focusOverride =", StringComparison.Ordinal)
        && legacyBlock.Contains("cameraOverride =", StringComparison.Ordinal)
        && legacyBlock.Contains("focusOverride =", StringComparison.Ordinal)
        && body.Contains("&& !savedViewPoseToApply.HasValue", StringComparison.Ordinal);
});

Test(484, "scene-ready saved pose path only records defer status and performs no saved pose write", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    var body = ExtractMethodBody(serviceText, "private void RestoreCharaSelectRuntimeCameraStateAfterSceneLoad()");

    return body.Contains("RestoreDecisionDeferSavedViewPoseToFixOn", StringComparison.Ordinal)
        && body.Contains("\"deferred-to-fixon-pose\"", StringComparison.Ordinal)
        && !body.Contains("ApplySavedViewCameraPoseAfterFixOn(", StringComparison.Ordinal)
        && !body.Contains("TryApplyLobbyCameraPose(", StringComparison.Ordinal);
});

Test(485, "view replay divergence detects delayed pose DirH reset without absolute camera targets", () =>
{
    var samples = new[]
    {
        new TitleBackgroundViewReplayTraceSample(
            0, true, Vector3.Zero, new Vector3(-302f, 12f, 499f), 0.78f, "success", string.Empty,
            DirH: 2.793f, DirV: 0.053f, Distance: 5.5f),
        new TitleBackgroundViewReplayTraceSample(
            2, true, Vector3.Zero, new Vector3(-302f, 12f, 499f), 0.78f, "success", string.Empty,
            DirH: 0f, DirV: 0f, Distance: 3.3f),
    };

    var result = TitleBackgroundViewReplayTraceLogic.EvaluateFirstDivergence(
        null, null, 0.78f, 2.793f, 0.053f, 5.5f, samples);

    return result.Diverged
        && result.DivergedAtFrame == 2
        && result.Component == TitleBackgroundViewReplayTraceComponent.DirH;
});

Test(486, "automatic run suppression is evaluated before delayed pose reservation and write", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var body = ExtractMethodBody(hooksText, "private nint LobbyCameraFixOnDetour(nint self, float* cameraPos, float* focusPos, float fovY)");
    var suppressionIndex = body.IndexOf("if (IsSavedViewSuppressedByAutomaticRun())", StringComparison.Ordinal);
    var reservationIndex = body.IndexOf("savedViewPoseToApply = savedView;", StringComparison.Ordinal);
    var writeIndex = body.IndexOf("ApplySavedViewCameraPoseAfterFixOn(savedViewPoseToApply.Value)", StringComparison.Ordinal);

    return suppressionIndex >= 0
        && reservationIndex > suppressionIndex
        && writeIndex > reservationIndex;
});

Test(487, "load-scoped FixOn reset clears view generation gate before each new load", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    var body = ExtractMethodBody(serviceText, "private void ResetFixOnExperimentSnapshot()");

    return body.Contains("_cameraObservation.LastViewOverrideAppliedGeneration = 0;", StringComparison.Ordinal);
});

Test(488, "bounded pose maintain state arms counts unique frames and preserves stop diagnostics", () =>
{
    var view = new TitleBackgroundCharaSelectView(
        true, "custom:n4f4", Vector3.Zero, Vector3.UnitY, 0.78f,
        PoseCaptured: true, DirH: 2.087f, DirV: 0.03f, Distance: 5.5f);
    var state = new TitleBackgroundSavedViewPoseMaintainRuntimeState();

    state.Arm(view, sceneGeneration: 4);
    state.MarkApplied(75);
    state.MarkApplied(75);
    state.MarkApplied(76);
    state.Stop("logged-in");

    return !state.Active
        && state.SceneGeneration == 4
        && state.SavedView == view
        && state.AppliedCallCount == 3
        && state.AppliedFrameCount == 2
        && state.LastAppliedFrame == 76
        && state.StopReason == "logged-in";
});

Test(489, "both curve hooks invoke bounded pose maintain after native original and Phase2G", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var setMid = ExtractMethodBody(hooksText, "private void SetCameraCurveMidPointDetour(nint self, float value)");
    var lowHigh = ExtractMethodBody(hooksText, "private void CalculateCameraCurveLowAndHighPointDetour(nint self, float value)");

    bool Ordered(string body, string original, string phase2G)
    {
        var originalIndex = body.IndexOf(original, StringComparison.Ordinal);
        var phase2GIndex = body.IndexOf(phase2G, StringComparison.Ordinal);
        var maintainIndex = body.IndexOf("TryMaintainSavedViewPoseAfterCurveOriginal(", StringComparison.Ordinal);
        return originalIndex >= 0 && phase2GIndex > originalIndex && maintainIndex > phase2GIndex;
    }

    return Ordered(setMid, "SetCameraCurveMidPointHook?.Original", "TryApplyPhase2GSetCameraCurveMidPointOverride")
        && Ordered(lowHigh, "CalculateCameraCurveLowAndHighPointHook?.Original", "TryApplyPhase2GLowHighCurveOverride");
});

Test(490, "bounded pose maintain writes only lobby pose inputs and never SceneCamera position", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    var maintainBody = ExtractMethodBody(hooksText, "private bool TryMaintainSavedViewPoseAfterCurveOriginal(");
    var coreBody = ExtractMethodBody(serviceText, "private bool TryApplyLobbyCameraPose(");
    var frameworkBody = ExtractMethodBody(hooksText, "private void OnFrameworkUpdate(IFramework _)");

    return maintainBody.Contains("TryApplyLobbyCameraPose(", StringComparison.Ordinal)
        && coreBody.Contains("lobbyCamera->DirH = dirH;", StringComparison.Ordinal)
        && coreBody.Contains("lobbyCamera->DirV = dirV;", StringComparison.Ordinal)
        && coreBody.Contains("lobbyCamera->Distance = distance;", StringComparison.Ordinal)
        && coreBody.Contains("lobbyCamera->InterpDistance = distance;", StringComparison.Ordinal)
        && coreBody.Contains("lobbyCamera->FoV = fovY;", StringComparison.Ordinal)
        && !maintainBody.Contains("SceneCamera", StringComparison.Ordinal)
        && !coreBody.Contains("SceneCamera", StringComparison.Ordinal)
        && !frameworkBody.Contains("TryMaintainSavedViewPoseAfterCurveOriginal", StringComparison.Ordinal)
        && !frameworkBody.Contains("TryApplyLobbyCameraPose", StringComparison.Ordinal);
});

Test(491, "bounded pose maintain gate stops on login generation run context and saved-view changes", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var body = ExtractMethodBody(hooksText, "private bool TryResolveSavedViewPoseMaintainTarget(");

    return body.Contains("_clientState.IsLoggedIn", StringComparison.Ordinal)
        && body.Contains("_hookLifecycle.State != TitleBackgroundServiceState.Ready", StringComparison.Ordinal)
        && body.Contains("IsHookProbeMode()", StringComparison.Ordinal)
        && body.Contains("IsSavedViewSuppressedByAutomaticRun()", StringComparison.Ordinal)
        && body.Contains("_charaSelectTitleBackgroundSessionActive", StringComparison.Ordinal)
        && body.Contains("_savedViewPoseMaintain.SceneGeneration", StringComparison.Ordinal)
        && body.Contains("_activeCharaSelectSceneGeneration", StringComparison.Ordinal)
        && body.Contains("GetFixOnFocusOverrideContextReason()", StringComparison.Ordinal)
        && body.Contains("ApplicationModeDelayedPose", StringComparison.Ordinal)
        && body.Contains("currentView != _savedViewPoseMaintain.SavedView", StringComparison.Ordinal);
});

Test(492, "bounded pose maintain is inactive before successful delayed FixOn apply and arms only on success", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    var applyBody = ExtractMethodBody(serviceText, "private bool ApplySavedViewCameraPoseAfterFixOn(TitleBackgroundCharaSelectView savedView)");
    var writeIndex = applyBody.IndexOf("TryApplyLobbyCameraPose(", StringComparison.Ordinal);
    var armIndex = applyBody.IndexOf("_savedViewPoseMaintain.Arm(", StringComparison.Ordinal);
    var state = new TitleBackgroundSavedViewPoseMaintainRuntimeState();

    return !state.Active
        && writeIndex >= 0
        && armIndex > writeIndex
        && applyBody.IndexOf("return false;", StringComparison.Ordinal) < armIndex;
});

Test(493, "automatic diagnostic allowlist includes bounded pose maintain keys", () =>
{
    var selected = TitleBackgroundAutomaticCheckDiagnosticSelector.Select(
    [
        "view.poseMaintain.active=False",
        "view.poseMaintain.sceneGeneration=4",
        "view.poseMaintain.appliedCallCount=240",
        "view.poseMaintain.appliedFrameCount=120",
        "view.poseMaintain.lastFrame=194",
        "view.poseMaintain.stopReason=world-login-transition",
        "unrelated.key=value",
    ]);

    return selected.Count == 6
        && selected.Contains("view.poseMaintain.active=False")
        && selected.Contains("view.poseMaintain.appliedFrameCount=120")
        && selected.Contains("view.poseMaintain.stopReason=world-login-transition")
        && !selected.Contains("unrelated.key=value");
});

Test(494, "session and load cleanup stop bounded pose maintain without erasing counters", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs"));
    var resetBody = ExtractMethodBody(serviceText, "private void ResetFixOnExperimentSnapshot()");
    var endBody = ExtractMethodBody(serviceText, "private void EndCharaSelectTitleBackgroundSession(string reason, string source)");

    return resetBody.Contains("_savedViewPoseMaintain.Stop(\"scene-load-started\");", StringComparison.Ordinal)
        && endBody.Contains("_savedViewPoseMaintain.Stop(reason);", StringComparison.Ordinal);
});

Test(495, "framework update performs only immediate maintain stop validation and no camera write", () =>
{
    var root = FindRepositoryRoot();
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var frameworkBody = ExtractMethodBody(hooksText, "private void OnFrameworkUpdate(IFramework _)");
    var stopBody = ExtractMethodBody(hooksText, "private void StopSavedViewPoseMaintainIfInvalid()");

    return frameworkBody.Contains("StopSavedViewPoseMaintainIfInvalid();", StringComparison.Ordinal)
        && !frameworkBody.Contains("TryApplyLobbyCameraPose", StringComparison.Ordinal)
        && stopBody.Contains("TryResolveSavedViewPoseMaintainTarget", StringComparison.Ordinal)
        && stopBody.Contains("_savedViewPoseMaintain.Stop(stopReason);", StringComparison.Ordinal)
        && !stopBody.Contains("TryApplyLobbyCameraPose", StringComparison.Ordinal);
});

Test(496, "character facing yaw uses saved DirH plus the single calibration offset", () =>
{
    var fromZero = TitleBackgroundCharaSelectCharacterFacing.ComputeYaw(0f);
    var fromSaved = TitleBackgroundCharaSelectCharacterFacing.ComputeYaw(2.087f);
    var expected = TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(
        2.087f + TitleBackgroundCharaSelectCharacterFacing.CalibrationOffset);

    return TitleBackgroundCharaSelectCharacterFacing.DefaultCalibrationOffset == 0f
        && Math.Abs(fromZero) < 0.0001f
        && Math.Abs(fromSaved - expected) < 0.0001f
        && Math.Abs(fromSaved - 2.087f) < 0.0001f
        && TitleBackgroundCharaSelectCharacterFacing.ComputeYaw(float.NaN) == 0f;
});

Test(497, "character rotation writer uses native SetRotation yaw and does not synthesize DrawObject quaternion", () =>
{
    var root = FindRepositoryRoot();
    var probeText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundCharacterSourceProbe.cs"));
    var body = ExtractMethodBody(probeText, "public static bool TrySetCurrentCharacterDrawRotation(");

    return body.Contains("character->SetRotation(yaw);", StringComparison.Ordinal)
        && body.Contains("readBackRotation = character->Rotation;", StringComparison.Ordinal)
        && !body.Contains("drawObject->Rotation", StringComparison.Ordinal)
        && !body.Contains("SceneCamera", StringComparison.Ordinal);
});

Test(498, "character facing runs directly after successful position write in the existing placement slot", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var body = ExtractMethodBody(timelineText, "private void MaintainCharaSelectCharacterPlacement()");
    var positionIndex = body.IndexOf("TrySetCurrentCharacterDrawPosition(placementActor, target)", StringComparison.Ordinal);
    var facingIndex = body.IndexOf("ApplySavedViewCharacterFacing(", StringComparison.Ordinal);

    return positionIndex >= 0
        && facingIndex > positionIndex
        && body.Contains("if (TitleBackgroundCharacterSourceProbe.TrySetCurrentCharacterDrawPosition(placementActor, target))", StringComparison.Ordinal);
});

Test(499, "character facing gate requires pre-login session run allowance maintained pose and matching saved view", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var body = ExtractMethodBody(timelineText, "private void ApplySavedViewCharacterFacing(");

    return body.Contains("_clientState.IsLoggedIn", StringComparison.Ordinal)
        && body.Contains("_charaSelectTitleBackgroundSessionActive", StringComparison.Ordinal)
        && body.Contains("IsSavedViewSuppressedByAutomaticRun()", StringComparison.Ordinal)
        && body.Contains("_savedViewPoseMaintain.Active", StringComparison.Ordinal)
        && body.Contains("TitleBackgroundCharaSelectViewEnabled", StringComparison.Ordinal)
        && body.Contains("activeCandidate.Id", StringComparison.Ordinal)
        && body.Contains("ApplicationModeDelayedPose", StringComparison.Ordinal)
        && body.Contains("savedView != _savedViewPoseMaintain.SavedView", StringComparison.Ordinal);
});

Test(500, "character facing diagnostics are included in the automatic report allowlist", () =>
{
    var selected = TitleBackgroundAutomaticCheckDiagnosticSelector.Select(
    [
        "character.facing.active=False",
        "character.facing.appliedFrameCount=120",
        "character.facing.appliedYaw=-1.055",
        "character.facing.savedDirH=2.087",
        "character.facing.readBackRotation=-1.055",
        "character.facing.lastError=suppressed-by-run",
        "unrelated.key=value",
    ]);

    return selected.Count == 6
        && selected.Contains("character.facing.active=False")
        && selected.Contains("character.facing.appliedYaw=-1.055")
        && selected.Contains("character.facing.readBackRotation=-1.055")
        && !selected.Contains("unrelated.key=value");
});

Test(501, "character facing never writes SceneCamera and only placement owns the rotation writer", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var hooksText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.NativeHooks.cs"));
    var facingBody = ExtractMethodBody(timelineText, "private void ApplySavedViewCharacterFacing(");
    var frameworkBody = ExtractMethodBody(hooksText, "private void OnFrameworkUpdate(IFramework _)");

    return facingBody.Contains("TrySetCurrentCharacterDrawRotation", StringComparison.Ordinal)
        && !facingBody.Contains("SceneCamera", StringComparison.Ordinal)
        && frameworkBody.Contains("MaintainCharaSelectCharacterPlacement();", StringComparison.Ordinal)
        && !frameworkBody.Contains("TrySetCurrentCharacterDrawRotation", StringComparison.Ordinal);
});

Test(502, "facing calibration derives the actor rotation convention in every camera quadrant", () =>
{
    var character = Vector3.Zero;
    var cases = new[]
    {
        new Vector3(1f, 0f, 1f),
        new Vector3(-1f, 0f, 1f),
        new Vector3(-1f, 0f, -1f),
        new Vector3(1f, 0f, -1f),
    };

    return cases.All(camera =>
    {
        var geometry = MathF.Atan2(camera.X, camera.Z);
        var naturalRotation = TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(geometry + 0.75f);
        return TitleBackgroundCharaSelectCharacterFacing.TryDeriveCalibrationOffset(
                naturalRotation,
                character,
                camera,
                out var offset,
                out var expected)
            && TitleBackgroundCharaSelectCharacterFacing.AngularDistance(offset, 0.75f) < 0.0001f
            && TitleBackgroundCharaSelectCharacterFacing.AngularDistance(expected, geometry) < 0.0001f;
    });
});

Test(503, "facing calibration rejects invalid geometry and ComputeYaw keeps the zero fallback", () =>
{
    var rejected = !TitleBackgroundCharaSelectCharacterFacing.TryDeriveCalibrationOffset(
        0f,
        Vector3.Zero,
        Vector3.Zero,
        out _,
        out _);
    var fallback = TitleBackgroundCharaSelectCharacterFacing.ComputeYaw(0f);
    return rejected
        && Math.Abs(fallback) < 0.0001f
        && float.IsNaN(TitleBackgroundCharaSelectCharacterFacing.AngularDistance(float.NaN, 0f));
});

Test(504, "facing calibration stable window rejects transient samples and computes spread from stable samples only", () =>
{
    var firstRejected = !TitleBackgroundCharaSelectCharacterFacing.IsStableCalibrationSample(
        null,
        null,
        2.5f,
        0f);
    var transitionRejected = !TitleBackgroundCharaSelectCharacterFacing.IsStableCalibrationSample(
        2.5f,
        0f,
        0.01f,
        0f);
    var stableAccepted = TitleBackgroundCharaSelectCharacterFacing.IsStableCalibrationSample(
        0.01f,
        0f,
        0.015f,
        0.004f);

    var spread = TitleBackgroundCharaSelectCharacterFacing.AccumulateOffsetSpread(
        0.01f,
        0f,
        0f,
        0.015f,
        out var minRelative,
        out var maxRelative);

    return firstRejected
        && transitionRejected
        && stableAccepted
        && Math.Abs(spread - 0.005f) < 0.0001f
        && Math.Abs(minRelative) < 0.0001f
        && Math.Abs(maxRelative - 0.005f) < 0.0001f;
});

Test(505, "facing calibration stable window survives an initial outlier before persistence spread", () =>
{
    float? previousNatural = null;
    float? previousExpected = null;
    float? anchor = null;
    float? minRelative = null;
    float? maxRelative = null;
    float? spread = null;
    var accepted = 0;
    var rejected = 0;

    foreach (var sample in new[]
    {
        (Natural: 2.5f, Expected: 0f, Offset: 2.5f),
        (Natural: 0.01f, Expected: 0f, Offset: 0.01f),
        (Natural: 0.012f, Expected: 0.001f, Offset: 0.011f),
        (Natural: 0.014f, Expected: 0.002f, Offset: 0.012f),
        (Natural: 0.016f, Expected: 0.003f, Offset: 0.013f),
        (Natural: 0.018f, Expected: 0.004f, Offset: 0.014f),
        (Natural: 0.02f, Expected: 0.005f, Offset: 0.015f),
    })
    {
        var stable = TitleBackgroundCharaSelectCharacterFacing.IsStableCalibrationSample(
            previousNatural,
            previousExpected,
            sample.Natural,
            sample.Expected);
        previousNatural = sample.Natural;
        previousExpected = sample.Expected;
        if (!stable)
        {
            rejected++;
            continue;
        }

        if (!anchor.HasValue)
        {
            anchor = sample.Offset;
            minRelative = 0f;
            maxRelative = 0f;
            spread = 0f;
        }
        else
        {
            spread = TitleBackgroundCharaSelectCharacterFacing.AccumulateOffsetSpread(
                anchor.Value,
                minRelative,
                maxRelative,
                sample.Offset,
                out var nextMin,
                out var nextMax);
            minRelative = nextMin;
            maxRelative = nextMax;
        }

        accepted++;
    }

    return rejected == 2
        && accepted == 5
        && spread <= TitleBackgroundCharaSelectCharacterFacing.CalibrationPersistenceMaxSpread
        && TitleBackgroundCharaSelectCharacterFacing.AngularDistance(anchor!.Value, 0.011f) < 0.0001f;
});

Test(506, "pose hold and camera jitter verification honor numeric thresholds", () =>
{
    var stable = new[]
    {
        new TitleBackgroundViewReplayTraceSample(0, true, null, null, null, "success", "", DirH: 1f, DirV: 0.2f, Distance: 5f),
        new TitleBackgroundViewReplayTraceSample(1, true, null, null, null, "success", "", DirH: 1.005f, DirV: 0.2f, Distance: 5f),
    };
    var unstable = new[]
    {
        stable[0],
        stable[1] with { DirH = 1.02f },
    };

    return TitleBackgroundVerificationLogic.EvaluatePoseHold(1f, 0.2f, 5f, stable).Status == TitleBackgroundVerificationStatus.PASS
        && TitleBackgroundVerificationLogic.EvaluateCameraJitter(stable).Status == TitleBackgroundVerificationStatus.PASS
        && TitleBackgroundVerificationLogic.EvaluatePoseHold(1f, 0.2f, 5f, unstable).Status == TitleBackgroundVerificationStatus.FAIL
        && TitleBackgroundVerificationLogic.EvaluateCameraJitter(unstable).Status == TitleBackgroundVerificationStatus.FAIL
        && TitleBackgroundVerificationLogic.EvaluatePoseHold(null, null, null, stable).Status == TitleBackgroundVerificationStatus.NotEvaluated;
});

Test(507, "facing framing suppression login and environment verification are fail closed only with evidence", () =>
{
    return TitleBackgroundVerificationLogic.EvaluateFacing(0.05f).Status == TitleBackgroundVerificationStatus.PASS
        && TitleBackgroundVerificationLogic.EvaluateFacing(0.11f).Status == TitleBackgroundVerificationStatus.FAIL
        && TitleBackgroundVerificationLogic.EvaluateFacing(null, 0.5f, 10).Status == TitleBackgroundVerificationStatus.NotEvaluated
        && TitleBackgroundVerificationLogic.EvaluateFraming(Vector3.Zero, new Vector3(0f, 0f, 1.9f)).Status == TitleBackgroundVerificationStatus.PASS
        && TitleBackgroundVerificationLogic.EvaluateFraming(Vector3.Zero, new Vector3(0f, 0f, 2.1f)).Status == TitleBackgroundVerificationStatus.FAIL
        && TitleBackgroundVerificationLogic.EvaluateSuppression(true, true, false, false).Status == TitleBackgroundVerificationStatus.PASS
        && TitleBackgroundVerificationLogic.EvaluateSuppression(true, true, true, false).Status == TitleBackgroundVerificationStatus.FAIL
        && TitleBackgroundVerificationLogic.EvaluateLoginStop(true, false, false, "world-login-transition", true, false).Status == TitleBackgroundVerificationStatus.PASS
        && TitleBackgroundVerificationLogic.EvaluateLoginStop(true, false, false, "not-started", true, true).Status == TitleBackgroundVerificationStatus.PASS
        && TitleBackgroundVerificationLogic.EvaluateLoginStop(true, false, false, "not-started", false, false).Status == TitleBackgroundVerificationStatus.NotEvaluated
        && TitleBackgroundVerificationLogic.EvaluateLoginStop(true, true, false, "world-login-transition", true, false).Status == TitleBackgroundVerificationStatus.FAIL
        && TitleBackgroundVerificationLogic.EvaluateEnvironment(true, 1, "applied", true, 1, "applied").Status == TitleBackgroundVerificationStatus.PASS
        && TitleBackgroundVerificationLogic.EvaluateEnvironment(true, 0, "not-applied", true, 1, "applied").Status == TitleBackgroundVerificationStatus.FAIL;
});

Test(508, "facing settled max starts after 120 consecutive applied frames and resets on non-applied frames", () =>
{
    var consecutive = 0;
    float? max = null;
    for (var frame = 1; frame < TitleBackgroundCharaSelectCharacterFacing.FacingSettledFrameThreshold; frame++)
    {
        consecutive = TitleBackgroundCharaSelectCharacterFacing.AdvanceFacingSettledFrameCount(true, consecutive);
        max = TitleBackgroundCharaSelectCharacterFacing.AccumulateSettledMaxAngularError(consecutive, max, frame == 1 ? 1f : 0.01f);
    }

    var beforeThresholdIgnored = !max.HasValue;
    consecutive = TitleBackgroundCharaSelectCharacterFacing.AdvanceFacingSettledFrameCount(true, consecutive);
    max = TitleBackgroundCharaSelectCharacterFacing.AccumulateSettledMaxAngularError(consecutive, max, 0.2f);
    var afterThresholdIncluded = Math.Abs(max!.Value - 0.2f) < 0.0001f;
    consecutive = TitleBackgroundCharaSelectCharacterFacing.AdvanceFacingSettledFrameCount(false, consecutive);

    return beforeThresholdIgnored
        && afterThresholdIncluded
        && consecutive == 0;
});

Test(509, "facing calibration config round-trips through both serializers", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundCharaSelectFacingCalibrationCaptured = true,
        TitleBackgroundCharaSelectFacingCalibrationCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectFacingCalibrationOffset = 0.75f,
    };
    var system = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(configuration));
    var newtonsoft = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(
        Newtonsoft.Json.JsonConvert.SerializeObject(configuration));

    return !new Configuration().TitleBackgroundCharaSelectFacingCalibrationCaptured
        && system?.TitleBackgroundCharaSelectFacingCalibrationCaptured == true
        && system.TitleBackgroundCharaSelectFacingCalibrationCandidateId == "custom:n4f4"
        && Math.Abs(system.TitleBackgroundCharaSelectFacingCalibrationOffset - 0.75f) < 0.0001f
        && newtonsoft?.TitleBackgroundCharaSelectFacingCalibrationCaptured == true
        && Math.Abs(newtonsoft.TitleBackgroundCharaSelectFacingCalibrationOffset - 0.75f) < 0.0001f;
});

Test(510, "facing calibration recovery and simple reset are wired", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundCharaSelectFacingCalibrationCaptured = true,
        TitleBackgroundCharaSelectFacingCalibrationCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectFacingCalibrationOffset = 0.75f,
    };
    var restored = new Configuration();
    TitleBackgroundAutomaticCheckSettingsSnapshot.Capture(configuration).ApplyTo(restored);
    var root = FindRepositoryRoot();
    var quickCheckText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.QuickCheck.cs"));
    var reset = ExtractMethodBody(quickCheckText, "internal bool ResetSimpleTitleBackgroundSettings()");

    return restored.TitleBackgroundCharaSelectFacingCalibrationCaptured
        && restored.TitleBackgroundCharaSelectFacingCalibrationCandidateId == "custom:n4f4"
        && Math.Abs(restored.TitleBackgroundCharaSelectFacingCalibrationOffset - 0.75f) < 0.0001f
        && reset.Contains("TitleBackgroundCharaSelectFacingCalibrationCaptured = false", StringComparison.Ordinal)
        && reset.Contains("TitleBackgroundCharaSelectFacingCalibrationCandidateId = string.Empty", StringComparison.Ordinal);
});

Test(511, "captured false ignores stale saved facing offset and uses the zero default", () =>
{
    var source = new Configuration
    {
        TitleBackgroundCharaSelectFacingCalibrationCaptured = false,
        TitleBackgroundCharaSelectFacingCalibrationCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectFacingCalibrationOffset = MathF.PI,
    };
    var target = new Configuration();
    target.ApplyFrom(source);

    var calibrationOffset = target.TitleBackgroundCharaSelectFacingCalibrationCaptured
        ? target.TitleBackgroundCharaSelectFacingCalibrationOffset
        : TitleBackgroundCharaSelectCharacterFacing.DefaultCalibrationOffset;
    return !target.TitleBackgroundCharaSelectFacingCalibrationCaptured
        && Math.Abs(target.TitleBackgroundCharaSelectFacingCalibrationOffset - MathF.PI) < 0.0001f
        && Math.Abs(TitleBackgroundCharaSelectCharacterFacing.ComputeYaw(2.087f, calibrationOffset) - 2.087f) < 0.0001f;
});

Test(512, "verification and calibration diagnostics are included in automatic reports", () =>
{
    var selected = TitleBackgroundAutomaticCheckDiagnosticSelector.Select(
    [
        "character.facing.calibration.derivedOffset=0.75",
        "character.facing.calibration.naturalRotation=1.25",
        "character.facing.calibration.stableSampleCount=5",
        "character.facing.calibration.rejectedTransientCount=2",
        "character.facing.offsetAbsError=0",
        "character.facing.settledMaxAngularError=0.01",
        "verify.poseHold=PASS",
        "verify.cameraJitter=PASS",
        "verify.rotationJitter=PASS",
        "verify.facing=PASS",
        "verify.facing.detail=settled-angular-error;see=character.facing.offsetAbsError",
        "verify.framing=PASS",
        "verify.suppression=PASS",
        "verify.loginStop=PASS",
        "verify.environment=PASS",
        "unrelated=value",
    ]);

    return selected.Count == 15
        && selected.Contains("verify.poseHold=PASS")
        && selected.Contains("verify.facing=PASS")
        && selected.Contains("verify.facing.detail=settled-angular-error;see=character.facing.offsetAbsError")
        && selected.Contains("character.facing.calibration.derivedOffset=0.75")
        && selected.Contains("character.facing.calibration.stableSampleCount=5")
        && selected.Contains("character.facing.calibration.rejectedTransientCount=2")
        && selected.Contains("character.facing.offsetAbsError=0")
        && !selected.Contains("unrelated=value");
});

Test(513, "QuickCheck uses numeric verification failures and accepts passing framing", () =>
{
    var failed = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        verifyFacing: "FAIL",
        verifyFraming: "FAIL"));
    var passed = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterExpectedVisible: true,
        characterCompositedApplied: true,
        characterGroundPlacementVerified: true,
        cameraFramesCharacter: "True",
        verifyFacing: "PASS",
        verifyFraming: "PASS",
        verifySuppression: "PASS",
        verifyLoginStop: "PASS",
        verifyEnvironment: "PASS"));

    return failed.Level == TitleBackgroundQuickCheckLevel.WARN
        && failed.Reason == "numeric verification failed"
        && failed.Warnings.Any(warning => warning.Contains("calibrated camera direction", StringComparison.Ordinal))
        && passed.Level == TitleBackgroundQuickCheckLevel.OK
        && !passed.Warnings.Any(warning => warning.Contains("camera does not frame", StringComparison.Ordinal));
});

Test(514, "delivery promotes numerically verified character composition without visual confirmation", () =>
{
    var delivery = DeliveryFromRaw(
        "stub-only",
        "all-zero-transform",
        "not-observed",
        8,
        0,
        0,
        0,
        [],
        lastOverrideApplied: true,
        currentObjectTableValidForCharaSelect: false,
        characterCompositedApplied: true,
        characterNumericallyVerified: true);

    return delivery.DeliveryVerdict == "working-character-composition"
        && delivery.CharacterVisibilityObserved == "numerically-verified"
        && delivery.CharacterVisibilityBlocker == "none"
        && delivery.MvpStatus == "complete-character-composition"
        && delivery.MvpBlockingIssue == "none"
        && delivery.UserNextAction == "No visual confirmation is required.";
});

Test(515, "suppressed automatic run preserves same-generation saved-view trace while normal load resets it", () =>
{
    TitleBackgroundViewReplayTraceRuntimeState CreateState()
    {
        var state = new TitleBackgroundViewReplayTraceRuntimeState
        {
            TraceSceneGeneration = 1,
            Source = "saved-view-pose-after-fixon",
            RelativeFrameCounter = -1,
            Status = "complete",
            TargetDirH = 1.2f,
        };
        state.Samples[0] = new TitleBackgroundViewReplayTraceSample(
            0,
            true,
            null,
            null,
            null,
            "success",
            "",
            DirH: 1.2f);
        return state;
    }

    var normal = CreateState();
    normal.PrepareForSceneLoad(1);
    var automatic = CreateState();
    automatic.PrepareForSceneLoad(1, preserveSameGenerationTraceForSuppressedRun: true);

    return normal.TraceSceneGeneration == 0
        && normal.Samples.Count == 0
        && automatic.TraceSceneGeneration == 1
        && automatic.Samples.Count == 1
        && automatic.Status == "complete";
});

Test(516, "numeric composition never overrides an unsafe login transition", () =>
{
    var delivery = DeliveryFromRaw(
        "stub-only",
        "all-zero-transform",
        "not-observed",
        8,
        0,
        0,
        0,
        [],
        lastOverrideApplied: true,
        currentObjectTableValidForCharaSelect: false,
        characterCompositedApplied: true,
        characterNumericallyVerified: true,
        transitionSafety: "unsafe");

    return delivery.DeliveryVerdict == "unsafe"
        && delivery.MvpStatus != "complete-character-composition"
        && delivery.MvpBlockingIssue != "none";
});

Test(517, "facing calibration freezes after the first complete stable window", () =>
{
    return !TitleBackgroundCharaSelectCharacterFacing.HasStableCalibrationWindow(4, 0.01f)
        && TitleBackgroundCharaSelectCharacterFacing.HasStableCalibrationWindow(5, 0.01f)
        && !TitleBackgroundCharaSelectCharacterFacing.HasStableCalibrationWindow(5, 0.051f)
        && !TitleBackgroundCharaSelectCharacterFacing.HasStableCalibrationWindow(5000, null);
});

Test(518, "automatic facing calibration capture stops reading after a stable window", () =>
{
    var root = FindRepositoryRoot();
    var timelineText = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var body = ExtractMethodBody(timelineText, "private void CaptureFacingCalibrationDuringAutomaticRun(");
    var stableWindowIndex = body.IndexOf("HasStableCalibrationWindow(", StringComparison.Ordinal);
    var readIndex = body.IndexOf("TryReadCharaSelectCharacterAim(", StringComparison.Ordinal);

    return stableWindowIndex >= 0
        && readIndex > stableWindowIndex;
});

// ---- Title Background V2 (Il Mheg proof) ----

Test(520, "v2 active disables legacy camera-maintenance", () =>
{
    return !TitleBackgroundV2Logic.IsLegacyCameraMaintenanceAllowed(true)
        && TitleBackgroundV2Logic.IsLegacyCameraMaintenanceAllowed(false);
});

Test(521, "v2 framing gate stops only on logged-in and inactive session (not on automatic-run suppression)", () =>
{
    // legacy saved-view/facing の run 抑止は V2 framing gate に渡さない。gate は run 抑止フラグを一切
    // 受け取らないため、one-click 実機確認 run 中でも pre-login CharaSelect の good path は Apply になる。
    TitleBackgroundV2FramingGate Gate(bool loggedIn, bool sessionActive) =>
        TitleBackgroundV2Logic.ShouldApplyFraming(
            v2Active: true,
            serviceReady: true,
            hookProbeMode: false,
            loggedIn: loggedIn,
            charaSelectSessionActive: sessionActive,
            activeSceneGeneration: 3,
            runtimeSceneGeneration: 3,
            boundedWindowOpen: true,
            currentMap: GameLobbyType.CharaSelect);

    return Gate(true, true).Decision == TitleBackgroundV2FramingDecision.Stop
        && Gate(false, false).Decision == TitleBackgroundV2FramingDecision.Stop
        && Gate(false, true).Decision == TitleBackgroundV2FramingDecision.Apply;
});

Test(522, "v2 framing gate applies only on pre-login chara-select with matching generation and open window", () =>
{
    var apply = TitleBackgroundV2Logic.ShouldApplyFraming(
        true, true, false, false, true, 5, 5, true, GameLobbyType.CharaSelect);
    var mismatch = TitleBackgroundV2Logic.ShouldApplyFraming(
        true, true, false, false, true, 5, 4, true, GameLobbyType.CharaSelect);
    var notChara = TitleBackgroundV2Logic.ShouldApplyFraming(
        true, true, false, false, true, 5, 5, true, GameLobbyType.Title);
    var closed = TitleBackgroundV2Logic.ShouldApplyFraming(
        true, true, false, false, true, 5, 5, false, GameLobbyType.CharaSelect);
    var v2Inactive = TitleBackgroundV2Logic.ShouldApplyFraming(
        false, true, false, false, true, 5, 5, true, GameLobbyType.CharaSelect);

    return apply.Decision == TitleBackgroundV2FramingDecision.Apply
        && mismatch.Decision == TitleBackgroundV2FramingDecision.Skip
        && notChara.Decision == TitleBackgroundV2FramingDecision.Skip
        && closed.Decision == TitleBackgroundV2FramingDecision.Skip
        && v2Inactive.Decision == TitleBackgroundV2FramingDecision.Skip;
});

Test(523, "v2 bounded framing window: retry budget exhausts to a hard stop when writes keep failing", () =>
{
    var state = new TitleBackgroundV2RuntimeState();
    state.ArmForSceneGeneration(1);
    var attempts = 0;
    while (state.ShouldAttemptFraming(1, 1) && attempts < 50)
    {
        state.RecordFramingAttempt(success: false, frame: attempts, status: "failed:test");
        attempts++;
    }

    return state.WindowClosed
        && !state.FramingApplied
        && state.LastStopReason == "retry-exhausted"
        && attempts == TitleBackgroundV2FramingWindow.RetryBudget
        && !state.ShouldAttemptFraming(1, 1);
});

Test(524, "v2 bounded framing window: settle budget exhausts to a hard stop after successful writes", () =>
{
    var state = new TitleBackgroundV2RuntimeState();
    state.ArmForSceneGeneration(2);
    var writes = 0;
    while (state.ShouldAttemptFraming(2, 2) && writes < 50)
    {
        state.RecordFramingAttempt(success: true, frame: writes, status: "applied");
        writes++;
    }

    return state.WindowClosed
        && state.FramingApplied
        && state.LastStopReason == "settle-window-complete"
        && writes == TitleBackgroundV2FramingWindow.SettleBudget
        && !state.ShouldAttemptFraming(2, 2)
        // 新しい scene generation で再 arm される（次のロード = 次の bounded ウィンドウ）。
        && Rearms(state);

    static bool Rearms(TitleBackgroundV2RuntimeState s)
    {
        s.ArmForSceneGeneration(3);
        return !s.WindowClosed && s.ShouldAttemptFraming(3, 3);
    }
});

Test(525, "v2 framing pose for n4f4 is finite and not top-down", () =>
{
    var pose = TitleBackgroundV2Logic.ResolveFramingPose(
        "custom:n4f4",
        TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        TitleBackgroundPreset.DefaultFovY);

    return float.IsFinite(pose.Yaw) && float.IsFinite(pose.Pitch)
        && float.IsFinite(pose.Distance) && float.IsFinite(pose.FovY)
        && Math.Abs(pose.Yaw) < 0.0001f
        && pose.Pitch > 0f && pose.Pitch < 0.6f
        && pose.Distance is > 1.5f and < 5.0f
        && pose.FovY > 0f;
});

Test(526, "v2 reset clears all run-scoped framing state", () =>
{
    var state = new TitleBackgroundV2RuntimeState();
    state.ArmForSceneGeneration(9);
    state.NotifySceneReadyObserved();
    state.RecordFramingAttempt(success: true, frame: 1, status: "applied");
    state.RecordAppliedPose(0f, 0.15f, 2.6f, 1f);
    state.MarkPostLoginWritesStopped();
    state.Reset();

    return state.SceneGeneration == 0
        && !state.FramingApplied
        && state.FramingAttemptCount == 0
        && state.SceneReadyObservedCount == 0
        && !state.PostLoginWritesStopped
        && !state.WindowClosed
        && state.LastStopReason == "not-run";
});

Test(527, "configuration TitleBackgroundV2Enabled defaults off and round-trips through both serializers", () =>
{
    var defaultConfiguration = new Configuration();
    var configuration = new Configuration { TitleBackgroundV2Enabled = true };
    var applied = new Configuration();
    applied.ApplyFrom(configuration);

    var systemJson = JsonSerializer.Serialize(configuration);
    var systemRestored = JsonSerializer.Deserialize<Configuration>(systemJson);
    var newtonsoftJson = Newtonsoft.Json.JsonConvert.SerializeObject(configuration);
    var newtonsoftRestored = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(newtonsoftJson);

    return !defaultConfiguration.TitleBackgroundV2Enabled
        && applied.TitleBackgroundV2Enabled
        && systemJson.Contains("\"TitleBackgroundV2Enabled\"", StringComparison.Ordinal)
        && systemRestored != null && systemRestored.TitleBackgroundV2Enabled
        && newtonsoftJson.Contains("\"TitleBackgroundV2Enabled\"", StringComparison.Ordinal)
        && newtonsoftRestored != null && newtonsoftRestored.TitleBackgroundV2Enabled;
});

Test(528, "automatic-check recovery snapshot round-trips V2Enabled and old journals default it off", () =>
{
    var source = new Configuration
    {
        TitleBackgroundV2Enabled = true,
        TitleBackgroundCharaSelectPlacementEnabled = true,
        TitleBackgroundCharaSelectPlacementCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectPlacementPositionCaptured = true,
        TitleBackgroundCharaSelectPlacementPositionX = 1.25f,
        TitleBackgroundCharaSelectPlacementPositionY = -2.5f,
        TitleBackgroundCharaSelectPlacementPositionZ = 3.75f,
        TitleBackgroundCharaSelectPlacementRotation = 0.625f,
    };
    var snapshot = TitleBackgroundAutomaticCheckSettingsSnapshot.Capture(source);
    var json = JsonSerializer.Serialize(snapshot);
    var restored = JsonSerializer.Deserialize<TitleBackgroundAutomaticCheckSettingsSnapshot>(json);

    var target = new Configuration { TitleBackgroundV2Enabled = true };
    var legacyJournal = JsonSerializer.Deserialize<TitleBackgroundAutomaticCheckSettingsSnapshot>("{}");
    legacyJournal!.ApplyTo(target);

    return restored != null && restored.V2Enabled
        && restored.CharaSelectPlacementEnabled
        && restored.CharaSelectPlacementCandidateId == "custom:n4f4"
        && restored.CharaSelectPlacementPositionCaptured
        && restored.CharaSelectPlacementPositionX == 1.25f
        && restored.CharaSelectPlacementPositionY == -2.5f
        && restored.CharaSelectPlacementPositionZ == 3.75f
        && restored.CharaSelectPlacementRotation == 0.625f
        && !legacyJournal.V2Enabled
        && !target.TitleBackgroundV2Enabled;
});

Test(529, "new CharaSelect engine disarms legacy camera-maintenance arm sites in service source", () =>
{
    var root = FindRepositoryRoot();
    string Read(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", file));

    var service = Read("TitleScreenBackgroundService.cs");
    var hooks = Read("TitleScreenBackgroundService.NativeHooks.cs");
    var timeline = Read("TitleScreenBackgroundService.TimelineDiagnostics.cs");

    var adapterBody = ExtractMethodBody(service, "private void ConfigureCharaSelectCameraAdapter(");
    var savedViewBody = ExtractMethodBody(service, "private bool ApplySavedViewCameraPoseAfterFixOn(");
    var curveMaintainBody = ExtractMethodBody(hooks, "private bool TryMaintainSavedViewPoseAfterCurveOriginal(");
    var phase2GBody = ExtractMethodBody(hooks, "private bool TryGetPhase2GGenerationOverrideCurve(");
    var placementBody = ExtractMethodBody(timeline, "private void MaintainCharaSelectCharacterPlacement(");

    // 排他の single source of truth は IsNewCharaSelectEngineActive
    // (= IsV2Active || persistent/proof placement owner)。
    return adapterBody.Contains("!IsNewCharaSelectEngineActive", StringComparison.Ordinal)
        && savedViewBody.Contains("IsLegacyCameraMaintenanceAllowed(IsNewCharaSelectEngineActive)", StringComparison.Ordinal)
        && curveMaintainBody.Contains("IsLegacyCameraMaintenanceAllowed(IsNewCharaSelectEngineActive)", StringComparison.Ordinal)
        && phase2GBody.Contains("IsLegacyCameraMaintenanceAllowed(IsNewCharaSelectEngineActive)", StringComparison.Ordinal)
        // 新 CharaSelect エンジン active では legacy の毎フレーム character placement も止める。
        && placementBody.Contains("if (IsNewCharaSelectEngineActive)", StringComparison.Ordinal);
});

Test(530, "v2 bounded framing window: repeated failures after a first success still hard-close after the settle budget", () =>
{
    var state = new TitleBackgroundV2RuntimeState();
    state.ArmForSceneGeneration(7);
    var attempts = 0;

    // 最初の 1 回だけ成功、以後は失敗が続く。
    while (state.ShouldAttemptFraming(7, 7) && attempts < 50)
    {
        state.RecordFramingAttempt(success: attempts == 0, frame: attempts, status: attempts == 0 ? "applied" : "failed:test");
        attempts++;
    }

    return state.WindowClosed
        && state.FramingApplied
        && state.LastStopReason == "settle-window-complete"
        && attempts == TitleBackgroundV2FramingWindow.SettleBudget
        && !state.ShouldAttemptFraming(7, 7);
});

Test(531, "v2 active skips per-frame character placement, non-V2 keeps the existing gated path", () =>
{
    var root = FindRepositoryRoot();
    var timeline = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground",
        "TitleScreenBackgroundService.TimelineDiagnostics.cs"));
    var placementBody = ExtractMethodBody(timeline, "private void MaintainCharaSelectCharacterPlacement(");

    var v2GuardIndex = placementBody.IndexOf("if (IsNewCharaSelectEngineActive)", StringComparison.Ordinal);
    var gateIndex = placementBody.IndexOf("IsCharaSelectCharacterCompositionActive()", StringComparison.Ordinal);

    // 新 CharaSelect エンジン（V2 または placement path）active では即 return（毎フレーム DrawObject
    // 書込を止める）。その guard は既存 gate より前。それ以外は従来どおり
    // IsCharaSelectCharacterCompositionActive() gate で legacy placement を行う。
    return v2GuardIndex >= 0
        && gateIndex > v2GuardIndex
        && placementBody.Contains("return;", StringComparison.Ordinal);
});

Test(532, "TitleEdit-informed placement path active denies legacy ownership and legacy per-frame placement", () =>
{
    // 排他: placement path active でも V2 active でも legacy ownership は不許可。両方 false なら許可。
    return !TitleBackgroundCharaSelectPlacementLogic.IsLegacyOwnershipAllowed(true)
        && TitleBackgroundCharaSelectPlacementLogic.IsLegacyOwnershipAllowed(false)
        && TitleBackgroundCharaSelectPlacementLogic.IsPlacementPathActive(overrideEnabled: true, placementEnabled: true)
        && !TitleBackgroundCharaSelectPlacementLogic.IsPlacementPathActive(overrideEnabled: false, placementEnabled: true)
        && !TitleBackgroundCharaSelectPlacementLogic.IsPlacementPathActive(overrideEnabled: true, placementEnabled: false);
});

Test(533, "placement gate stops on logged-in and inactive session, skips before prerequisites, applies when all hold", () =>
{
    TitleBackgroundCharaSelectEngineGate Gate(
        bool loggedIn = false,
        bool sessionActive = true,
        bool serviceReady = true,
        bool hookProbe = false,
        int activeGen = 4,
        int runtimeGen = 4,
        bool charaSelectMap = true,
        bool candidateMatches = true,
        bool hasPosition = true,
        bool characterResolved = true) =>
        TitleBackgroundCharaSelectPlacementLogic.ResolveGate(
            placementPathActive: true,
            serviceReady: serviceReady,
            hookProbeMode: hookProbe,
            loggedIn: loggedIn,
            charaSelectSessionActive: sessionActive,
            activeSceneGeneration: activeGen,
            runtimeSceneGeneration: runtimeGen,
            isCharaSelectMap: charaSelectMap,
            candidateMatches: candidateMatches,
            hasSourceBackedPosition: hasPosition,
            characterResolved: characterResolved);

    return Gate(loggedIn: true).Decision == TitleBackgroundCharaSelectEngineDecision.Stop
        && Gate(sessionActive: false).Decision == TitleBackgroundCharaSelectEngineDecision.Stop
        && Gate(serviceReady: false).Decision == TitleBackgroundCharaSelectEngineDecision.Skip
        && Gate(hookProbe: true).Decision == TitleBackgroundCharaSelectEngineDecision.Skip
        && Gate(runtimeGen: 5).Decision == TitleBackgroundCharaSelectEngineDecision.Skip
        && Gate(activeGen: 0, runtimeGen: 0).Decision == TitleBackgroundCharaSelectEngineDecision.Skip
        && Gate(charaSelectMap: false).Decision == TitleBackgroundCharaSelectEngineDecision.Skip
        && Gate(candidateMatches: false).Decision == TitleBackgroundCharaSelectEngineDecision.Skip
        && Gate(hasPosition: false).Decision == TitleBackgroundCharaSelectEngineDecision.Skip
        && Gate(characterResolved: false).Decision == TitleBackgroundCharaSelectEngineDecision.Skip
        && Gate().Decision == TitleBackgroundCharaSelectEngineDecision.Apply
        && Gate().Reason == "ready";
});

Test(534, "placement path inactive short-circuits the gate to skip", () =>
{
    var gate = TitleBackgroundCharaSelectPlacementLogic.ResolveGate(
        placementPathActive: false,
        serviceReady: true,
        hookProbeMode: false,
        loggedIn: true,
        charaSelectSessionActive: false,
        activeSceneGeneration: 1,
        runtimeSceneGeneration: 1,
        isCharaSelectMap: true,
        candidateMatches: true,
        hasSourceBackedPosition: true,
        characterResolved: true);
    return gate.Decision == TitleBackgroundCharaSelectEngineDecision.Skip
        && gate.Reason == "placement-path-inactive";
});

Test(535, "placement write trigger is bounded to new scene generation or changed character pointer", () =>
{
    // 同一 generation + 同一 pointer は書かない（bounded no-op）。generation 変化 or pointer 変化で書く。
    return !TitleBackgroundPlacementTestShims.ShouldWrite(3, 3, 0x100, 0x100)
        && TitleBackgroundPlacementTestShims.ShouldWrite(4, 3, 0x100, 0x100)
        && TitleBackgroundPlacementTestShims.ShouldWrite(3, 3, 0x200, 0x100)
        && !TitleBackgroundPlacementTestShims.ShouldWrite(4, 3, 0x0, 0x100);
});

Test(536, "placement location model requires a captured source-backed position and finite coordinates", () =>
{
    var uncaptured = TitleBackgroundCharaSelectPlacementLogic.BuildLocationModel(
        "ffxiv/fld_ffxiv/n4f4", 816, 0, positionCaptured: false, 1f, 2f, 3f, 0.5f);
    var captured = TitleBackgroundCharaSelectPlacementLogic.BuildLocationModel(
        "ffxiv/fld_ffxiv/n4f4", 816, 0, positionCaptured: true, 1f, 2f, 3f, 0.5f);
    var noPath = TitleBackgroundCharaSelectPlacementLogic.BuildLocationModel(
        "", 0, 0, positionCaptured: true, 1f, 2f, 3f, 0.5f);

    return !uncaptured.HasSourceBackedPosition
        && captured.HasSourceBackedPosition
        && captured.Position.X == 1f && captured.Position.Z == 3f && captured.Rotation == 0.5f
        && !noPath.HasSourceBackedPosition;
});

Test(537, "placement runtime state records applies, records login stop, and resets fully", () =>
{
    var state = new TitleBackgroundCharaSelectPlacementRuntimeState();
    state.RecordPlacementApplied(2, 0x100, new Vector3(1f, 2f, 3f), 0.25f, 12);
    var afterFirst = state.PlacementApplyCount == 1
        && state.LastAppliedSceneGeneration == 2
        && state.LastAppliedCharacterPtr() == 0x100
        && state.LastAppliedFrame == 12
        && state.LastReason == "applied";

    state.MarkLoginStopped();
    var afterLogin = state.LoginStopped && state.LastReason == "logged-in";

    state.Reset();
    var afterReset = state.PlacementApplyCount == 0
        && state.LastAppliedSceneGeneration == 0
        && state.LastAppliedCharacterPtr() == 0UL
        && !state.LoginStopped
        && state.LastAppliedFrame == -1
        && state.LastReason == "not-run";

    return afterFirst && afterLogin && afterReset;
});

Test(538, "configuration placement keys default off and round-trip through both serializers", () =>
{
    var defaults = new Configuration();
    var configuration = new Configuration
    {
        TitleBackgroundCharaSelectPlacementEnabled = true,
        TitleBackgroundCharaSelectPlacementCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectPlacementPositionCaptured = true,
        TitleBackgroundCharaSelectPlacementPositionX = 12.5f,
        TitleBackgroundCharaSelectPlacementPositionY = -3.25f,
        TitleBackgroundCharaSelectPlacementPositionZ = 7f,
        TitleBackgroundCharaSelectPlacementRotation = 1.5f,
    };
    var applied = new Configuration();
    applied.ApplyFrom(configuration);

    var systemJson = JsonSerializer.Serialize(configuration);
    var systemRestored = JsonSerializer.Deserialize<Configuration>(systemJson);
    var newtonsoftJson = Newtonsoft.Json.JsonConvert.SerializeObject(configuration);
    var newtonsoftRestored = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(newtonsoftJson);

    return !defaults.TitleBackgroundCharaSelectPlacementEnabled
        && !defaults.TitleBackgroundCharaSelectPlacementPositionCaptured
        && applied.TitleBackgroundCharaSelectPlacementEnabled
        && applied.TitleBackgroundCharaSelectPlacementPositionCaptured
        && applied.TitleBackgroundCharaSelectPlacementPositionX == 12.5f
        && applied.TitleBackgroundCharaSelectPlacementRotation == 1.5f
        && systemJson.Contains("\"TitleBackgroundCharaSelectPlacementEnabled\"", StringComparison.Ordinal)
        && systemRestored != null && systemRestored.TitleBackgroundCharaSelectPlacementPositionCaptured
        && systemRestored.TitleBackgroundCharaSelectPlacementPositionZ == 7f
        && newtonsoftJson.Contains("\"TitleBackgroundCharaSelectPlacementCandidateId\"", StringComparison.Ordinal)
        && newtonsoftRestored != null && newtonsoftRestored.TitleBackgroundCharaSelectPlacementEnabled
        && newtonsoftRestored.TitleBackgroundCharaSelectPlacementPositionY == -3.25f;
});

Test(539, "configuration normalization fails closed on non-finite placement coordinates", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundCharaSelectPlacementEnabled = true,
        TitleBackgroundCharaSelectPlacementPositionCaptured = true,
        TitleBackgroundCharaSelectPlacementPositionX = float.NaN,
        TitleBackgroundCharaSelectPlacementPositionY = 1f,
        TitleBackgroundCharaSelectPlacementPositionZ = 2f,
        TitleBackgroundCharaSelectPlacementRotation = 0f,
    };
    var applied = new Configuration();
    applied.ApplyFrom(configuration);

    // ApplyFrom itself clears captured when a coordinate is non-finite; coordinates are sanitized to 0.
    return !applied.TitleBackgroundCharaSelectPlacementPositionCaptured
        && applied.TitleBackgroundCharaSelectPlacementPositionX == 0f;
});

Test(540, "OneClick placement proof is a runtime bool and recovery preserves persistent placement", () =>
{
    var root = FindRepositoryRoot();
    string Read(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", file));

    var quickCheck = Read("TitleScreenBackgroundService.QuickCheck.cs");
    var oneClick = Read("TitleScreenBackgroundService.OneClickVerification.cs");
    var recovery = Read("TitleBackgroundAutomaticCheckRecovery.cs");
    var runtimeState = Read("TitleBackgroundAutomaticCheckRuntimeState.cs");

    // proof arm は runtime bool のみ。config フリップ（PlacementEnabled=true / V2Enabled=false の一時保存）も
    // config Save もしない（run 終了後に config 差分が残らない）。
    var armBody = ExtractMethodBody(quickCheck, "private void ArmAutomaticPlacementProof(");
    var armOk = armBody.Contains("_automaticCheck.PlacementProofArmed = true;", StringComparison.Ordinal)
        && !armBody.Contains("TitleBackgroundCharaSelectPlacementEnabled = true", StringComparison.Ordinal)
        && !armBody.Contains("TitleBackgroundV2Enabled = false", StringComparison.Ordinal)
        && !armBody.Contains("_configuration.Save()", StringComparison.Ordinal);

    // 両 OneClick 入口が baseline 準備の後に arm する。
    var wiredInBoth = quickCheck.Contains("ArmAutomaticPlacementProof();", StringComparison.Ordinal)
        && oneClick.Contains("ArmAutomaticPlacementProof();", StringComparison.Ordinal);

    // arming 中の一時設定は snapshot に作らない。一方、既存の persistent placement は
    // failed/partial run から保護するため recovery journal に保持する。
    var recoveryOk = recovery.Contains("CharaSelectPlacementEnabled", StringComparison.Ordinal)
        && recovery.Contains("CharaSelectPlacementCandidateId", StringComparison.Ordinal)
        && recovery.Contains("CharaSelectPlacementPositionCaptured", StringComparison.Ordinal)
        && recovery.Contains("CharaSelectPlacementPositionX", StringComparison.Ordinal)
        && recovery.Contains("CharaSelectPlacementRotation", StringComparison.Ordinal);

    // 廃止したメソッドは残っていない。runtime state に proof bool + completed-run snapshot がある。
    var stateOk = !quickCheck.Contains("ArmCharaSelectPlacementForAutomaticRun", StringComparison.Ordinal)
        && runtimeState.Contains("public bool PlacementProofArmed", StringComparison.Ordinal)
        && runtimeState.Contains("CompletedRunProof", StringComparison.Ordinal);

    return armOk && wiredInBoth && recoveryOk && stateOk;
});

Test(541, "simple auto setup arms verified V2 as the persistent baseline and leaves the placement path off", () =>
{
    // 点8: 恒久 production baseline は verified Il Mheg V2。placement path は OneClick run が
    // run-scoped に arm/restore するため、恒久設定では有効化しない。
    var configuration = new Configuration();
    TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(configuration, "custom:n4f4");

    return configuration.TitleBackgroundOverrideEnabled
        && configuration.TitleBackgroundV2Enabled
        && !configuration.TitleBackgroundCharaSelectPlacementEnabled
        && TitleBackgroundQuickCheckUiPresenter.IsSimpleAutoSetupConfigured(configuration);
});

Test(542, "simple reset clears the placement path and service disposes placement state on every lifecycle boundary", () =>
{
    var root = FindRepositoryRoot();
    string Read(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", file));

    var quickCheck = Read("TitleScreenBackgroundService.QuickCheck.cs");
    var service = Read("TitleScreenBackgroundService.cs");

    return quickCheck.Contains("TitleBackgroundCharaSelectPlacementEnabled = false;", StringComparison.Ordinal)
        && quickCheck.Contains("_charaSelectPlacement.Reset();", StringComparison.Ordinal)
        && service.Contains("_charaSelectPlacement.Reset();", StringComparison.Ordinal)
        // dispose / reload / clear override / OFF も placement 状態を破棄する。
        && CountOccurrences(service, "_charaSelectPlacement.Reset();") >= 4
        // イベント購読は dispose で必ず解除する（lifetime safety）。
        && service.Contains("_charaSelectService.SelectedCharacterChanged -= OnCharaSelectSelectionChanged;", StringComparison.Ordinal);
});

Test(543, "placement diagnostics come from a single key source that feeds both the automatic-report branch and the allow-list", () =>
{
    var root = FindRepositoryRoot();
    string Read(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", file));

    var diagnostics = Read("TitleScreenBackgroundService.Diagnostics.cs");
    var quickCheckSource = Read("TitleBackgroundQuickCheck.cs");
    var diagStatic = Read("TitleBackgroundCharaSelectPlacementDiagnostics.cs");
    var placement = Read("TitleScreenBackgroundService.CharaSelectPlacement.cs");

    // 非詳細（automatic report）ブランチが placement 行を出す。
    var branchStart = diagnostics.IndexOf("if (!includeDetailedPhase2Diagnostics)", StringComparison.Ordinal);
    var branchReturn = branchStart >= 0 ? diagnostics.IndexOf("return", branchStart, StringComparison.Ordinal) : -1;
    var branchEnd = branchReturn >= 0 ? diagnostics.IndexOf("];", branchReturn, StringComparison.Ordinal) : -1;
    var nonDetailedBranch = branchReturn >= 0 && branchEnd > branchReturn
        ? diagnostics[branchReturn..branchEnd]
        : string.Empty;
    var nonDetailedOk = nonDetailedBranch.Contains("BuildCharaSelectPlacementDiagnosticLines()", StringComparison.Ordinal)
        && nonDetailedBranch.Contains("v2.active=", StringComparison.Ordinal);

    // 詳細ブランチも同じ単一ソースから出す。
    var detailedOk = diagnostics.Contains("lines.AddRange(BuildCharaSelectPlacementDiagnosticLines());", StringComparison.Ordinal);

    // allow-list は placement 診断キーを単一ソースから UnionWith する（実装と乖離しない, skill §3）。
    var allowListOk = quickCheckSource.Contains(
        "IncludedKeys.UnionWith(TitleBackgroundCharaSelectPlacementDiagnostics.Keys)", StringComparison.Ordinal);

    // 点9 + REPORT SEMANTICS: actual run ownership キー + resolver / capture キーが単一ソース配列に含まれ、Build が出す。
    string[] requiredKeys =
    [
        "automaticRun.engineOwner",
        "automaticRun.placementProofArmed",
        "automaticRun.v2Suppressed",
        "automaticRun.preLoginPlacementObserved",
        "automaticRun.reportSource",
        "charaselect.placement.resolve.source",
        "charaselect.placement.resolve.mappingHit",
        "charaselect.placement.resolve.objectResolved",
        "charaselect.placement.resolve.drawReady",
        "charaselect.placement.resolve.retryCount",
        "charaselect.placement.capture.positionCaptured",
        "charaselect.placement.capture.zeroPositionAccepted",
        "charaselect.placement.capture.stableSamples",
        "charaselect.placement.applyCount",
        "charaselect.placement.trigger",
        "charaselect.placement.legacyOwnershipInactive",
        "charaselect.placement.loginStopped",
        "charaselect.placement.lastPreLoginReason",
    ];
    foreach (var key in requiredKeys)
    {
        if (!diagStatic.Contains($"\"{key}\"", StringComparison.Ordinal)
            || !diagStatic.Contains($"$\"{key}=", StringComparison.Ordinal))
        {
            return false;
        }
    }

    // raw pointer は report へ出さない（点3）。
    var noRawPointer = !diagStatic.Contains("resolvedCharacterAddress", StringComparison.Ordinal)
        && !placement.Contains("LastResolvedCharacterAddress", StringComparison.Ordinal);

    return nonDetailedOk && detailedOk && allowListOk && noRawPointer;
});

Test(544, "identity resolution is centralised in CharaSelectService and TitleBackground only consumes it", () =>
{
    var root = FindRepositoryRoot();
    string ReadCharaSelect(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "CharaSelect", file));
    string ReadTitleBg(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", file));

    var resolver = ReadCharaSelect("CharaSelectService.SelectedCharacterIdentity.cs");
    var probe = ReadTitleBg("TitleBackgroundCharacterSourceProbe.cs");
    var placement = ReadTitleBg("TitleScreenBackgroundService.CharaSelectPlacement.cs");

    // canonical resolver は CharaSelectService 側の唯一の実装。primary = GetCurrentCharacter ポインタと
    // CharacterMapping -> ClientObjectIndex -> GetObjectByIndex の一致。selected index/content-id は
    // strong source、hovered は fallback。pointer は frame 内のみ、runtime state へ保存しない。
    var resolverOk = resolver.Contains("internal bool TryResolveCurrentCharaSelectActor(", StringComparison.Ordinal)
        && resolver.Contains("agent->SelectedCharacterIndex", StringComparison.Ordinal)
        && resolver.Contains("GetCharacterEntryByIndex", StringComparison.Ordinal)
        && resolver.Contains("SelectedCharacterContentId", StringComparison.Ordinal)
        && resolver.Contains("HoveredCharacterContentId", StringComparison.Ordinal)
        && resolver.Contains("CharacterMapping", StringComparison.Ordinal)
        && resolver.Contains("ClientObjectIndex", StringComparison.Ordinal)
        && resolver.Contains("GetObjectByIndex", StringComparison.Ordinal)
        && resolver.Contains("CharaSelectCharacterList.GetCurrentCharacter()", StringComparison.Ordinal)
        && resolver.Contains("TryResolveCurrentCharacterMapping", StringComparison.Ordinal)
        && !resolver.Contains("MapMarker", StringComparison.Ordinal)
        && !resolver.Contains("Correspondence", StringComparison.Ordinal)
        && !resolver.Contains("_lastSelectedCharacterDetourIndex", StringComparison.Ordinal);

    // TitleBackground は独自 resolver を持たない。write/read helper は canonical resolver が返す
    // CharaSelectResolvedActorContext だけを受け取り、raw pointer を迂回する bypass を持たない。
    var probeOk = !probe.Contains("ResolveCurrentCharaSelectCharacterAddress", StringComparison.Ordinal)
        && !probe.Contains("nint characterAddress", StringComparison.Ordinal)
        && probe.Contains("in CharaSelectResolvedActorContext actor,", StringComparison.Ordinal)
        && probe.Contains("actor.CharacterAddress", StringComparison.Ordinal);

    var placementBody = ExtractMethodBody(placement, "private void MaintainTitleEditInformedCharaSelectPlacement(");
    var placementOk = placementBody.Contains("_charaSelectService?.TryResolveCurrentCharaSelectActor(out actor)", StringComparison.Ordinal)
        && !placementBody.Contains("CharaSelectCharacterList.GetCurrentCharacter()", StringComparison.Ordinal)
        && placementBody.Contains("TrySetCharaSelectCharacterPosition(", StringComparison.Ordinal)
        // capture / write / readback が共有する唯一の同一フレーム context。
        && placementBody.Contains("var resolvedContext = new TitleBackgroundResolvedActorContext(", StringComparison.Ordinal);

    // 選択キャラ変更イベントは既存 detour を再利用（新 native hook を足さない）。
    var eventOk = ReadCharaSelect("CharaSelectService.NativeHooks.cs")
            .Contains("NotifySelectedCharacterObserved(", StringComparison.Ordinal)
        && resolver.Contains("SelectedCharacterChanged", StringComparison.Ordinal);

    return resolverOk && probeOk && placementOk && eventOk;
});

Test(545, "one-click evidence capture is read-only, run-scoped, stable-sample gated, bounded, and accepts a zero position", () =>
{
    var root = FindRepositoryRoot();
    var placement = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground",
        "TitleScreenBackgroundService.CharaSelectPlacement.cs"));

    // capture は write と同じ Framework frame・同じ canonical context で完結する。
    // 専用 hook 呼び出しは足さず、MaintainTitleEditInformedCharaSelectPlacement 内から呼ぶ。
    var maintainBody = ExtractMethodBody(placement, "private void MaintainTitleEditInformedCharaSelectPlacement(");
    var captureBody = ExtractMethodBody(placement, "private void CaptureCharaSelectPlacementProof(");

    return maintainBody.Contains("CaptureCharaSelectPlacementProof(resolvedContext);", StringComparison.Ordinal)
        // read-only: only TryRead*, never TrySet*/SetPosition/SetRotation inside the capture.
        && captureBody.Contains("TryReadCharaSelectCharacterTransform(", StringComparison.Ordinal)
        && !captureBody.Contains("TrySetCharaSelectCharacterPosition(", StringComparison.Ordinal)
        && !captureBody.Contains("SetRotation(", StringComparison.Ordinal)
        // pre-login + proof-arm / persistent の両経路。既存の永続 capture があっても proof run は fresh に採取。
        && captureBody.Contains("IsCharaSelectPlacementActive", StringComparison.Ordinal)
        && captureBody.Contains("_clientState.IsLoggedIn", StringComparison.Ordinal)
        // 安定サンプル + bounded timeout（fail-closed）。
        && captureBody.Contains("EvaluateCaptureSampleStreak(", StringComparison.Ordinal)
        && captureBody.Contains("IsCaptureStreakSatisfied(streak)", StringComparison.Ordinal)
        && captureBody.Contains("IsCaptureBudgetExceeded(framesElapsed)", StringComparison.Ordinal)
        && captureBody.Contains("MarkCaptureTimedOut()", StringComparison.Ordinal)
        // 点5: (0,0,0) は拒否しない。zeroAccepted として記録するだけ（early return しない）。
        && captureBody.Contains("zeroAccepted", StringComparison.Ordinal)
        && !captureBody.Contains("if (TitleBackgroundCharacterSourceEvaluation.IsZeroPosition(capturedPosition))", StringComparison.Ordinal);
});

Test(547, "load-time V2 to placement auto-migration is withdrawn; a curated V2 config stays on V2", () =>
{
    var root = FindRepositoryRoot();
    var configSource = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Configuration.TitleBackground.cs"));

    static Configuration CuratedV2() => new()
    {
        TitleBackgroundOverrideEnabled = true,
        TitleBackgroundCameraOverrideEnabled = true,
        TitleBackgroundIntegratedCompositionEnabled = true,
        TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.CharaSelectOnly,
        TitleBackgroundV2Enabled = true,
        TitleBackgroundCharaSelectPlacementEnabled = false,
        TitleBackgroundCharacterSelectOverrideCandidateId = "custom:n4f4",
        TitleBackgroundTerritoryPath = "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
    };

    var loaded = new Configuration();
    loaded.ApplyFrom(CuratedV2());

    // 点8: 移行は撤回。verified V2 baseline を勝手に placement へ移さない。
    var stayedOnV2 = loaded.TitleBackgroundV2Enabled
        && !loaded.TitleBackgroundCharaSelectPlacementEnabled
        && loaded.TitleBackgroundCharacterSelectOverrideCandidateId == "custom:n4f4"
        && loaded.TitleBackgroundTerritoryPath == "ex3/01_nvt_n4/fld/n4f4/level/n4f4";

    // ソースにも移行ロジックが残っていない。
    var noMigrationInSource = !configSource.Contains("go-forward 機構である TitleEdit-informed placement path へ 1 回だけ移す", StringComparison.Ordinal)
        && !configSource.Contains("TitleBackgroundCharaSelectPlacementEnabled = true;", StringComparison.Ordinal);

    return stayedOnV2 && noMigrationInSource;
});

Test(548, "auto-copy report prefixes the placement runtime proof line via selector-bypass; no local path / build marker / assembly identity leaks", () =>
{
    var root = FindRepositoryRoot();
    string Read(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", file));

    var quickCheck = Read("TitleBackgroundQuickCheck.cs");
    var service = Read("TitleScreenBackgroundService.QuickCheck.cs");
    var oneClick = Read("TitleScreenBackgroundService.OneClickVerification.cs");
    var placement = Read("TitleScreenBackgroundService.CharaSelectPlacement.cs");

    // The QuickCheck section, the OneClick failure header, and the failure fallback are all prefixed
    // with the placement runtime proof line (selector never sees it).
    var runtimeProofBody = ExtractMethodBody(placement, "internal IReadOnlyList<string> BuildAutomaticCheckRuntimeProofLines(");
    var runtimeProofOk = runtimeProofBody.Contains("BuildCharaSelectPlacementRuntimeProofLine()", StringComparison.Ordinal);
    var completeBody = ExtractMethodBody(service, "private void CompleteAutomaticQuickCheck(");
    var completeOk = completeBody.Contains("BuildAutomaticCheckRuntimeProofLines()", StringComparison.Ordinal);
    var oneClickOk = oneClick.Contains("new List<string>(BuildAutomaticCheckRuntimeProofLines())", StringComparison.Ordinal);
    var fallbackBody = ExtractMethodBody(service, "private string BuildAutomaticCheckFailureFallback(");
    var fallbackOk = fallbackBody.Contains("BuildCharaSelectPlacementRuntimeProofLine()", StringComparison.Ordinal);

    // The placement proof line reports the actual owner + proof-arm + resolve status, and prefers the
    // completed-run snapshot over live runtime (REPORT SEMANTICS).
    var proofBody = ExtractMethodBody(placement, "internal string BuildCharaSelectPlacementRuntimeProofLine(");
    var proofOk = proofBody.Contains("engineOwner={FormatNone(owner)}", StringComparison.Ordinal)
        && proofBody.Contains("placementProofArmed={proofArmed}", StringComparison.Ordinal)
        && proofBody.Contains("_automaticCheck.CompletedRunProof", StringComparison.Ordinal)
        && proofBody.Contains("characterResolveStatus={FormatNone(resolveStatus)}", StringComparison.Ordinal);

    // Temporary stale-load instrumentation was removed after the real-game proof: no local path,
    // build marker, or assembly identity anywhere in the automatic report / fallback / runtime proof path.
    var buildBody = ExtractMethodBody(quickCheck, "public static string Build(");
    var noInstrumentation =
        !buildBody.Contains("assembly.location", StringComparison.Ordinal)
        && !buildBody.Contains("assembly.version", StringComparison.Ordinal)
        && !buildBody.Contains("build.marker", StringComparison.Ordinal)
        && !fallbackBody.Contains("assembly.location", StringComparison.Ordinal)
        && !fallbackBody.Contains("build.marker", StringComparison.Ordinal)
        && !runtimeProofBody.Contains("assembly.location", StringComparison.Ordinal)
        && !runtimeProofBody.Contains("build.marker", StringComparison.Ordinal)
        && !placement.Contains("TitleBackgroundAssemblyIdentity", StringComparison.Ordinal)
        && !quickCheck.Contains("TitleBackgroundBuildMarker", StringComparison.Ordinal);

    return runtimeProofOk && completeOk && oneClickOk && fallbackOk && proofOk && noInstrumentation;
});

Test(549, "identity content-id resolve order is live selected-index first, selected-content-id second, hovered last, available only", () =>
{
    var all = CharaSelectSelectedCharacterIdentityLogic.BuildResolveOrder(
        hasCurrentCharacterMapping: false, hasSelectedIndexContentId: true, hasSelectedContentId: true, hasHoveredContentId: true);
    var hoveredOnly = CharaSelectSelectedCharacterIdentityLogic.BuildResolveOrder(
        hasCurrentCharacterMapping: false, hasSelectedIndexContentId: false, hasSelectedContentId: false, hasHoveredContentId: true);
    var detourAndHovered = CharaSelectSelectedCharacterIdentityLogic.BuildResolveOrder(
        hasCurrentCharacterMapping: false, hasSelectedIndexContentId: true, hasSelectedContentId: false, hasHoveredContentId: true);
    var none = CharaSelectSelectedCharacterIdentityLogic.BuildResolveOrder(
        hasCurrentCharacterMapping: false, hasSelectedIndexContentId: false, hasSelectedContentId: false, hasHoveredContentId: false);

    return all.Count == 3
        && all[0] == CharaSelectIdentityResolveSource.SelectedCharacterIndex
        && all[1] == CharaSelectIdentityResolveSource.SelectedContentId
        && all[2] == CharaSelectIdentityResolveSource.HoveredCharacter
        && hoveredOnly.Count == 1 && hoveredOnly[0] == CharaSelectIdentityResolveSource.HoveredCharacter
        && detourAndHovered.Count == 2
        && detourAndHovered[0] == CharaSelectIdentityResolveSource.SelectedCharacterIndex
        && detourAndHovered[1] == CharaSelectIdentityResolveSource.HoveredCharacter
        && none.Count == 0
        // 100 オフセット正規化 + API15 SelectedCharacterIndex の 0xFF sentinel / 範囲外は -1。
        && CharaSelectSelectedCharacterIdentityLogic.NormalizeSelectedIndex(103) == 3
        && CharaSelectSelectedCharacterIdentityLogic.NormalizeSelectedIndex(2) == 2
        && CharaSelectSelectedCharacterIdentityLogic.NormalizeSelectedIndex(0xFF) == -1
        && CharaSelectSelectedCharacterIdentityLogic.NormalizeSelectedIndex(-1) == -1
        && CharaSelectSelectedCharacterIdentityLogic.NormalizeSelectedIndex(99) == -1;
});

Test(550, "capture stable-sample streak resets on jitter, satisfies at 5 consecutive, and the frame budget is bounded", () =>
{
    var p0 = new Vector3(1f, 2f, 3f);
    // first sample -> streak 1.
    var s1 = TitleBackgroundCharaSelectPlacementLogic.EvaluateCaptureSampleStreak(
        hasPreviousSample: false, default, 0f, p0, 0.5f, currentStreak: 0);
    // stable next -> 2.
    var s2 = TitleBackgroundCharaSelectPlacementLogic.EvaluateCaptureSampleStreak(
        hasPreviousSample: true, p0, 0.5f, new Vector3(1.005f, 2f, 3f), 0.505f, currentStreak: s1);
    // jitter beyond epsilon -> reset to 1.
    var jittered = TitleBackgroundCharaSelectPlacementLogic.EvaluateCaptureSampleStreak(
        hasPreviousSample: true, p0, 0.5f, new Vector3(1.5f, 2f, 3f), 0.5f, currentStreak: s2);
    // non-finite -> 0.
    var nonFinite = TitleBackgroundCharaSelectPlacementLogic.EvaluateCaptureSampleStreak(
        hasPreviousSample: true, p0, 0.5f, new Vector3(float.NaN, 2f, 3f), 0.5f, currentStreak: 4);

    return s1 == 1 && s2 == 2 && jittered == 1 && nonFinite == 0
        && !TitleBackgroundCharaSelectPlacementLogic.IsCaptureStreakSatisfied(4)
        && TitleBackgroundCharaSelectPlacementLogic.IsCaptureStreakSatisfied(5)
        && TitleBackgroundCharaSelectPlacementLogic.CaptureStableSampleTarget == 5
        && !TitleBackgroundCharaSelectPlacementLogic.IsCaptureBudgetExceeded(1)
        && TitleBackgroundCharaSelectPlacementLogic.IsCaptureBudgetExceeded(
            TitleBackgroundCharaSelectPlacementLogic.CaptureFrameBudget);
});

Test(551, "captured (0,0,0) is a valid source-backed position; capture validity ignores draw-ready and rejects non-finite only", () =>
{
    var zeroCaptured = TitleBackgroundCharaSelectPlacementLogic.BuildLocationModel(
        "ex3/01_nvt_n4/fld/n4f4/level/n4f4", 816, 0, positionCaptured: true, 0f, 0f, 0f, 0f);
    var okValidity = TitleBackgroundCharaSelectPlacementLogic.EvaluateCaptureValidity(
        mappingHit: true, objectResolved: true, drawReady: true,
        activeSceneGeneration: 4, runtimeSceneGeneration: 4, position: new Vector3(0f, 0f, 0f), rotation: 0f);
    var notReady = TitleBackgroundCharaSelectPlacementLogic.EvaluateCaptureValidity(
        mappingHit: true, objectResolved: true, drawReady: false,
        activeSceneGeneration: 4, runtimeSceneGeneration: 4, position: new Vector3(1f, 1f, 1f), rotation: 0f);
    var genMismatch = TitleBackgroundCharaSelectPlacementLogic.EvaluateCaptureValidity(
        mappingHit: true, objectResolved: true, drawReady: true,
        activeSceneGeneration: 4, runtimeSceneGeneration: 5, position: new Vector3(1f, 1f, 1f), rotation: 0f);
    var nonFinite = TitleBackgroundCharaSelectPlacementLogic.EvaluateCaptureValidity(
        mappingHit: true, objectResolved: true, drawReady: true,
        activeSceneGeneration: 4, runtimeSceneGeneration: 4, position: new Vector3(float.NaN, 0f, 0f), rotation: 0f);

    return zeroCaptured.HasSourceBackedPosition
        && okValidity == "ok"
        && notReady == "ok"
        && genMismatch == "scene-generation-mismatch"
        && nonFinite == "non-finite-transform";
});

Test(552, "placement write triggers on capture-complete and selection-change; same generation+actor with no trigger is a no-op", () =>
{
    // 点7: capture 完了 / generation 変化 / selection 変化 のみ。同一 (gen, actor) へ連続 write しない。
    var sameNoTrigger = TitleBackgroundPlacementTestShims.ShouldWrite(
        3, 3, 0x100, 0x100, captureJustCompleted: false, selectionChangePending: false);
    var captureComplete = TitleBackgroundPlacementTestShims.ShouldWrite(
        3, 3, 0x100, 0x100, captureJustCompleted: true, selectionChangePending: false);
    var selectionChange = TitleBackgroundPlacementTestShims.ShouldWrite(
        3, 3, 0x100, 0x100, captureJustCompleted: false, selectionChangePending: true);
    var genChange = TitleBackgroundPlacementTestShims.ShouldWrite(
        4, 3, 0x100, 0x100);
    var nullActor = TitleBackgroundPlacementTestShims.ShouldWrite(
        3, 3, 0x0, 0x100, captureJustCompleted: true, selectionChangePending: true);

    var trigCapture = TitleBackgroundPlacementTestShims.Trigger(
        3, 3, 0x100, 0x100, captureJustCompleted: true, selectionChangePending: false);
    var trigGen = TitleBackgroundPlacementTestShims.Trigger(
        4, 3, 0x100, 0x100, captureJustCompleted: false, selectionChangePending: false);
    var trigSelection = TitleBackgroundPlacementTestShims.Trigger(
        3, 3, 0x200, 0x100, captureJustCompleted: false, selectionChangePending: true);

    return !sameNoTrigger && captureComplete && selectionChange && genChange && !nullActor
        && trigCapture == TitleBackgroundCharaSelectPlacementTrigger.CaptureComplete
        && trigGen == TitleBackgroundCharaSelectPlacementTrigger.SceneGeneration
        && trigSelection == TitleBackgroundCharaSelectPlacementTrigger.SelectionChange;
});

Test(553, "pre-login resolver diagnostics freeze at login and the pre-login gate reason is preserved", () =>
{
    var state = new TitleBackgroundCharaSelectPlacementRuntimeState();
    state.RecordCharacterResolve(
        resolved: true, resolveSource: "SelectedCharacterIndex",
        entryAvailable: true, selectedContentAvailable: true, mappingAvailable: true,
        mappingHit: true, clientObjectIndexValid: true, objectResolved: true, drawReady: true,
        retryCount: 0, actorChanged: false);
    state.RecordSkip("candidate-mismatch");

    // login 検出 -> freeze。
    state.MarkLoginStopped();
    var frozenSource = state.LastResolveSource;
    var frozenPreLoginReason = state.LastPreLoginReason;

    // login 後の resolver 更新は無視される（pre-login 状態を保持, 点9）。
    state.RecordCharacterResolve(
        resolved: false, resolveSource: "None",
        entryAvailable: false, selectedContentAvailable: false, mappingAvailable: false,
        mappingHit: false, clientObjectIndexValid: false, objectResolved: false, drawReady: false,
        retryCount: 9, actorChanged: true);

    return frozenSource == "SelectedCharacterIndex"
        && frozenPreLoginReason == "candidate-mismatch"
        && state.LastResolveSource == "SelectedCharacterIndex"
        && state.LastObjectResolved
        && state.RetryCount == 0
        && state.LoginStopped
        && state.LastReason == "logged-in"
        && state.LastPreLoginReason == "candidate-mismatch";
});

Test(554, "engine owner: proof arm wins over baseline V2 config without any config flip", () =>
{
    var O = TitleBackgroundCharaSelectEngineOwner.None;
    _ = O;

    // failure mode 1: baseline V2Enabled=true / PlacementEnabled=false でも proof arm 中は owner=PlacementProof。
    var proofOwner = TitleBackgroundCharaSelectEngineOwnerLogic.Resolve(
        overrideEnabled: true, automaticPlacementProofArmed: true,
        persistentPlacementEnabled: false, v2Enabled: true);
    // proof を解除すると config どおり V2 に戻る。
    var v2Owner = TitleBackgroundCharaSelectEngineOwnerLogic.Resolve(
        overrideEnabled: true, automaticPlacementProofArmed: false,
        persistentPlacementEnabled: false, v2Enabled: true);
    var persistentPlacementOwner = TitleBackgroundCharaSelectEngineOwnerLogic.Resolve(
        overrideEnabled: true, automaticPlacementProofArmed: false,
        persistentPlacementEnabled: true, v2Enabled: true);
    var noneWhenOverrideOff = TitleBackgroundCharaSelectEngineOwnerLogic.Resolve(
        overrideEnabled: false, automaticPlacementProofArmed: true,
        persistentPlacementEnabled: true, v2Enabled: true);

    return proofOwner == TitleBackgroundCharaSelectEngineOwner.PlacementProof
        && TitleBackgroundCharaSelectEngineOwnerLogic.IsPlacementOwner(proofOwner)
        && !TitleBackgroundCharaSelectEngineOwnerLogic.IsV2Owner(proofOwner)
        && TitleBackgroundCharaSelectEngineOwnerLogic.IsNewEngineOwner(proofOwner)
        && TitleBackgroundCharaSelectEngineOwnerLogic.IsV2SuppressedByProof(proofOwner, v2Enabled: true)
        && v2Owner == TitleBackgroundCharaSelectEngineOwner.V2
        && TitleBackgroundCharaSelectEngineOwnerLogic.IsV2Owner(v2Owner)
        && persistentPlacementOwner == TitleBackgroundCharaSelectEngineOwner.PlacementProof == false
        && persistentPlacementOwner == TitleBackgroundCharaSelectEngineOwner.Placement
        && noneWhenOverrideOff == TitleBackgroundCharaSelectEngineOwner.None
        && TitleBackgroundCharaSelectEngineOwnerLogic.Describe(proofOwner) == "placement-proof";
});

Test(555, "OneClick arms the proof after all baseline setup/reload and never disarms it on the WaitingForCharacterSelect -> Collecting transition", () =>
{
    var root = FindRepositoryRoot();
    string Read(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", file));

    var oneClick = Read("TitleScreenBackgroundService.OneClickVerification.cs");
    var quickCheck = Read("TitleScreenBackgroundService.QuickCheck.cs");

    // failure mode 2: arm は PrepareAutomaticQuickCheckDiagnostics() の後、Requested=true の前。
    var startBody = ExtractMethodBody(oneClick, "public IReadOnlyList<string> StartOneClickTitleBackgroundVerification(")
        + ExtractMethodBody(oneClick, "private IReadOnlyList<string> ContinueOneClickAfterSourceCapture(");
    var prepIdx = startBody.IndexOf("PrepareAutomaticQuickCheckDiagnostics();", StringComparison.Ordinal);
    var armIdx = startBody.IndexOf("ArmAutomaticPlacementProof();", StringComparison.Ordinal);
    var reqIdx = startBody.IndexOf("_automaticCheck.Requested = true;", StringComparison.Ordinal);
    var orderOk = prepIdx >= 0 && armIdx > prepIdx && reqIdx > armIdx;

    // failure mode 3: WaitingForCharacterSelect -> Collecting を担う ArmAutomaticQuickCheck / UpdateAutomaticQuickCheck は
    // PlacementProofArmed を触らない（proof ownership を維持）。
    var armAutoBody = ExtractMethodBody(quickCheck, "private void ArmAutomaticQuickCheck(");
    var updateBody = ExtractMethodBody(quickCheck, "private void UpdateAutomaticQuickCheck(");
    var preservedAcrossTransition = !armAutoBody.Contains("PlacementProofArmed", StringComparison.Ordinal)
        && !updateBody.Contains("PlacementProofArmed = false", StringComparison.Ordinal);

    // disarm は run 終了・失敗・cancel・reset・dispose だけ。
    var disarmBody = ExtractMethodBody(quickCheck, "private void DisarmAutomaticPlacementProof(");
    var disarmOnlyClearsRuntime = disarmBody.Contains("_automaticCheck.PlacementProofArmed = false;", StringComparison.Ordinal)
        && !disarmBody.Contains("_configuration.", StringComparison.Ordinal);

    return orderOk && preservedAcrossTransition && disarmOnlyClearsRuntime;
});

Test(556, "login path: freeze -> completed-run snapshot -> stop; runtime reset happens only after the report is built", () =>
{
    var root = FindRepositoryRoot();
    string Read(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", file));

    var placement = Read("TitleScreenBackgroundService.CharaSelectPlacement.cs");
    var quickCheck = Read("TitleScreenBackgroundService.QuickCheck.cs");

    // failure mode 4: Stop 分岐で MarkLoginStopped() の直後に CaptureCompletedRunProofSnapshot("login")。
    var maintainBody = ExtractMethodBody(placement, "private void MaintainTitleEditInformedCharaSelectPlacement(");
    var freezeIdx = maintainBody.IndexOf("_charaSelectPlacement.MarkLoginStopped();", StringComparison.Ordinal);
    var snapIdx = maintainBody.IndexOf("CaptureCompletedRunProofSnapshot(\"login\");", StringComparison.Ordinal);
    var freezeThenSnapshot = freezeIdx >= 0 && snapIdx > freezeIdx;

    // Complete: snapshot 確定は QuickCheck 評価より前（report 生成前）、disarm→runtime Reset はその後（report 生成後）。
    var completeBody = ExtractMethodBody(quickCheck, "private void CompleteAutomaticQuickCheck(");
    var snapCallIdx = completeBody.IndexOf("CaptureCompletedRunProofSnapshot(\"complete\");", StringComparison.Ordinal);
    var evalIdx = completeBody.IndexOf("var result = EvaluateQuickCheck();", StringComparison.Ordinal);
    var resetIdx = completeBody.IndexOf("_charaSelectPlacement.Reset();", StringComparison.Ordinal);
    var disarmIdx = completeBody.IndexOf("DisarmAutomaticPlacementProof();", StringComparison.Ordinal);
    var orderOk = snapCallIdx >= 0 && evalIdx > snapCallIdx
        && disarmIdx > evalIdx && resetIdx > disarmIdx;

    // CaptureProofSnapshot は 1 回だけ（既に取得済みなら上書きしない）。
    var captureHelper = ExtractMethodBody(quickCheck, "private void CaptureCompletedRunProofSnapshot(");
    var idempotent = captureHelper.Contains("_automaticCheck.CompletedRunProof != null", StringComparison.Ordinal)
        && captureHelper.Contains("!_automaticCheck.PlacementProofArmed", StringComparison.Ordinal);

    return freezeThenSnapshot && orderOk && idempotent;
});

Test(557, "report reads the completed-run proof snapshot, never re-evaluated live state after restore", () =>
{
    var root = FindRepositoryRoot();
    string Read(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", file));

    var placement = Read("TitleScreenBackgroundService.CharaSelectPlacement.cs");
    var quickCheck = Read("TitleScreenBackgroundService.QuickCheck.cs");

    // failure mode 6: 診断行は CompletedRunProof があればそこから、無ければ live snapshot。
    var diagBody = ExtractMethodBody(placement, "internal IEnumerable<string> BuildCharaSelectPlacementDiagnosticLines(");
    var readsSnapshot = diagBody.Contains("_automaticCheck.CompletedRunProof", StringComparison.Ordinal)
        && diagBody.Contains("completed ??", StringComparison.Ordinal)
        && diagBody.Contains("reportFromSnapshot", StringComparison.Ordinal);

    // Finalize は restore 状態を追記するだけ（placement proof を live から再評価しない）。
    var finalizeBody = ExtractMethodBody(quickCheck, "private void FinalizeAutomaticCheckReport(");
    var finalizeAppendsOnly = !finalizeBody.Contains("GetDiagnosticLines(", StringComparison.Ordinal)
        && !finalizeBody.Contains("EvaluateQuickCheck(", StringComparison.Ordinal)
        && !finalizeBody.Contains("BuildCharaSelectPlacementDiagnosticLines(", StringComparison.Ordinal)
        && finalizeBody.Contains("settingsRestored=", StringComparison.Ordinal);

    // proof snapshot は immutable record（reset 後も値が変わらない）。
    var state = new TitleBackgroundCharaSelectPlacementRuntimeState();
    state.RecordCharacterResolve(
        resolved: true, resolveSource: "SelectedCharacterIndex",
        entryAvailable: true, selectedContentAvailable: true, mappingAvailable: true,
        mappingHit: true, clientObjectIndexValid: true, objectResolved: true, drawReady: true,
        retryCount: 0, actorChanged: false);
    state.RecordPlacementApplied(3, 0x100, new Vector3(0f, 0f, 0f), 0f, 10, "capture-complete");
    state.MarkLoginStopped();
    var snap = state.CaptureProofSnapshot("placement-proof", placementProofArmed: true, positionCaptured: true, legacyOwnershipInactive: true);
    state.Reset();
    var snapshotSurvivesReset = snap.EngineOwner == "placement-proof"
        && snap.ApplyCount == 1
        && snap.Trigger == "capture-complete"
        && snap.LoginStopped
        && snap.PreLoginPlacementObserved
        && snap.LegacyOwnershipInactive
        && state.PlacementApplyCount == 0;

    return readsSnapshot && finalizeAppendsOnly && snapshotSurvivesReset;
});

Test(566, "manual QuickCheck/reset cannot reuse or leave a completed placement proof", () =>
{
    var root = FindRepositoryRoot();
    var quickCheck = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground",
        "TitleScreenBackgroundService.QuickCheck.cs"));

    var startBody = ExtractMethodBody(quickCheck, "public IReadOnlyList<string> StartQuickCheck()");
    var runBody = ExtractMethodBody(quickCheck, "public IReadOnlyList<string> RunQuickCheck()");
    var resetBody = ExtractMethodBody(quickCheck, "public IReadOnlyList<string> ResetQuickCheck()");

    var manualRunClearsProof = startBody.Contains("_automaticCheck.CompletedRunProof = null;", StringComparison.Ordinal)
        && startBody.Contains("_automaticCheck.ResetPlacementPromotion();", StringComparison.Ordinal);
    var manualCommandClearsProof = runBody.Contains("_automaticCheck.State != TitleBackgroundAutomaticCheckState.Collecting", StringComparison.Ordinal)
        && runBody.Contains("_automaticCheck.CompletedRunProof = null;", StringComparison.Ordinal)
        && runBody.Contains("_automaticCheck.ResetPlacementPromotion();", StringComparison.Ordinal);
    var resetClearsRuntime = resetBody.Contains("DisarmAutomaticPlacementProof();", StringComparison.Ordinal)
        && resetBody.Contains("_automaticCheck.CompletedRunProof = null;", StringComparison.Ordinal)
        && resetBody.Contains("_charaSelectPlacement.Reset();", StringComparison.Ordinal);

    return manualRunClearsProof && manualCommandClearsProof && resetClearsRuntime;
});

Test(567, "OneClick failure paths cannot report a stale completed placement proof", () =>
{
    var root = FindRepositoryRoot();
    var oneClick = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.OneClickVerification.cs"));
    var quickCheck = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.QuickCheck.cs"));
    var withoutTransaction = ExtractMethodBody(
        oneClick,
        "private IReadOnlyList<string> FailOneClickWithoutTransaction(");
    var withTransaction = ExtractMethodBody(
        oneClick,
        "private IReadOnlyList<string> FailOneClickWithReport(");
    var newRun = ExtractMethodBody(
        quickCheck,
        "private void ResetAutomaticCheckReportForNewRun()");
    return withoutTransaction.Contains("_automaticCheck.CompletedRunProof = null;", StringComparison.Ordinal)
        && withoutTransaction.Contains("_automaticCheck.ResetPlacementPromotion();", StringComparison.Ordinal)
        && withTransaction.Contains("_automaticCheck.CompletedRunProof = null;", StringComparison.Ordinal)
        && withTransaction.Contains("_automaticCheck.ResetPlacementPromotion();", StringComparison.Ordinal)
        && newRun.Contains("_automaticCheck.CompletedRunProof = null;", StringComparison.Ordinal);
});

Test(568, "REPORT SEMANTICS: confirmed placement run reports pre-login gate/reason as its meaningful final state, not the login-teardown wait", () =>
{
    // 再現: resolve→capture→write→readback→applied の後、login 遷移中（actor 破棄・zone load 中で
    // IsLoggedIn はまだ false）の待機フレームが last* gate/reason を object-resolve-timeout へ上書きする。
    var state = new TitleBackgroundCharaSelectPlacementRuntimeState();
    state.IncrementSceneGeneration();
    var generation = state.SceneGeneration;
    state.ObserveLifecycle(
        charaSelectSessionActive: true, activeSceneGeneration: generation,
        quickCheckCollecting: true, logoutTransition: true, attachedToActiveScene: true);
    // 初期の解決待ち（object 生成が 120 フレーム超）。
    state.RecordGateEvaluation("object-resolve-timeout", preLogin: true);
    state.RecordSkip("object-resolve-timeout");
    // 解決 → capture → write confirmed → applied。
    state.RecordCharacterResolve(
        resolved: true, resolveSource: "SelectedCharacterIndex",
        entryAvailable: true, selectedContentAvailable: true, mappingAvailable: true,
        mappingHit: true, clientObjectIndexValid: true, objectResolved: true, drawReady: false,
        retryCount: 0, actorChanged: true);
    state.RecordCapturePersisted(
        stableSamples: 5, zeroAccepted: true, candidateId: "custom:n4f4",
        position: Vector3.Zero, rotation: 0f);
    state.RecordPlacementWriteAttempt(
        generation, 0x100,
        setterCallCompleted: true, positionReadbackConfirmed: true, rotationReadbackConfirmed: true,
        status: "confirmed", clientObjectIndex: 7);
    state.RecordPlacementApplied(
        generation, 0x100, Vector3.Zero, 0f, frame: 12, trigger: "CaptureComplete", clientObjectIndex: 7);
    // login-teardown の待機フレームが last* を再度 timeout へ上書きする（バグの再現）。
    state.RecordGateEvaluation("object-resolve-timeout", preLogin: true);
    state.RecordSkip("object-resolve-timeout");
    state.MarkLoginStopped();

    var confirmedProof = state.CaptureProofSnapshot(
        "placement-proof", placementProofArmed: true, positionCaptured: true, legacyOwnershipInactive: true,
        candidateId: "custom:n4f4", candidateMatches: true,
        targetScene: "ex3/01_nvt_n4/fld/n4f4/level/n4f4");
    // 前提: raw な frozen 値は依然 timeout（runtime state は変更していない）。
    var rawStillTimeout = confirmedProof.LastPreLoginGateReason == "object-resolve-timeout"
        && confirmedProof.LastPreLoginReason == "object-resolve-timeout"
        && confirmedProof.ApplyCount == 1
        && confirmedProof.WriteConfirmed;

    var confirmedLines = TitleBackgroundCharaSelectPlacementDiagnostics.Build(
        confirmedProof,
        reportFromCompletedRunSnapshot: true,
        placementConfigEnabled: true,
        candidateId: "custom:n4f4",
        candidateMatches: true,
        targetScene: "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
        targetPosition: "0.000, 0.000, 0.000",
        targetRotation: "0.000",
        disposed: false).ToList();
    // 表示レイヤは confirmed run の最終状態を出す。first* は初期待機のまま。
    var confirmedOk = confirmedLines.Contains("automaticRun.lastPreLoginGateReason=ready")
        && confirmedLines.Contains("charaselect.placement.lastPreLoginReason=applied")
        && confirmedLines.Contains("automaticRun.firstPreLoginGateReason=object-resolve-timeout");

    // 未確定 run では raw な失敗ステージをそのまま出す（診断能力を落とさない）。
    var failedState = new TitleBackgroundCharaSelectPlacementRuntimeState();
    failedState.IncrementSceneGeneration();
    failedState.RecordGateEvaluation("object-resolve-timeout", preLogin: true);
    failedState.RecordSkip("object-resolve-timeout");
    failedState.MarkLoginStopped();
    var failedProof = failedState.CaptureProofSnapshot(
        "placement-proof", placementProofArmed: true, positionCaptured: false, legacyOwnershipInactive: true,
        candidateId: "custom:n4f4", candidateMatches: true,
        targetScene: "ex3/01_nvt_n4/fld/n4f4/level/n4f4");
    var failedLines = TitleBackgroundCharaSelectPlacementDiagnostics.Build(
        failedProof,
        reportFromCompletedRunSnapshot: true,
        placementConfigEnabled: true,
        candidateId: "custom:n4f4",
        candidateMatches: true,
        targetScene: "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
        targetPosition: "none",
        targetRotation: "none",
        disposed: false).ToList();
    var failedOk = failedLines.Contains("automaticRun.lastPreLoginGateReason=object-resolve-timeout")
        && failedLines.Contains("charaselect.placement.lastPreLoginReason=object-resolve-timeout");

    return rawStillTimeout && confirmedOk && failedOk;
});

Test(569, "REPORT SEMANTICS: confirmed new-engine placement lifts the legacy delivery verdict off working-background-only", () =>
{
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    // legacy path 前提（numeric 未検証）では従来どおり working-background-only。
    var legacy = Delivery(summary, lastOverrideApplied: true, transitionSafety: "safe");
    var legacyUnchanged = legacy.DeliveryVerdict == "working-background-only"
        && legacy.MvpStatus == "complete-background-only"
        && legacy.NextAction == "use-background-only";

    // 新 placement engine が今回 run で確定 → top-line verdict を整合させる。
    var newEngine = TitleBackgroundDeliveryDiagnostic.BuildSummary(
        TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly,
        TitleBackgroundCharacterSelectLightingMode.Default,
        string.Empty,
        "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
        816,
        51,
        sceneOverrideEnabled: true,
        lastOverrideApplied: true,
        "not-found",
        "unknown",
        "unknown",
        0,
        0,
        0,
        0,
        "none",
        [],
        "safe",
        charaSelectPlacementEngineConfirmed: true);

    var newEngineOk = newEngine.DeliveryVerdict == "working-character-composition"
        && newEngine.NextAction == "none"
        && newEngine.MvpStatus == "complete-character-composition";

    return legacyUnchanged && newEngineOk;
});

Test(558, "placement maintenance records a run-scoped trace at every stage so an unrun/gated/failed run is distinguishable", () =>
{
    var root = FindRepositoryRoot();
    var placement = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground",
        "TitleScreenBackgroundService.CharaSelectPlacement.cs"));
    var frameworkHooks = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground",
        "TitleScreenBackgroundService.NativeHooks.cs"));

    var body = ExtractMethodBody(placement, "private void MaintainTitleEditInformedCharaSelectPlacement(");

    // OnFrameworkUpdate から無条件に呼ばれる（"呼ばれていない" を切り分けられる）。
    var calledEveryFrame = frameworkHooks.Contains("MaintainTitleEditInformedCharaSelectPlacement();", StringComparison.Ordinal);

    // 最初の guard より前に RecordMaintainCall()。
    var maintainCallIdx = body.IndexOf("_charaSelectPlacement.RecordMaintainCall();", StringComparison.Ordinal);
    var firstGuardIdx = body.IndexOf("if (!IsCharaSelectPlacementActive)", StringComparison.Ordinal);
    var countsBeforeGuard = maintainCallIdx >= 0 && firstGuardIdx > maintainCallIdx;

    // proof armed で owner が placement でない場合は理由を残す（lastPreLoginReason=not-run を防ぐ）。
    var firstGuardLeavesReason = body.Contains("RecordOwnerNotPlacementWhileArmed()", StringComparison.Ordinal)
        && body.Contains("RecordSkip($\"engine-owner-{ownerName}\")", StringComparison.Ordinal);

    // gate 評価は毎回カウント + pre-login first/last 理由を保持。
    var gateEvalRecorded = body.Contains("_charaSelectPlacement.RecordGateEvaluation(", StringComparison.Ordinal);

    // 既に active な CharaSelect scene へ read-only attach（新 hook / scene reload なし）。
    var attachesToActiveScene = body.Contains("_charaSelectTitleBackgroundSessionActive = true;", StringComparison.Ordinal)
        && body.Contains("_activeCharaSelectSceneGeneration = runtimeSceneGeneration;", StringComparison.Ordinal)
        && body.Contains("charaselect placement attached to active scene", StringComparison.Ordinal)
        && !body.Contains("RequestCharaSelectReload", StringComparison.Ordinal);

    // pre-login framework frame も数える。
    var framesCounted = body.Contains("RecordPreLoginFrameworkFrame();", StringComparison.Ordinal);

    // OneClick は通常ログイン中に開始される。まだ logout を観測していない間の loggedIn Stop は
    // freeze せず "awaiting-logout" で待機する（run 開始時点のログインを terminal login と誤認しない）。
    var awaitingLogoutGuard = body.Contains("!_charaSelectPlacement.LogoutTransitionObserved", StringComparison.Ordinal)
        && body.Contains("RecordSkip(\"awaiting-logout\")", StringComparison.Ordinal)
        && body.IndexOf("RecordSkip(\"awaiting-logout\")", StringComparison.Ordinal)
            < body.IndexOf("MarkLoginStopped();", StringComparison.Ordinal);

    return calledEveryFrame && countsBeforeGuard && firstGuardLeavesReason
        && gateEvalRecorded && attachesToActiveScene && framesCounted && awaitingLogoutGuard;
});

Test(559, "placement runtime lifecycle counters record first/last pre-login gate reason, observe signals, and reset fully", () =>
{
    var state = new TitleBackgroundCharaSelectPlacementRuntimeState();

    state.RecordPreLoginFrameworkFrame();
    state.RecordPreLoginFrameworkFrame();
    state.RecordMaintainCall();
    state.RecordOwnerNotPlacementWhileArmed();
    state.RecordGateEvaluation("candidate-mismatch", preLogin: true);
    state.RecordGateEvaluation("no-source-backed-position", preLogin: true);
    state.ObserveLifecycle(
        charaSelectSessionActive: true, activeSceneGeneration: 7,
        quickCheckCollecting: true, logoutTransition: true, attachedToActiveScene: true);

    var recorded = state.PreLoginFrameworkFrameCount == 2
        && state.PlacementMaintainCallCount == 1
        && state.OwnerNotPlacementWhileArmedCount == 1
        && state.PlacementGateEvaluationCount == 2
        && state.FirstPreLoginGateReason == "candidate-mismatch"
        && state.LastPreLoginGateReason == "no-source-backed-position"
        && state.CharaSelectSessionObserved
        && state.SceneGenerationObserved == 7
        && state.QuickCheckCollectingObserved
        && state.LogoutTransitionObserved
        && state.AttachedToActiveScene;

    // login freeze 後は first/last pre-login gate reason を上書きしない。
    state.MarkLoginStopped();
    state.RecordGateEvaluation("logged-in", preLogin: false);
    var frozenAfterLogin = state.FirstPreLoginGateReason == "candidate-mismatch"
        && state.LastPreLoginGateReason == "no-source-backed-position"
        && state.PlacementGateEvaluationCount == 3;

    var snap = state.CaptureProofSnapshot("placement-proof", true, false, true);
    var inSnapshot = snap.PreLoginFrameworkFrameCount == 2
        && snap.PlacementMaintainCallCount == 1
        && snap.FirstPreLoginGateReason == "candidate-mismatch"
        && snap.LastPreLoginGateReason == "no-source-backed-position"
        && snap.OwnerNotPlacementWhileArmedCount == 1
        && snap.CharaSelectSessionObserved
        && snap.SceneGenerationObserved == 7;

    state.Reset();
    var afterReset = state.PreLoginFrameworkFrameCount == 0
        && state.PlacementMaintainCallCount == 0
        && state.PlacementGateEvaluationCount == 0
        && state.FirstPreLoginGateReason == "not-run"
        && state.LastPreLoginGateReason == "not-run"
        && !state.CharaSelectSessionObserved
        && state.SceneGenerationObserved == 0
        && !state.QuickCheckCollectingObserved
        && !state.LogoutTransitionObserved
        && state.OwnerNotPlacementWhileArmedCount == 0
        && !state.AttachedToActiveScene;

    return recorded && frozenAfterLogin && inSnapshot && afterReset;
});

Test(560, "lifecycle diagnostic keys flow through the single-source Build and the allow-list", () =>
{
    var root = FindRepositoryRoot();
    string Read(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", file));

    var diagStatic = Read("TitleBackgroundCharaSelectPlacementDiagnostics.cs");

    string[] lifecycleKeys =
    [
        "automaticRun.preLoginFrameworkFrameCount",
        "automaticRun.placementMaintainCallCount",
        "automaticRun.placementGateEvaluationCount",
        "automaticRun.firstPreLoginGateReason",
        "automaticRun.lastPreLoginGateReason",
        "automaticRun.charaSelectSessionObserved",
        "automaticRun.sceneGenerationObserved",
        "automaticRun.quickCheckCollectingObserved",
        "automaticRun.logoutTransitionObserved",
        "automaticRun.ownerNotPlacementWhileArmedCount",
        "automaticRun.attachedToActiveScene",
    ];
    foreach (var key in lifecycleKeys)
    {
        if (!diagStatic.Contains($"\"{key}\"", StringComparison.Ordinal)
            || !diagStatic.Contains($"$\"{key}=", StringComparison.Ordinal))
        {
            return false;
        }
    }

    return true;
});

Test(561, "placement path gates off an independent scene-generation counter, not the legacy camera adapter (which never arms for a new-engine owner)", () =>
{
    // runtime state: 独立カウンタは increment で進み、Reset で 0 に戻る（run 間リセット・run 内 monotonic）。
    var state = new TitleBackgroundCharaSelectPlacementRuntimeState();
    var startsZero = state.SceneGeneration == 0;
    state.IncrementSceneGeneration();
    state.IncrementSceneGeneration();
    var advanced = state.SceneGeneration == 2;
    state.RecordPlacementApplied(2, 0x100, new Vector3(1f, 0f, 1f), 0f, 5, "scene-generation");
    state.Reset();
    var resetToZero = state.SceneGeneration == 0 && state.LastAppliedSceneGeneration == 0;

    var root = FindRepositoryRoot();
    string Read(string file) => File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", file));
    var nativeHooks = Read("TitleScreenBackgroundService.NativeHooks.cs");
    var placement = Read("TitleScreenBackgroundService.CharaSelectPlacement.cs");

    // 増分は「実際の CharaSelect scene 差し替え」= CreateSceneDetour の override 適用ブランチだけ。
    var createSceneBody = ExtractMethodBody(nativeHooks, "private int CreateSceneDetour(");
    var incIndex = createSceneBody.IndexOf("_charaSelectPlacement.IncrementSceneGeneration();", StringComparison.Ordinal);
    var overrideMarkIndex = createSceneBody.IndexOf("_charaSelectTitleBackgroundSessionActive = true;", StringComparison.Ordinal);
    var originalCallIndex = createSceneBody.LastIndexOf("_hookLifecycle.CreateSceneHook?.Original(", StringComparison.Ordinal);
    var incrementInOverrideBranch = incIndex >= 0 && overrideMarkIndex >= 0 && incIndex > overrideMarkIndex
        && originalCallIndex >= 0 && incIndex > originalCallIndex
        // LoadLobbySceneDetour では増分しない（二重 increment 防止, phase0 §3）。
        && CountOccurrences(nativeHooks, "_charaSelectPlacement.IncrementSceneGeneration();") == 1;

    // maintain は独立カウンタを runtimeSceneGeneration として読み、それを canonical context へ載せる。
    // capture は同じ context の generation を消費し、legacy camera adapter の SceneGeneration は
    // 参照しない（新エンジン owner では adapter が Configure(enabled:true) されず 0 のまま）。
    var maintainBody = ExtractMethodBody(placement, "private void MaintainTitleEditInformedCharaSelectPlacement(");
    var captureBody = ExtractMethodBody(placement, "private void CaptureCharaSelectPlacementProof(");
    var readsIndependentCounter =
        maintainBody.Contains("var runtimeSceneGeneration = _charaSelectPlacement.SceneGeneration;", StringComparison.Ordinal)
        && captureBody.Contains("context.RuntimeSceneGeneration", StringComparison.Ordinal)
        && !maintainBody.Contains("_charaSelectCameraAdapter.RuntimeState.SceneGeneration", StringComparison.Ordinal)
        && !captureBody.Contains("_charaSelectCameraAdapter.RuntimeState.SceneGeneration", StringComparison.Ordinal);

    // mismatch gate は削除しない: 0 を valid 扱いせず、active>0 && runtime>0 && 等しいときだけ Apply。
    var mismatchGateIntact =
        TitleBackgroundCharaSelectPlacementLogic.ResolveGate(true, true, false, false, true, 0, 0, true, true, true, true).Decision
            == TitleBackgroundCharaSelectEngineDecision.Skip
        && TitleBackgroundCharaSelectPlacementLogic.ResolveGate(true, true, false, false, true, 2, 3, true, true, true, true).Decision
            == TitleBackgroundCharaSelectEngineDecision.Skip
        && TitleBackgroundCharaSelectPlacementLogic.ResolveGate(true, true, false, false, true, 2, 2, true, true, true, true).Decision
            == TitleBackgroundCharaSelectEngineDecision.Apply;

    return startsZero && advanced && resetToZero
        && incrementInOverrideBranch && readsIndependentCounter && mismatchGateIntact;
});

Test(562, "representative end-to-end placement sequence: arm -> pre-login wait -> scene generation -> resolve -> apply -> selection re-apply -> login freeze; abnormal inputs never write", () =>
{
    // §11 の代表正常系を pure decision/state で 1 本にたどる（native object の巨大 mock を作らない）。
    var state = new TitleBackgroundCharaSelectPlacementRuntimeState();

    TitleBackgroundCharaSelectEngineGate Gate(bool loggedIn, bool session, int activeGen, int runtimeGen, bool resolved) =>
        TitleBackgroundCharaSelectPlacementLogic.ResolveGate(
            placementPathActive: true, serviceReady: true, hookProbeMode: false,
            loggedIn: loggedIn, charaSelectSessionActive: session,
            activeSceneGeneration: activeGen, runtimeSceneGeneration: runtimeGen,
            isCharaSelectMap: true, candidateMatches: true,
            hasSourceBackedPosition: true, characterResolved: resolved);

    // 1) OneClick arm: run-scoped state を Reset。まだ logout 前（logged-in）。
    state.Reset();
    // 2) pre-login へ入った直後、CreateScene 前: session 未確定 -> Stop("session-inactive")（pre-login では skip 扱い）。
    var preScene = Gate(loggedIn: false, session: false, activeGen: 0, runtimeGen: 0, resolved: false);
    state.RecordGateEvaluation(preScene.Reason, preLogin: true);
    // 3) CreateScene override 成功: 独立カウンタ +1。次フレームで attach（active := runtime）。
    state.IncrementSceneGeneration();
    var runtimeGen = state.SceneGeneration; // 1
    var activeGen = runtimeGen;             // attach で同期
    // 4) resolver 成功（content-id -> mapping -> object）。
    state.RecordCharacterResolve(
        resolved: true, resolveSource: "SelectedCharacterIndex",
        entryAvailable: true, selectedContentAvailable: true, mappingAvailable: true,
        mappingHit: true, clientObjectIndexValid: true, objectResolved: true, drawReady: true,
        retryCount: 0, actorChanged: true);
    var applyGate = Gate(loggedIn: false, session: true, activeGen: activeGen, runtimeGen: runtimeGen, resolved: true);
    state.RecordGateEvaluation(applyGate.Reason, preLogin: true);
    // 5) capture 完了 -> 最初の書き込みトリガ。
    var writeOnCaptureComplete = TitleBackgroundPlacementTestShims.ShouldWrite(
        activeGen, state.LastAppliedSceneGeneration, 0x100, state.LastAppliedCharacterPtr(),
        captureJustCompleted: true, selectionChangePending: false);
    var trigger1 = TitleBackgroundPlacementTestShims.Trigger(
        activeGen, state.LastAppliedSceneGeneration, 0x100, state.LastAppliedCharacterPtr(), true, false);
    state.RecordPlacementApplied(activeGen, 0x100, new Vector3(10f, 0f, 5f), 0.5f, 30, trigger1.ToString());
    // 6) 同一 generation + 同一 actor + トリガ無し -> no-op（bounded）。
    var noReWrite = !TitleBackgroundPlacementTestShims.ShouldWrite(
        activeGen, state.LastAppliedSceneGeneration, 0x100, state.LastAppliedCharacterPtr(), false, false);
    // 7) 選択キャラ変更 -> 1 回だけ再適用。
    var writeOnSelectionChange = TitleBackgroundPlacementTestShims.ShouldWrite(
        activeGen, state.LastAppliedSceneGeneration, 0x200, state.LastAppliedCharacterPtr(),
        captureJustCompleted: false, selectionChangePending: true);
    state.RecordPlacementApplied(activeGen, 0x200, new Vector3(10f, 0f, 5f), 0.5f, 60, "SelectionChange");
    var appliedTwice = state.PlacementApplyCount == 2;
    // 8) login: freeze -> snapshot -> stop。
    var loginGate = Gate(loggedIn: true, session: true, activeGen: activeGen, runtimeGen: runtimeGen, resolved: true);
    state.MarkLoginStopped();
    var snap = state.CaptureProofSnapshot("placement-proof", placementProofArmed: true, positionCaptured: true, legacyOwnershipInactive: true);

    var normalPath = preScene.Decision == TitleBackgroundCharaSelectEngineDecision.Stop
        && preScene.Reason == "session-inactive"
        && applyGate.Decision == TitleBackgroundCharaSelectEngineDecision.Apply
        && writeOnCaptureComplete && trigger1 == TitleBackgroundCharaSelectPlacementTrigger.CaptureComplete
        && noReWrite && writeOnSelectionChange && appliedTwice
        && loginGate.Decision == TitleBackgroundCharaSelectEngineDecision.Stop && loginGate.Reason == "logged-in"
        && snap.ApplyCount == 2 && snap.LoginStopped && snap.PreLoginPlacementObserved
        && snap.LegacyOwnershipInactive
        && snap.FirstPreLoginGateReason == "session-inactive"
        && snap.LastPreLoginGateReason == "ready"
        // run 内では monotonic（Reset は completion finally 側で、この sequence では未到達）。
        && state.SceneGeneration == 1;

    // 重要異常系: generation mismatch / unresolved actor / non-finite capture では書かない。
    var genMismatchNoWrite = Gate(loggedIn: false, session: true, activeGen: 2, runtimeGen: 3, resolved: true).Decision
        == TitleBackgroundCharaSelectEngineDecision.Skip;
    var unresolvedNoWrite = Gate(loggedIn: false, session: true, activeGen: 1, runtimeGen: 1, resolved: false).Decision
        == TitleBackgroundCharaSelectEngineDecision.Skip;
    var nonFiniteCaptureRejected = TitleBackgroundCharaSelectPlacementLogic.EvaluateCaptureValidity(
        mappingHit: true, objectResolved: true, drawReady: true,
        activeSceneGeneration: 1, runtimeSceneGeneration: 1,
        position: new Vector3(float.NaN, 0f, 0f), rotation: 0f) == "non-finite-transform";
    var nullActorNoWrite = !TitleBackgroundPlacementTestShims.ShouldWrite(
        2, 1, 0UL, 0x100, captureJustCompleted: true, selectionChangePending: true);

    return normalPath && genMismatchNoWrite && unresolvedNoWrite && nonFiniteCaptureRejected && nullActorNoWrite;
});

Test(563, "login-frame resolver result never clobbers the frozen pre-login resolver diagnostics", () =>
{
    // Fix: MaintainTitleEditInformedCharaSelectPlacement は RecordCharacterResolve を pre-login のみ呼ぶ。
    // login フレームでは TryResolveSelectedCharacterActor が login ガードで false を返すため、
    // ここで記録すると直前の正しい pre-login resolve 結果を None で潰し、直後の
    // MarkLoginStopped -> CaptureCompletedRunProofSnapshot がその誤値を freeze してしまう。
    var root = FindRepositoryRoot();
    var placement = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground",
        "TitleScreenBackgroundService.CharaSelectPlacement.cs"));
    var maintainBody = ExtractMethodBody(placement, "private void MaintainTitleEditInformedCharaSelectPlacement(");

    var recordIdx = maintainBody.IndexOf("_charaSelectPlacement.RecordCharacterResolve(", StringComparison.Ordinal);
    var guardIdx = maintainBody.IndexOf("if (preLogin)", StringComparison.Ordinal);
    var gateIdx = maintainBody.IndexOf("TitleBackgroundCharaSelectPlacementLogic.ResolveGate(", StringComparison.Ordinal);
    // guard は record より前、record は gate 評価より前（gate で MarkLoginStopped する前に潰さない）。
    var sourceGuarded = guardIdx >= 0 && recordIdx > guardIdx && gateIdx > recordIdx
        // 単一の RecordCharacterResolve 呼び出しのみ（別経路で無ガード呼び出しを足さない）。
        && CountOccurrences(maintainBody, "_charaSelectPlacement.RecordCharacterResolve(") == 1
        && maintainBody.Contains("actor.IdentityKey != _charaSelectPlacement.LastAppliedActorKey", StringComparison.Ordinal);

    // runtime state 側でも login freeze 後の record は無視される（二重防御）。
    var state = new TitleBackgroundCharaSelectPlacementRuntimeState();
    state.RecordCharacterResolve(
        resolved: true, resolveSource: "SelectedCharacterIndex",
        entryAvailable: true, selectedContentAvailable: true, mappingAvailable: true,
        mappingHit: true, clientObjectIndexValid: true, objectResolved: true, drawReady: true,
        retryCount: 0, actorChanged: false);
    state.MarkLoginStopped();
    state.RecordCharacterResolve(
        resolved: false, resolveSource: "None",
        entryAvailable: false, selectedContentAvailable: false, mappingAvailable: false,
        mappingHit: false, clientObjectIndexValid: false, objectResolved: false, drawReady: false,
        retryCount: 0, actorChanged: true);
    var frozen = state.LastResolveSource == "SelectedCharacterIndex"
        && state.LastMappingHit
        && state.LastObjectResolved
        && state.CaptureProofSnapshot("placement-proof", true, true, true).ResolveSource == "SelectedCharacterIndex";

    return sourceGuarded && frozen;
});

Test(564, "proof completion promotes only a stable applied run and reuses the persistent production owner", () =>
{
    var state = new TitleBackgroundCharaSelectPlacementRuntimeState();
    state.IncrementSceneGeneration();
    var generation = state.SceneGeneration;
    state.ObserveLifecycle(
        charaSelectSessionActive: true,
        activeSceneGeneration: generation,
        quickCheckCollecting: true,
        logoutTransition: true,
        attachedToActiveScene: true);
    state.RecordCharacterResolve(
        resolved: true,
        resolveSource: "SelectedCharacterIndex",
        entryAvailable: true,
        selectedContentAvailable: true,
        mappingAvailable: true,
        mappingHit: true,
        clientObjectIndexValid: true,
        objectResolved: true,
        drawReady: false,
        retryCount: 0,
        actorChanged: true);
    state.RecordCapturePersisted(
        stableSamples: 5,
        zeroAccepted: true,
        candidateId: "custom:n4f4",
        position: Vector3.Zero,
        rotation: 0f);
    state.RecordPlacementWriteAttempt(
        generation,
        0x100,
        setterCallCompleted: true,
        positionReadbackConfirmed: true,
        rotationReadbackConfirmed: true,
        status: "confirmed",
        clientObjectIndex: 7);
    state.RecordPlacementApplied(
        generation,
        0x100,
        Vector3.Zero,
        0f,
        frame: 12,
        trigger: "CaptureComplete",
        clientObjectIndex: 7);
    state.MarkLoginStopped();

    var proof = state.CaptureProofSnapshot(
        "placement-proof",
        placementProofArmed: true,
        positionCaptured: true,
        legacyOwnershipInactive: true,
        candidateId: "custom:n4f4",
        candidateMatches: true,
        targetScene: "ex3/01_nvt_n4/fld/n4f4/level/n4f4");
    var promotion = TitleBackgroundCharaSelectPlacementLogic.EvaluatePromotion(
        proof,
        partial: false,
        quickCheckLevel: TitleBackgroundQuickCheckLevel.OK,
        candidateMatches: true,
        targetFinite: true,
        postLoginLeakStatus: "none");

    var quickCheck = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        runScoped: true,
        isLoggedIn: true,
        startedLoggedIn: true,
        runState: TitleBackgroundQuickCheckRunState.LoggedInObserved) with
    {
        CharaSelectPlacementProof = proof,
    });

    // promotion 後の通常 run は同じ ResolveGate / placement owner を使う。
    var persistentOwner = TitleBackgroundCharaSelectEngineOwnerLogic.Resolve(
        overrideEnabled: true,
        automaticPlacementProofArmed: false,
        persistentPlacementEnabled: true,
        v2Enabled: false);
    var persistentGate = TitleBackgroundCharaSelectPlacementLogic.ResolveGate(
        placementPathActive: true,
        serviceReady: true,
        hookProbeMode: false,
        loggedIn: false,
        charaSelectSessionActive: true,
        activeSceneGeneration: generation,
        runtimeSceneGeneration: generation,
        isCharaSelectMap: true,
        candidateMatches: true,
        hasSourceBackedPosition: true,
        characterResolved: true);
    var persistentQuickCheck = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        runScoped: true,
        isLoggedIn: true,
        runState: TitleBackgroundQuickCheckRunState.LoggedInObserved) with
    {
        PersistentCharaSelectPlacementActive = true,
        PersistentCharaSelectPlacementApplied = true,
        PersistentCharaSelectPlacementReadbackConfirmed = true,
    });
    var persistentQuickCheckReadbackFailure = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        runScoped: true,
        isLoggedIn: true,
        runState: TitleBackgroundQuickCheckRunState.LoggedInObserved) with
    {
        PersistentCharaSelectPlacementActive = true,
        PersistentCharaSelectPlacementApplied = true,
        PersistentCharaSelectPlacementReadbackConfirmed = false,
    });

    var failed = new TitleBackgroundCharaSelectPlacementRuntimeState();
    failed.IncrementSceneGeneration();
    failed.ObserveLifecycle(
        charaSelectSessionActive: true,
        activeSceneGeneration: failed.SceneGeneration,
        quickCheckCollecting: true,
        logoutTransition: true,
        attachedToActiveScene: true);
    failed.RecordCharacterResolve(
        resolved: true,
        resolveSource: "SelectedCharacterIndex",
        entryAvailable: true,
        selectedContentAvailable: true,
        mappingAvailable: true,
        mappingHit: true,
        clientObjectIndexValid: true,
        objectResolved: true,
        drawReady: false,
        retryCount: 0,
        actorChanged: false);
    failed.MarkLoginStopped();
    var failedProof = failed.CaptureProofSnapshot(
        "placement-proof",
        placementProofArmed: true,
        positionCaptured: false,
        legacyOwnershipInactive: true,
        candidateId: "custom:n4f4",
        candidateMatches: true,
        targetScene: "ex3/01_nvt_n4/fld/n4f4/level/n4f4");
    var failedPromotion = TitleBackgroundCharaSelectPlacementLogic.EvaluatePromotion(
        failedProof,
        partial: false,
        quickCheckLevel: TitleBackgroundQuickCheckLevel.OK,
        candidateMatches: true,
        targetFinite: true,
        postLoginLeakStatus: "none");

    var root = FindRepositoryRoot();
    var quickCheckSource = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleScreenBackgroundService.QuickCheck.cs"));
    var completionBody = ExtractMethodBody(quickCheckSource, "private void CompleteAutomaticQuickCheck(bool partial)");
    var restoreBody = ExtractMethodBody(quickCheckSource, "private AutomaticCheckRestoreResult RestoreAutomaticCheckSettingsOnce(");
    var promotionBody = ExtractMethodBody(
        quickCheckSource,
        "private TitleBackgroundRunCharaSelectPlacementPersistenceCandidate? ResolveRunCharaSelectPlacementPersistenceCandidate(");
    var callbackOrder = completionBody.IndexOf("afterRestoreBeforeReload:", StringComparison.Ordinal)
        < completionBody.IndexOf("FinalizeAutomaticCheckReport(", StringComparison.Ordinal);
    var restoreCallbackIsPostRestore = restoreBody.IndexOf("afterRestoreBeforeReload();", StringComparison.Ordinal)
        > restoreBody.IndexOf("_automaticCheck.SettingsRestored = true;", StringComparison.Ordinal)
        && restoreBody.IndexOf("afterRestoreBeforeReload();", StringComparison.Ordinal)
            < restoreBody.IndexOf("ReloadNativeIntegration();", StringComparison.Ordinal);
    var failedRunsReturnNoCandidate = promotionBody.Contains("if (!decision.Eligible)", StringComparison.Ordinal)
        && promotionBody.Contains("return null;", StringComparison.Ordinal);

    return promotion.Eligible
        && proof.CaptureStableSamples == 5
        && proof.WriteConfirmed
        && proof.LoginStopped
        && quickCheck.Level == TitleBackgroundQuickCheckLevel.OK
        && quickCheck.Reason.Contains("placement proof", StringComparison.OrdinalIgnoreCase)
        && persistentOwner == TitleBackgroundCharaSelectEngineOwner.Placement
        && persistentGate.Decision == TitleBackgroundCharaSelectEngineDecision.Apply
        && persistentQuickCheck.Level == TitleBackgroundQuickCheckLevel.OK
        && persistentQuickCheck.Reason.Contains("persistent Character Select placement", StringComparison.Ordinal)
        && persistentQuickCheckReadbackFailure.Level == TitleBackgroundQuickCheckLevel.NG
        && persistentQuickCheckReadbackFailure.Reason.Contains("readback", StringComparison.OrdinalIgnoreCase)
        && !failedPromotion.Eligible
        && failedPromotion.Reason == "stable-capture-not-complete"
        && callbackOrder
        && restoreCallbackIsPostRestore
        && failedRunsReturnNoCandidate;
});

Test(565, "capture identity and write retry reset on mapping index changes while keeping writes bounded", () =>
{
    var state = new TitleBackgroundCharaSelectPlacementRuntimeState();
    state.RecordCaptureSample(1, new Vector3(1f, 2f, 3f), 0.5f, framesElapsed: 1);
    state.RecordCaptureSampleIdentity(0x100, 123UL, 7, 2);
    var sameIdentity = state.CaptureIdentityMatches(0x100, 123UL, 7, 2);
    var changedClientObject = !state.CaptureIdentityMatches(0x100, 123UL, 8, 2);
    var changedGeneration = !state.CaptureIdentityMatches(0x100, 123UL, 7, 3);

    for (var i = 0; i < TitleBackgroundCharaSelectPlacementLogic.PlacementWriteRetryBudget; i++)
    {
        state.RecordPlacementWriteAttempt(
            2,
            0x100,
            setterCallCompleted: true,
            positionReadbackConfirmed: false,
            rotationReadbackConfirmed: false,
            status: "position-readback-mismatch",
            clientObjectIndex: 7);
    }

    var exhausted = !state.CanAttemptPlacementWrite(2, 0x100, 7);
    var changedMappingAllowsRetry = state.CanAttemptPlacementWrite(2, 0x100, 8);
    var changedContentAllowsRetry = state.CanAttemptPlacementWrite(2, 0x100, 7, 123UL);

    var root = FindRepositoryRoot();
    var placement = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleScreenBackgroundService.CharaSelectPlacement.cs"));
    var modelBody = ExtractMethodBody(placement, "private TitleBackgroundCharaSelectLocationModel BuildCurrentCharaSelectLocationModel(");

    return sameIdentity
        && changedClientObject
        && changedGeneration
        && exhausted
        && changedMappingAllowsRetry
        && changedContentAllowsRetry
        && modelBody.Contains("var proofRun = _automaticCheck.PlacementProofArmed", StringComparison.Ordinal)
        && modelBody.Contains("var positionCaptured = proofRun", StringComparison.Ordinal)
        && modelBody.Contains(": _configuration.TitleBackgroundCharaSelectPlacementPositionCaptured", StringComparison.Ordinal);
});

Test(566, "FRU clear-stage candidate registers verified constants and a user-approved static anchor", () =>
{
    var found = TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
        out var fru);

    return found
        && fru.Id == "custom:fru-clear-stage"
        && fru.DisplayName == "FRU クリア後ステージ"
        && fru.TerritoryPath == "ex3/01_nvt_n4/goe/n4gw/level/n4gw"
        && fru.TerritoryId == 1238u
        && fru.LayerFilterKey == 0u
        // real-game OneClick passed -> promoted to verified.
        && fru.VerifiedInGame
        && fru.KnownIssue == "none"
        && fru.RecommendedAction == "none"
        && !fru.RequiresSourceBackedLayout
        && fru.ApprovedStaticAnchor.HasValue
        && fru.ApprovedStaticAnchor.Value == new Vector3(100f, 0f, 100f)
        && fru.StaticAnchorProvenance.Length > 0
        // approved-static-anchor 候補は LayerFilterKey=0 でも candidate-fields-valid を通る。
        && TitleBackgroundCharaSelectSourceLayoutLogic.IsCandidateFieldsValid(fru, fru.TerritoryPath, fru.TerritoryId, fru.TerritoryId, 0u)
        && TitleBackgroundCharaSelectStaticAnchorLogic.ResolvePlacementSourceMode(fru) == "approved-static-anchor";
});

Test(567, "FRU static anchor authorizes only when scene + loaded layout identity all match", () =>
{
    TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
        out var fru);

    var ok = TitleBackgroundCharaSelectStaticAnchorLogic.Evaluate(
        fru,
        preLogin: true,
        charaSelectMap: true,
        sceneOverrideApplied: true,
        appliedScenePath: "ex3/01_nvt_n4/goe/n4gw/level/n4gw",
        appliedTerritoryTypeId: 1238u,
        appliedLayerFilterKey: 0u,
        activeLayoutAvailable: true,
        layoutInitState: 7,
        loadedLayoutTerritoryTypeId: 1238u,
        loadedLayerFilterKey: 0u,
        sceneGeneration: 4);

    var rs = new TitleBackgroundCharaSelectStaticAnchorRuntimeState();
    rs.Capture(ok);
    var anchorReturned = rs.TryGetAuthorizedAnchor("custom:fru-clear-stage", out var anchor)
        && anchor == new Vector3(100f, 0f, 100f);
    var wrongCandidateRejected = !rs.TryGetAuthorizedAnchor("custom:n4f4", out _);

    return ok.Authorized
        && ok.AuthorizationReason == "authorized"
        && ok.Anchor == new Vector3(100f, 0f, 100f)
        && ok.LayoutReady
        && anchorReturned
        && wrongCandidateRejected
        && rs.HasSnapshot;
});

Test(568, "FRU static anchor fails closed on missing override, stale layout, or ambiguous identity", () =>
{
    TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
        out var fru);

    static TitleBackgroundCharaSelectStaticAnchorSnapshot Eval(
        TitleBackgroundCharacterSelectOverrideCandidate c,
        bool sceneOverrideApplied,
        int layoutInitState,
        uint loadedLayoutTerritoryTypeId,
        int sceneGeneration)
        => TitleBackgroundCharaSelectStaticAnchorLogic.Evaluate(
            c,
            preLogin: true,
            charaSelectMap: true,
            sceneOverrideApplied: sceneOverrideApplied,
            appliedScenePath: "ex3/01_nvt_n4/goe/n4gw/level/n4gw",
            appliedTerritoryTypeId: 1238u,
            appliedLayerFilterKey: 0u,
            activeLayoutAvailable: layoutInitState >= 0,
            layoutInitState: layoutInitState,
            loadedLayoutTerritoryTypeId: loadedLayoutTerritoryTypeId,
            loadedLayerFilterKey: 0u,
            sceneGeneration: sceneGeneration);

    var noOverride = Eval(fru, false, 7, 1238u, 4);
    var layoutNotReady = Eval(fru, true, 3, 1238u, 4);
    var layoutTerritoryMismatch = Eval(fru, true, 7, 999u, 4);
    var noSceneGeneration = Eval(fru, true, 7, 1238u, 0);
    var postLogin = TitleBackgroundCharaSelectStaticAnchorLogic.Evaluate(
        fru, preLogin: false, charaSelectMap: true, sceneOverrideApplied: true,
        appliedScenePath: "ex3/01_nvt_n4/goe/n4gw/level/n4gw", appliedTerritoryTypeId: 1238u,
        appliedLayerFilterKey: 0u, activeLayoutAvailable: true, layoutInitState: 7,
        loadedLayoutTerritoryTypeId: 1238u, loadedLayerFilterKey: 0u, sceneGeneration: 4);

    var rs = new TitleBackgroundCharaSelectStaticAnchorRuntimeState();
    rs.Capture(layoutTerritoryMismatch);
    var noAnchorWhenUnauthorized = !rs.TryGetAuthorizedAnchor("custom:fru-clear-stage", out _);
    rs.Reset();

    return !noOverride.Authorized && noOverride.AuthorizationReason == "scene-override-not-applied"
        && !layoutNotReady.Authorized && layoutNotReady.AuthorizationReason == "active-layout-not-ready"
        && !layoutTerritoryMismatch.Authorized && layoutTerritoryMismatch.AuthorizationReason == "loaded-layout-territory-mismatch"
        && !noSceneGeneration.Authorized && noSceneGeneration.AuthorizationReason == "scene-generation-not-observed"
        && !postLogin.Authorized && postLogin.AuthorizationReason == "not-pre-login"
        && noAnchorWhenUnauthorized
        && !rs.HasSnapshot;
});

Test(569, "placement source mode distinguishes approved static anchor from source-backed and run-scoped", () =>
{
    TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet("custom:fru-clear-stage", out var fru);
    TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet("custom:ultima-thule-elpis", out var elpis);
    TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet("custom:n4f4", out var ilMheg);

    return TitleBackgroundCharaSelectStaticAnchorLogic.ResolvePlacementSourceMode(fru) == "approved-static-anchor"
        && TitleBackgroundCharaSelectStaticAnchorLogic.ResolvePlacementSourceMode(elpis) == "source-backed-same-terrain"
        && TitleBackgroundCharaSelectStaticAnchorLogic.ResolvePlacementSourceMode(ilMheg) == "run-scoped-capture";
});

Test(570, "FRU explicit LayerFilterKey=0 requires strict applied/loaded layer match, not just territory", () =>
{
    TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
        out var fru);

    static TitleBackgroundCharaSelectStaticAnchorSnapshot Eval(
        TitleBackgroundCharacterSelectOverrideCandidate c,
        uint appliedLayerFilterKey,
        uint loadedLayerFilterKey)
        => TitleBackgroundCharaSelectStaticAnchorLogic.Evaluate(
            c,
            preLogin: true,
            charaSelectMap: true,
            sceneOverrideApplied: true,
            appliedScenePath: "ex3/01_nvt_n4/goe/n4gw/level/n4gw",
            appliedTerritoryTypeId: 1238u,
            appliedLayerFilterKey: appliedLayerFilterKey,
            activeLayoutAvailable: true,
            layoutInitState: 7,
            loadedLayoutTerritoryTypeId: 1238u,
            loadedLayerFilterKey: loadedLayerFilterKey,
            sceneGeneration: 4);

    var strictZeroOk = Eval(fru, 0u, 0u);
    var appliedLayerNonZero = Eval(fru, 51u, 0u);
    var loadedLayerNonZero = Eval(fru, 0u, 51u);

    return fru.LayerFilterKeyExplicit
        && strictZeroOk.Authorized
        && !appliedLayerNonZero.Authorized
        && appliedLayerNonZero.AuthorizationReason == "applied-layer-filter-mismatch"
        && !loadedLayerNonZero.Authorized
        && loadedLayerNonZero.AuthorizationReason == "loaded-layer-filter-mismatch"
        // 非明示・expected==0 の legacy 判定は territory 一致で loaded layer を問わない。
        && TitleBackgroundCharaSelectStaticAnchorLogic.IsLayerFilterConsistent(0u, 51u, true, false)
        && !TitleBackgroundCharaSelectStaticAnchorLogic.IsLayerFilterConsistent(0u, 51u, true, true);
});

Test(571, "approved static anchor model never falls back to persisted config Position", () =>
{
    var body = ReadServiceMethodBody(
        "TitleScreenBackgroundService.CharaSelectPlacement.cs",
        "private TitleBackgroundCharaSelectLocationModel BuildCurrentCharaSelectLocationModel(");

    var anchorBranchIndex = body.IndexOf("activeCandidate.ApprovedStaticAnchor.HasValue", StringComparison.Ordinal);
    var anchorReturnIndex = body.IndexOf("_charaSelectStaticAnchor.TryGetAuthorizedAnchor(", StringComparison.Ordinal);
    var configFallbackIndex = body.IndexOf("_configuration.TitleBackgroundCharaSelectPlacementPositionX", StringComparison.Ordinal);

    return anchorBranchIndex >= 0
        && anchorReturnIndex > anchorBranchIndex
        // anchor 分岐は早期 return し、config Position fallback より前で完結する。
        && configFallbackIndex > anchorReturnIndex
        && body.IndexOf("return TitleBackgroundCharaSelectPlacementLogic.BuildLocationModel(", anchorReturnIndex, StringComparison.Ordinal)
            < configFallbackIndex
        && body.Contains("anchorReady = anchorAuthorized", StringComparison.Ordinal);
});

Test(572, "completed-run proof snapshot uses the approved anchor as target, not the pre-write character position", () =>
{
    var body = ReadServiceMethodBody(
        "TitleScreenBackgroundService.QuickCheck.cs",
        "private void CaptureCompletedRunProofSnapshot(string reason)");

    var anchorAuthIndex = body.IndexOf("_charaSelectStaticAnchor.TryGetAuthorizedAnchor(", StringComparison.Ordinal);
    var targetPositionIndex = body.IndexOf("var targetPosition =", StringComparison.Ordinal);
    var capturedPositionIndex = body.IndexOf("_charaSelectPlacement.CapturedPosition", targetPositionIndex, StringComparison.Ordinal);

    return anchorAuthIndex >= 0
        // anchor candidate の positionCaptured は「authorized かつ stable capture 完了」で決まる。
        && body.Contains("positionCaptured = activeCandidate.ApprovedStaticAnchor.HasValue", StringComparison.Ordinal)
        && body.Contains("anchorAuthorized && _charaSelectPlacement.CaptureCompleted", StringComparison.Ordinal)
        // anchor 分岐は authorized anchor か default のみ。CapturedPosition への分岐はその後（非 anchor 用）。
        && body.Contains("anchorAuthorized ? approvedAnchor : default", StringComparison.Ordinal)
        && capturedPositionIndex > anchorAuthIndex
        && body.IndexOf("activeCandidate.ApprovedStaticAnchor.HasValue", targetPositionIndex, StringComparison.Ordinal)
            < capturedPositionIndex
        // rotation は canonical stable capture 由来のまま。
        && body.Contains("targetRotation = _charaSelectPlacement.CaptureCompleted", StringComparison.Ordinal);
});

Test(573, "FRU environment time policy uses the 13:00 clock time; other candidates keep noon", () =>
{
    var fru = TitleBackgroundEnvironmentTimePolicy.ResolveDayTimeSeconds("custom:fru-clear-stage");
    var ilMheg = TitleBackgroundEnvironmentTimePolicy.ResolveDayTimeSeconds("custom:n4f4");
    var elpis = TitleBackgroundEnvironmentTimePolicy.ResolveDayTimeSeconds("custom:ultima-thule-elpis");

    return fru == (13f * 3600f)
        && fru == 46800f
        && ilMheg == TitleBackgroundEnvironmentNoonWriter.NoonDayTimeSeconds
        && elpis == TitleBackgroundEnvironmentNoonWriter.NoonDayTimeSeconds
        && ilMheg == 43200f
        && TitleBackgroundEnvironmentTimePolicy.ResolvePolicyName("custom:fru-clear-stage") == "fru-clock-13-00"
        && TitleBackgroundEnvironmentTimePolicy.ResolvePolicyName("custom:n4f4") == "noon"
        // FRU weather policy is unchanged: still Clear Skies (id 1).
        && TitleBackgroundEnvironmentWeatherPolicy.ResolveRequestedWeatherId("custom:fru-clear-stage")
            == TitleBackgroundEnvironmentClearSkyWriter.ClearSkiesWeatherId;
});

Test(574, "FRU scene-object suppression matches fight gimmick SharedGroup tokens and never the clear-stage keep set", () =>
{
    static TitleBackgroundSceneObjectSuppressionVerdict V(string path, bool isSharedGroup)
        => TitleBackgroundCharaSelectSceneObjectSuppressionLogic.Evaluate(path, isSharedGroup).Verdict;

    return
        // fight gimmick / magic circle / ice / destruction shared groups -> suppress
        V("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a2_gmc01.sgb", true) == TitleBackgroundSceneObjectSuppressionVerdict.Suppress
        && V("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a6_mag00.sgb", true) == TitleBackgroundSceneObjectSuppressionVerdict.Suppress
        && V("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a3_ice01.sgb", true) == TitleBackgroundSceneObjectSuppressionVerdict.Suppress
        && V("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a6_dst00.sgb", true) == TitleBackgroundSceneObjectSuppressionVerdict.Suppress
        // LVD telegraph + n4gw fight VFX shared groups -> suppress
        && V("bgcommon/world/lvd/shared/for_vfx/sgvf_w_lvd_b1844.sgb", true) == TitleBackgroundSceneObjectSuppressionVerdict.Suppress
        && V("bg/ex3/01_nvt_n4/shared/for_vfx/sgvf_n4gw_b3558.sgb", true) == TitleBackgroundSceneObjectSuppressionVerdict.Suppress
        && V("bg/ex3/01_nvt_n4/shared/for_vfx/sgvf_n4gw_b3561.sgb", true) == TitleBackgroundSceneObjectSuppressionVerdict.Suppress
        // clear-stage keep set: flower field / floor / vista trees / lighting -> keep
        && V("bg/ex3/01_nvt_n4/goe/n4gw/bgparts/n4gw_a8_plt01.mdl", true) == TitleBackgroundSceneObjectSuppressionVerdict.Keep
        && V("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a6_flo00.sgb", true) == TitleBackgroundSceneObjectSuppressionVerdict.Keep
        && V("bg/ex3/01_nvt_n4/goe/n4gw/bgparts/n4gw_a8_tree1.mdl", true) == TitleBackgroundSceneObjectSuppressionVerdict.Keep
        && V("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a1_lig02.sgb", true) == TitleBackgroundSceneObjectSuppressionVerdict.Keep
        // raw VFX-type avfx / non SharedGroup / empty path -> skip (VFX type is not touched; semantics unverified)
        && V("bg/ex3/01_nvt_n4/common/vfx/eff/n4gw01dmg1_y1.avfx", false) == TitleBackgroundSceneObjectSuppressionVerdict.Skip
        && V("bg/ex3/01_nvt_n4/goe/n4gw/bgparts/n4gw_a1_stg01.mdl", false) == TitleBackgroundSceneObjectSuppressionVerdict.Skip
        && V("", true) == TitleBackgroundSceneObjectSuppressionVerdict.Skip;
});

Test(575, "FRU suppression window needs a stable streak, never stables on 0 match, and is bounded", () =>
{
    const int streak = TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState.StableStreakTarget;
    const int budget = TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState.WriteBudgetPerInstance;
    var keyA = ((ulong)10865420u << 32) | 0u;

    // --- stable only after N consecutive passes where EVERY matched instance was already
    //     inactive at pass start (no write, no still-active, no exception this pass) ---
    var s = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s.ArmForGeneration(4);
    // a pass that WRITES (even with readback success) does not count as clean.
    s.BeginPass();
    s.RecordScanned();
    s.RecordMatched(keyA);
    s.TryConsumeWriteBudget(keyA);
    s.RecordWriteAttempted(keyA, "SharedGroup");
    s.RecordConfirmedInactive(keyA);
    s.EndPass();
    var writePassNotClean = s.StableStreak == 0;

    var completedBeforeStreak = false;
    for (var i = 0; i < streak; i++)
    {
        s.BeginPass();
        s.RecordScanned();
        s.RecordMatched(keyA);
        s.RecordAlreadyInactive(keyA);
        s.EndPass();
        if (i < streak - 1 && s.Completed)
        {
            completedBeforeStreak = true;
        }
    }
    var stabledAfterStreak = s.Completed && s.StopReason == "stable" && !s.ShouldRunPass();

    // a still-active pass mid-streak resets the streak on a live window.
    var s2 = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s2.ArmForGeneration(7);
    for (var i = 0; i < streak - 1; i++)
    {
        s2.BeginPass();
        s2.RecordScanned();
        s2.RecordMatched(keyA);
        s2.RecordAlreadyInactive(keyA);
        s2.EndPass();
    }
    var s2NearStable = !s2.Completed && s2.StableStreak == streak - 1;
    s2.BeginPass();
    s2.RecordScanned();
    s2.RecordMatched(keyA);
    s2.TryConsumeWriteBudget(keyA);
    s2.RecordWriteAttempted(keyA, "SharedGroup");
    s2.RecordStillActive(keyA); // game re-activated it
    s2.EndPass();
    var s2StreakReset = !s2.Completed && s2.StableStreak == 0 && s2.StillActiveCount > 0;

    // --- 0 match never stables; closes as no-matched-instances after the grace period ---
    var s3 = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s3.ArmForGeneration(9);
    for (var i = 0; i < TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState.NoMatchGracePasses; i++)
    {
        s3.BeginPass();
        s3.RecordScanned();
        s3.EndPass();
    }
    var zeroMatchNoStable = s3.Completed && s3.StopReason == "no-matched-instances" && !s3.EverMatched;

    // --- per-instance write budget is bounded on a fresh window ---
    var s4 = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s4.ArmForGeneration(11);
    for (var i = 0; i < budget + 5; i++)
    {
        s4.TryConsumeWriteBudget(keyA);
    }
    var budgetBounded = s4.TotalWriteCalls == budget && !s4.TryConsumeWriteBudget(keyA);

    // --- new generation re-arms and clears generation-scoped failure/gate ---
    var s5 = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s5.ArmForGeneration(20);
    s5.RecordFailure("write-window-budget-exhausted");
    s5.RecordGateStatus("active-layout-not-ready");
    s5.ArmForGeneration(21);
    var genScopedReset = !s5.Completed
        && s5.ArmedSceneGeneration == 21
        && s5.TotalWriteCalls == 0
        && s5.FirstFailureReason == "none";

    // --- character switch: forceReArm rebuilds a fresh window on the SAME generation ---
    var s6 = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s6.ArmForGeneration(30);
    for (var i = 0; i < streak; i++)
    {
        s6.BeginPass();
        s6.RecordScanned();
        s6.RecordMatched(keyA);
        s6.RecordAlreadyInactive(keyA);
        s6.EndPass();
    }
    var s6ClosedFirst = s6.Completed;
    s6.ArmForGeneration(30); // same generation, no force -> stays closed
    var s6StillClosed = s6.Completed;
    s6.ArmForGeneration(30, forceReArm: true); // character switch -> fresh window on the same generation
    var s6ReArmed = !s6.Completed && s6.ShouldRunPass() && s6.StableStreak == 0 && s6.PassCount == 0;

    var lines = s.BuildDiagnosticLines("custom:fru-clear-stage", true).ToArray();
    var diagOk = lines.Contains("fru.suppression.vfxMode=excluded-semantics-unverified")
        && lines.Any(l => l.StartsWith("fru.suppression.stableStreak=", StringComparison.Ordinal))
        && lines.Any(l => l.StartsWith("fru.suppression.lastGateStatus=", StringComparison.Ordinal))
        && lines.Any(l => l.StartsWith("fru.suppression.stillActiveCount=", StringComparison.Ordinal));

    s.Reset();
    var afterReset = s.BuildDiagnosticLines("custom:n4f4", false).ToArray();
    var resetOk = afterReset.Contains("fru.suppression.attempted=False")
        && afterReset.Contains("fru.suppression.stopReason=not-run")
        && afterReset.Contains("fru.suppression.lastGateStatus=not-run")
        && afterReset.Contains("fru.suppression.armedSceneGeneration=-1");

    return writePassNotClean && !completedBeforeStreak && stabledAfterStreak && s2NearStable && s2StreakReset
        && zeroMatchNoStable && budgetBounded && genScopedReset
        && s6ClosedFirst && s6StillClosed && s6ReArmed && diagOk && resetOk;
});

Test(576, "fresh config + FRU normal preset enables approved-static placement without fabricating promotion", () =>
{
    var configuration = new Configuration();

    TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(
        configuration,
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId);

    var ownerFlipped = configuration.TitleBackgroundOverrideEnabled
        && configuration.TitleBackgroundCameraOverrideEnabled
        && configuration.TitleBackgroundIntegratedCompositionEnabled
        && configuration.TitleBackgroundCharaSelectPlacementEnabled
        && !configuration.TitleBackgroundV2Enabled
        && configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
        && configuration.TitleBackgroundCharacterSelectBackgroundMode == TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly
        && configuration.TitleBackgroundCharaSelectCameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended
        && configuration.TitleBackgroundCharacterSelectOverrideCandidateId == "custom:fru-clear-stage"
        && configuration.TitleBackgroundCharaSelectPlacementCandidateId == "custom:fru-clear-stage";

    // OneClick promotion 相当を偽装しない: PositionCaptured は false のまま、captured XYZ / rotation は未設定。
    var noFakePromotion = !configuration.TitleBackgroundCharaSelectPlacementPositionCaptured
        && configuration.TitleBackgroundCharaSelectPlacementPositionX == 0f
        && configuration.TitleBackgroundCharaSelectPlacementPositionY == 0f
        && configuration.TitleBackgroundCharaSelectPlacementPositionZ == 0f
        && configuration.TitleBackgroundCharaSelectPlacementRotation == 0f
        && !TitleBackgroundQuickCheckUiPresenter.IsPersistentCharaSelectPlacementConfigured(configuration);

    return ownerFlipped
        && noFakePromotion
        && TitleBackgroundQuickCheckUiPresenter.IsApprovedStaticPlacementAutoSetupConfigured(configuration)
        && TitleBackgroundQuickCheckUiPresenter.IsSimpleAutoSetupConfigured(configuration);
});

Test(577, "promoted FRU placement is preserved exactly by simple auto setup", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundOverrideEnabled = true,
        TitleBackgroundCameraOverrideEnabled = true,
        TitleBackgroundIntegratedCompositionEnabled = true,
        TitleBackgroundV2Enabled = false,
        TitleBackgroundCharaSelectPlacementEnabled = true,
        TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.CharaSelectOnly,
        TitleBackgroundCharacterSelectBackgroundMode = TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly,
        TitleBackgroundCharaSelectCameraFramingMode = TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        TitleBackgroundCharacterSelectOverrideCandidateId = "custom:fru-clear-stage",
        TitleBackgroundCharaSelectPlacementCandidateId = "custom:fru-clear-stage",
        TitleBackgroundCharaSelectPlacementPositionCaptured = true,
        TitleBackgroundCharaSelectPlacementPositionX = 123.5f,
        TitleBackgroundCharaSelectPlacementPositionY = 1f,
        TitleBackgroundCharaSelectPlacementPositionZ = 98.25f,
        TitleBackgroundCharaSelectPlacementRotation = 1.25f,
    };

    var wasPromoted = TitleBackgroundQuickCheckUiPresenter.IsPersistentCharaSelectPlacementConfigured(configuration);

    TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(
        configuration,
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId);

    return wasPromoted
        && configuration.TitleBackgroundCharaSelectPlacementEnabled
        && !configuration.TitleBackgroundV2Enabled
        && configuration.TitleBackgroundCharaSelectPlacementPositionCaptured
        && configuration.TitleBackgroundCharaSelectPlacementPositionX == 123.5f
        && configuration.TitleBackgroundCharaSelectPlacementPositionY == 1f
        && configuration.TitleBackgroundCharaSelectPlacementPositionZ == 98.25f
        && configuration.TitleBackgroundCharaSelectPlacementRotation == 1.25f
        && TitleBackgroundQuickCheckUiPresenter.IsPersistentCharaSelectPlacementConfigured(configuration);
});

Test(578, "Il Mheg normal preset keeps verified V2 baseline and is not approved-static eligible", () =>
{
    var configuration = new Configuration();
    TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(configuration, "custom:n4f4");

    TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet("custom:n4f4", out var ilMheg);

    return configuration.TitleBackgroundV2Enabled
        && !configuration.TitleBackgroundCharaSelectPlacementEnabled
        && !configuration.TitleBackgroundCharaSelectPlacementPositionCaptured
        && !TitleBackgroundQuickCheckUiPresenter.IsApprovedStaticProductionPlacementEligible(ilMheg)
        && !TitleBackgroundQuickCheckUiPresenter.IsApprovedStaticPlacementAutoSetupConfigured(configuration);
});

Test(579, "Elpis stays source-backed / OneClick-dependent and invents no static placement", () =>
{
    var configuration = new Configuration();
    TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(
        configuration,
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId);

    TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId,
        out var elpis);

    return elpis.RequiresSourceBackedLayout
        && !elpis.VerifiedInGame
        && !elpis.ApprovedStaticAnchor.HasValue
        && !TitleBackgroundQuickCheckUiPresenter.IsApprovedStaticProductionPlacementEligible(elpis)
        && configuration.TitleBackgroundV2Enabled
        && !configuration.TitleBackgroundCharaSelectPlacementEnabled
        && !configuration.TitleBackgroundCharaSelectPlacementPositionCaptured;
});

Test(580, "switching away from FRU does not leave the placement owner active for another candidate", () =>
{
    var configuration = new Configuration();
    TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(
        configuration,
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId);
    var fruEnabledPlacement = configuration.TitleBackgroundCharaSelectPlacementEnabled;

    TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(configuration, "custom:n4f4");

    return fruEnabledPlacement
        && !configuration.TitleBackgroundCharaSelectPlacementEnabled
        && configuration.TitleBackgroundV2Enabled
        && configuration.TitleBackgroundCharaSelectPlacementCandidateId == "custom:n4f4"
        && !configuration.TitleBackgroundCharaSelectPlacementPositionCaptured;
});

Test(581, "FRU VFX inventory gate collects only for FRU + pre-login + exact authorized scene, else fails closed", () =>
{
    static TitleBackgroundVfxInventoryGate G(
        bool candidateIsFru = true,
        bool isLoggedIn = false,
        bool sessionActive = true,
        bool hookReady = true,
        bool charaSelectMap = true,
        int sceneGeneration = 4,
        bool anchorAuthorized = true,
        int anchorGeneration = 4,
        bool activeLayoutAvailable = true,
        int initState = 7,
        uint loadedTerritory = 1238u,
        uint loadedLayer = 0u)
        => TitleBackgroundCharaSelectVfxInventoryLogic.Evaluate(
            candidateIsFru, isLoggedIn, sessionActive, hookReady, charaSelectMap, sceneGeneration,
            anchorAuthorized, anchorGeneration, activeLayoutAvailable, initState,
            loadedTerritory, loadedLayer, candidateTerritoryId: 1238u, candidateLayerFilterKey: 0u).Gate;

    var collects = G() == TitleBackgroundVfxInventoryGate.Collect;
    var notFru = G(candidateIsFru: false) == TitleBackgroundVfxInventoryGate.NotFruCandidate;
    var postLogin = G(isLoggedIn: true) == TitleBackgroundVfxInventoryGate.PostLogin;
    var hookDown = G(hookReady: false) == TitleBackgroundVfxInventoryGate.SessionOrHookNotReady;
    var sessionDown = G(sessionActive: false) == TitleBackgroundVfxInventoryGate.SessionOrHookNotReady;
    var notMap = G(charaSelectMap: false) == TitleBackgroundVfxInventoryGate.NotCharaSelectMap;
    var noGen = G(sceneGeneration: 0) == TitleBackgroundVfxInventoryGate.SceneGenerationNotObserved;
    var notAuthorized = G(anchorAuthorized: false) == TitleBackgroundVfxInventoryGate.SceneNotAuthorized;
    var genMismatch = G(anchorGeneration: 3) == TitleBackgroundVfxInventoryGate.SceneGenerationMismatch;
    var layoutNotReady = G(initState: 3) == TitleBackgroundVfxInventoryGate.ActiveLayoutNotReady;
    var layoutNull = G(activeLayoutAvailable: false) == TitleBackgroundVfxInventoryGate.ActiveLayoutNotReady;
    var territoryMismatch = G(loadedTerritory: 999u) == TitleBackgroundVfxInventoryGate.LoadedLayoutTerritoryMismatch;
    var layerMismatch = G(loadedLayer: 51u) == TitleBackgroundVfxInventoryGate.LoadedLayoutLayerMismatch;
    // fail-closed: loaded territory 0 (uninitialised) is a mismatch, never Collect.
    var zeroTerritory = G(loadedTerritory: 0u) == TitleBackgroundVfxInventoryGate.LoadedLayoutTerritoryMismatch;

    return collects && notFru && postLogin && hookDown && sessionDown && notMap && noGen
        && notAuthorized && genMismatch && layoutNotReady && layoutNull
        && territoryMismatch && layerMismatch && zeroTerritory;
});

Test(582, "FRU VFX inventory report stays bounded, always states writes=0, closes on stable count, and resets", () =>
{
    const int cap = TitleBackgroundCharaSelectVfxInventoryRuntimeState.MaxRepresentatives;

    var s = new TitleBackgroundCharaSelectVfxInventoryRuntimeState();
    s.ArmForGeneration(4);

    // one pass with more instances than the representative cap -> report never exceeds the cap.
    s.BeginPass();
    for (var i = 0; i < cap + 20; i++)
    {
        s.RecordInstance(isActive: i % 2 == 0, hasPrimaryPath: true, isPrimaryLoaded: true, hasGraphicsObject: true);
        s.OfferRepresentative(
            TitleBackgroundCharaSelectVfxInventoryLogic.FormatRepresentative(
                ((ulong)(uint)(1000 + i) << 32) | 0u, 0u, i % 2 == 0, true, true, 0xABCDEF01u,
                "bg/ex3/01_nvt_n4/common/vfx/eff/n4gw_petal" + i + ".avfx",
                TitleBackgroundCharaSelectVfxInventoryRuntimeState.RepresentativePathMaxLength),
            hasPrimaryPath: true,
            isActive: i % 2 == 0);
    }
    s.EndPass();

    var lines = s.BuildDiagnosticLines("custom:fru-clear-stage", true).ToArray();
    var repLines = lines
        .Where(l => l.StartsWith("fru.vfx.rep", StringComparison.Ordinal)
            && l.Length > 11 && char.IsDigit(l[11]))
        .ToArray();
    var boundedReps = repLines.Length == cap
        && s.RepresentativeCount <= cap
        && lines.Contains($"fru.vfx.representativeCount={cap}");
    var alwaysNoWrite = lines.Contains("fru.vfx.writes=0");
    var reportsTotals = lines.Contains($"fru.vfx.totalCount={cap + 20}")
        && lines.Any(l => l.StartsWith("fru.vfx.activeCount=", StringComparison.Ordinal))
        && lines.Any(l => l.StartsWith("fru.vfx.primaryPathCount=", StringComparison.Ordinal))
        && lines.Any(l => l.StartsWith("fru.vfx.graphicsObjectCount=", StringComparison.Ordinal));
    // representative line is compact: single line, path trimmed to the configured max.
    var repCompact = repLines.All(l =>
        l.Length <= 24 + TitleBackgroundCharaSelectVfxInventoryRuntimeState.RepresentativePathMaxLength + 40
        && !l.Contains('\n'));

    // stable-count streak closes the window; no writes are ever recorded regardless.
    for (var i = 0; i < TitleBackgroundCharaSelectVfxInventoryRuntimeState.StablePassTarget + 1; i++)
    {
        s.BeginPass();
        s.RecordInstance(isActive: true, hasPrimaryPath: true, isPrimaryLoaded: true, hasGraphicsObject: true);
        s.EndPass();
    }
    var closedOnStable = s.Completed && s.StopReason == "stable" && !s.ShouldRunPass();

    // read failures are counted but never escalate to a write.
    var s2 = new TitleBackgroundCharaSelectVfxInventoryRuntimeState();
    s2.ArmForGeneration(9);
    s2.BeginPass();
    s2.RecordReadFailure("instance:AccessViolationException");
    s2.EndPass();
    var readFailTracked = s2.BuildDiagnosticLines("custom:fru-clear-stage", true)
        .Contains("fru.vfx.readFailureCount=1")
        && s2.BuildDiagnosticLines("custom:fru-clear-stage", true).Contains("fru.vfx.writes=0");

    // deterministic managed path hash (stand-in identity; no raw PathCrc read).
    var h1 = TitleBackgroundCharaSelectVfxInventoryLogic.HashPath("BG/Foo/Bar.AVFX");
    var h2 = TitleBackgroundCharaSelectVfxInventoryLogic.HashPath("bg/foo/bar.avfx");
    var h3 = TitleBackgroundCharaSelectVfxInventoryLogic.HashPath("bg/foo/baz.avfx");
    var hashStable = h1 == h2 && h1 != h3;

    s.Reset();
    var afterReset = s.BuildDiagnosticLines("custom:n4f4", false).ToArray();
    var resetOk = afterReset.Contains("fru.vfx.attempted=False")
        && afterReset.Contains("fru.vfx.stopReason=not-run")
        && afterReset.Contains("fru.vfx.lastGateStatus=not-run")
        && afterReset.Contains("fru.vfx.armedSceneGeneration=-1")
        && afterReset.Contains("fru.vfx.writes=0")
        && afterReset.Contains("fru.vfx.representativeCount=0");

    return boundedReps && alwaysNoWrite && reportsTotals && repCompact
        && closedOnStable && readFailTracked && hashStable && resetOk;
});

Test(583, "TitleEdit VFX UUID derivation is deterministic pure arithmetic (instanceKey + (subId << 32))", () =>
{
    static ulong D(uint k, uint s) => TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(k, s);

    var subZeroIsInstanceKey = D(10861454u, 0u) == 10861454ul;
    var subShiftsToHigh32 = D(7u, 6u) == 7ul + ((ulong)6u << 32);
    var deterministic = D(123u, 45u) == D(123u, 45u);
    var distinctBySub = D(100u, 1u) != D(100u, 2u);
    var distinctByKey = D(1u, 5u) != D(2u, 5u);
    // matches RokasKil/TitleEdit Extensions/LayoutInstance.cs UUID(): Id.InstanceKey + ((ulong)SubId << 32)
    var maxSubId = D(0xABCDu, uint.MaxValue) == 0xABCDul + ((ulong)uint.MaxValue << 32);

    return subZeroIsInstanceKey && subShiftsToHigh32 && deterministic
        && distinctBySub && distinctByKey && maxSubId;
});

Test(584, "FRU VFX detail snapshot: bounded, latest-pass-wins, deterministic order, de-duplicated, cleared on reset", () =>
{
    const int max = TitleBackgroundCharaSelectVfxInventoryRuntimeState.MaxDetailRows;

    static TitleBackgroundVfxDetailEntry E(ulong mapKey, uint instanceKey, uint subId, bool active) =>
        new(mapKey, instanceKey, subId,
            TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(instanceKey, subId),
            active, true, true, 0x1234u, "bg/ex3/01_nvt_n4/common/vfx/eff/x.avfx");

    var s = new TitleBackgroundCharaSelectVfxInventoryRuntimeState();
    s.ArmForGeneration(4);
    var pendingBeforePass = s.DetailStatus == "pending";

    // pass 1: unsorted input + a duplicate map key -> snapshot sorted by uuid, duplicate dropped.
    s.BeginPass();
    s.RecordDetail(E(30, 300u, 0u, false));
    s.RecordDetail(E(10, 100u, 0u, true));
    s.RecordDetail(E(20, 200u, 0u, false));
    s.RecordDetail(E(10, 999u, 7u, false)); // same instance map key -> ignored
    s.EndPass();

    var snap1 = s.BuildDetailFileLines("custom:fru-clear-stage").ToArray();
    var rows1 = snap1.Where(l => l.StartsWith("vfx[", StringComparison.Ordinal)).ToArray();
    var dedup = rows1.Length == 3 && s.DetailSnapshotCount == 3;
    var ordered = rows1[0].Contains("instanceKey=100")
        && rows1[1].Contains("instanceKey=200")
        && rows1[2].Contains("instanceKey=300");
    var readyStatus = s.DetailStatus == "ready";
    var fileMeta = snap1.Contains("detailRowCount=3")
        && snap1.Contains("writes=0")
        && snap1.Any(l => l.StartsWith("pathCrc=na", StringComparison.Ordinal));

    // pass 2: a different, smaller set -> latest completed pass wins (old rows gone).
    s.BeginPass();
    s.RecordDetail(E(50, 500u, 0u, true));
    s.EndPass();
    var rows2 = s.BuildDetailFileLines("custom:fru-clear-stage")
        .Where(l => l.StartsWith("vfx[", StringComparison.Ordinal)).ToArray();
    var latestWins = rows2.Length == 1 && rows2[0].Contains("instanceKey=500") && s.DetailSnapshotCount == 1;

    // bound: snapshot never exceeds MaxDetailRows / MaxScanPerPass even when fed more.
    var s2 = new TitleBackgroundCharaSelectVfxInventoryRuntimeState();
    s2.ArmForGeneration(9);
    s2.BeginPass();
    for (var i = 0; i < max + 50; i++)
    {
        s2.RecordDetail(E((ulong)(i + 1), (uint)(i + 1), 0u, i % 2 == 0));
    }
    s2.EndPass();
    var bounded = s2.DetailSnapshotCount <= max
        && s2.DetailSnapshotCount <= TitleBackgroundCharaSelectVfxInventoryRuntimeState.MaxScanPerPass;

    // reset clears the snapshot, status, and file body.
    s.Reset();
    var cleared = s.DetailSnapshotCount == 0
        && s.DetailStatus == "not-run"
        && !s.BuildDetailFileLines("custom:n4f4").Any(l => l.StartsWith("vfx[", StringComparison.Ordinal));

    return pendingBeforePass && dedup && ordered && readyStatus && fileMeta
        && latestWins && bounded && cleared;
});

Test(585, "compact FRU VFX report exposes only detailFile/detailStatus/detailRowCount, never the full inventory", () =>
{
    static TitleBackgroundVfxDetailEntry E(uint k) =>
        new(k, k, 0u, TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(k, 0u),
            true, true, true, 0xABCDu, "bg/ex3/01_nvt_n4/common/vfx/eff/p" + k + ".avfx");

    var s = new TitleBackgroundCharaSelectVfxInventoryRuntimeState();
    s.ArmForGeneration(4);
    s.BeginPass();
    for (var i = 0; i < 60; i++)
    {
        s.RecordInstance(isActive: i == 0, hasPrimaryPath: true, isPrimaryLoaded: true, hasGraphicsObject: true);
        s.RecordDetail(E((uint)(i + 1)));
    }
    s.EndPass();

    var compact = s.BuildDiagnosticLines("custom:fru-clear-stage", true).ToArray();

    // the compact / clipboard report never carries per-VFX rows or the detail-file body.
    var noFullInventory = compact.All(l =>
        !l.StartsWith("vfx[", StringComparison.Ordinal)
        && !l.Contains("uuidFormula", StringComparison.Ordinal)
        && !l.Contains(" instanceKey=", StringComparison.Ordinal));
    var boundedLines = compact.Count(l => l.StartsWith("fru.vfx.", StringComparison.Ordinal)) <= 32;
    var hasDetailKeys =
        compact.Contains($"fru.vfx.detailFile={TitleBackgroundCharaSelectVfxInventoryRuntimeState.DetailFileName}")
        && compact.Any(l => l.StartsWith("fru.vfx.detailStatus=", StringComparison.Ordinal))
        && compact.Contains("fru.vfx.detailRowCount=60");
    // every emitted fru.vfx.* key is in the auto-copy allowlist single source (skill: no impl/allowlist drift).
    var allowlisted = compact
        .Where(l => l.StartsWith("fru.vfx.", StringComparison.Ordinal))
        .Select(l => l[..l.IndexOf('=')])
        .All(k => TitleBackgroundCharaSelectVfxInventoryRuntimeState.DiagnosticKeys.Contains(k));

    return noFullInventory && boundedLines && hasDetailKeys && allowlisted;
});

Test(586, "Phase A selection-change classifier maps evidence to timing-gap / coverage-gap / deactivation-semantics / insufficient-evidence", () =>
{
    static TitleBackgroundSceneObjectSelectionChangeClass C(TitleBackgroundSceneObjectSelectionChangeEvidence e)
        => TitleBackgroundCharaSelectSceneObjectSuppressionLogic.ClassifySelectionChange(e).Class;
    static string R(TitleBackgroundSceneObjectSelectionChangeEvidence e)
        => TitleBackgroundCharaSelectSceneObjectSuppressionLogic.ClassifySelectionChange(e).Reason;

    var thr = TitleBackgroundCharaSelectSceneObjectSuppressionLogic.PromptFirstPassMsThreshold;

    // evidence: (observed, eventToFirstPassMs, blockedFrames, firstBlockingGate, firstPassCaptured,
    //   firstPassMatchedActiveBeforeWrite, firstPassWrites, firstPassConfirmedInactive, firstPassStillActive,
    //   activeNonDenyKeepSampleCount, activeNonDenyKeepResolvedInactiveCount, windowStopReason)

    // no re-armed pass captured -> insufficient-evidence (never guesses).
    var notObserved = C(new(false, -1, 0, "none", false, 0, 0, 0, 0, 0, 0, "running"))
        == TitleBackgroundSceneObjectSelectionChangeClass.InsufficientEvidence;
    var observedButNoPass = C(new(true, -1, 3, "active-layout-not-ready", false, 0, 0, 0, 0, 0, 0, "running"))
        == TitleBackgroundSceneObjectSelectionChangeClass.InsufficientEvidence;

    // deny-covered group still active after a gate delay -> timing-gap even if the pass was otherwise a full success.
    var blockedDelay = C(new(true, 40, 2, "active-layout-not-ready", true, 1, 1, 1, 0, 0, 0, "running"))
        == TitleBackgroundSceneObjectSelectionChangeClass.TimingGap;
    // deny-covered group still active after a long event->first-pass latency -> timing-gap.
    var latencyDelay = C(new(true, thr + 1, 0, "authorized", true, 1, 1, 1, 0, 0, 0, "running"))
        == TitleBackgroundSceneObjectSelectionChangeClass.TimingGap;

    // MUST FIX 1: DeactivationSemantics only when EVERY active matched instance was written,
    // EVERY one confirmed inactive, and none still active in the first re-armed pass.
    var fullSuccessFade = C(new(true, 50, 0, "authorized", true, 2, 2, 2, 0, 0, 0, "stable"))
        == TitleBackgroundSceneObjectSelectionChangeClass.DeactivationSemantics;
    // partial write (1 of 2) -> timing-gap, not a fade claim.
    var partialWrite = new TitleBackgroundSceneObjectSelectionChangeEvidence(
        true, 50, 0, "authorized", true, 2, 1, 1, 1, 0, 0, "running");
    var partialWriteIsTiming = C(partialWrite) == TitleBackgroundSceneObjectSelectionChangeClass.TimingGap
        && R(partialWrite).StartsWith("deny-covered-group-partial-suppression-in-first-pass", StringComparison.Ordinal);
    // partial readback (1 of 2 confirmed) -> timing-gap.
    var partialReadback = C(new(true, 50, 0, "authorized", true, 2, 2, 1, 1, 0, 0, "running"))
        == TitleBackgroundSceneObjectSelectionChangeClass.TimingGap;
    // still-active > 0 blocks the fade claim even when confirmed == active.
    var stillActiveBlocksFade = C(new(true, 50, 0, "authorized", true, 2, 2, 2, 1, 0, 0, "running"))
        == TitleBackgroundSceneObjectSelectionChangeClass.TimingGap;

    // MUST FIX 2: CoverageGap only when a sampled active non-deny SharedGroup was confirmed
    // active -> inactive within the same window.
    var coverageConfirmed = C(new(true, 30, 0, "authorized", true, 0, 0, 0, 0, 2, 1, "no-matched-instances"))
        == TitleBackgroundSceneObjectSelectionChangeClass.CoverageGap;
    var coverageUnconfirmed = new TitleBackgroundSceneObjectSelectionChangeEvidence(
        true, 30, 0, "authorized", true, 0, 0, 0, 0, 2, 0, "stable");
    var coverageUnconfirmedIsInsufficient =
        C(coverageUnconfirmed) == TitleBackgroundSceneObjectSelectionChangeClass.InsufficientEvidence
        && R(coverageUnconfirmed).StartsWith("coverage-candidate-unconfirmed", StringComparison.Ordinal);
    // no deny-covered active group and no non-deny sample at all -> insufficient-evidence.
    var nothingAssociated = C(new(true, 30, 0, "authorized", true, 0, 0, 0, 0, 0, 0, "no-matched-instances"))
        == TitleBackgroundSceneObjectSelectionChangeClass.InsufficientEvidence;

    // threshold boundary: exactly at the prompt ms threshold with 0 blocked frames is still prompt.
    var atThreshold = C(new(true, thr, 0, "authorized", true, 1, 1, 1, 0, 0, 0, "stable"))
        == TitleBackgroundSceneObjectSelectionChangeClass.DeactivationSemantics;

    return notObserved && observedButNoPass && blockedDelay && latencyDelay
        && fullSuccessFade && partialWriteIsTiming && partialReadback && stillActiveBlocksFade
        && coverageConfirmed && coverageUnconfirmedIsInsufficient && nothingAssociated && atThreshold;
});

Test(587, "Phase A selection-change evidence: re-arm survives forceReArm, blocked frames counted, first pass snapshot captured, coverage sample bounded+sanitized, cleared on new generation/reset", () =>
{
    var keyA = ((ulong)10865420u << 32) | 0u;

    // --- prompt path: deny-covered active group fully suppressed and confirmed in the first re-armed pass ---
    var s = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s.ArmForGeneration(5);
    s.NoteSelectionChangeReArm(generationAtEvent: 5, generationAtReArm: 5, eventTickMs: 1000, eventToReArmMs: 16);
    s.ArmForGeneration(5, forceReArm: true); // same generation -> forceReArm keeps the evidence just noted
    var evidenceSurvivesForceReArm = s.SelectionChangeReArmCount == 1
        && s.AwaitingFirstReArmedPass
        && s.SelectionChangeEventTickMs == 1000
        && s.SelectionChangeEventToReArmMs == 16;
    s.MarkFirstReArmedPassStarting(50);
    var capturingFlag = s.CapturingFirstReArmedPass;
    s.BeginPass();
    s.RecordScanned();
    s.RecordMatched(keyA);
    s.TryConsumeWriteBudget(keyA);
    s.RecordWriteAttempted(keyA, "SharedGroup");
    s.RecordConfirmedInactive(keyA);
    s.EndPass();
    var firstPassCaptured = s.FirstReArmedPassCaptured
        && s.FirstReArmedPassMatched == 1
        && s.FirstReArmedPassMatchedActiveBeforeWrite == 1
        && s.FirstReArmedPassWrites == 1
        && s.FirstReArmedPassConfirmedInactive == 1
        && s.FirstReArmedPassStillActive == 0
        && s.SelectionChangeEventToFirstPassMs == 50
        && s.SelectionChangeBlockedFramesBeforeFirstPass == 0
        && !s.AwaitingFirstReArmedPass
        && !s.CapturingFirstReArmedPass;
    var promptClass = s.BuildDiagnosticLines("custom:fru-clear-stage", true)
        .Contains("fru.suppression.selectionChange.class=DeactivationSemantics");
    // a second pass does not overwrite the first-pass snapshot.
    s.BeginPass();
    s.RecordScanned();
    s.RecordMatched(keyA);
    s.RecordAlreadyInactive(keyA);
    s.EndPass();
    var snapshotStable = s.FirstReArmedPassMatchedActiveBeforeWrite == 1 && s.FirstReArmedPassWrites == 1;

    // --- blocked-frame path: gate rejections after re-arm are counted, first blocking gate is sticky ---
    var s2 = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s2.ArmForGeneration(8);
    s2.NoteSelectionChangeReArm(8, 8, 2000, 20);
    s2.ArmForGeneration(8, forceReArm: true);
    s2.RecordGateStatus("active-layout-not-ready");
    s2.RecordGateStatus("loaded-layout-territory-mismatch");
    s2.RecordGateStatus("authorized"); // not counted as blocked
    s2.MarkFirstReArmedPassStarting(40);
    s2.BeginPass();
    s2.RecordScanned();
    s2.RecordMatched(keyA); // active, not already inactive
    s2.EndPass();
    var blockedCounted = s2.SelectionChangeBlockedFramesBeforeFirstPass == 2
        && s2.SelectionChangeFirstBlockingGate == "active-layout-not-ready"
        && s2.FirstReArmedPassMatchedActiveBeforeWrite == 1;
    var timingClass = s2.BuildDiagnosticLines("custom:fru-clear-stage", true)
        .Contains("fru.suppression.selectionChange.class=TimingGap");

    // --- coverage sampling: active non-deny keep paths recorded, bounded, sanitized to game-asset roots ---
    var s3 = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s3.ArmForGeneration(9);
    s3.NoteSelectionChangeReArm(9, 9, 3000, 10);
    s3.ArmForGeneration(9, forceReArm: true);
    s3.MarkFirstReArmedPassStarting(30);
    s3.BeginPass();
    s3.RecordScanned();
    s3.RecordActiveNonDenyKeepPath("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a5_bg01.sgb");
    s3.RecordActiveNonDenyKeepPath("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a5_bg01.sgb"); // duplicate ignored
    s3.RecordActiveNonDenyKeepPath("plugin/local/private-note.txt"); // not a game-asset root -> dropped
    s3.RecordActiveNonDenyKeepPath("bgcommon/nature/prop/sgbg_x.sgb");
    for (var i = 0; i < 40; i++)
    {
        s3.RecordActiveNonDenyKeepPath($"bg/ex3/01_nvt_n4/shared/for_bg/sgbg_pad_{i}.sgb");
    }
    var coverageBounded = s3.ActiveNonDenyKeepPathSampleCount
            == TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState.MaxActiveNonDenyKeepPathSamples
        && s3.ActiveNonDenyKeepPaths.Contains("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a5_bg01.sgb")
        && s3.ActiveNonDenyKeepPaths.Contains("bgcommon/nature/prop/sgbg_x.sgb")
        && !s3.ActiveNonDenyKeepPaths.Contains("plugin/local/private-note.txt")
        && s3.ActiveNonDenyKeepPaths.All(p =>
            p.StartsWith("bg/", StringComparison.Ordinal) || p.StartsWith("bgcommon/", StringComparison.Ordinal));
    s3.EndPass();
    // sampling alone (no confirmed transition) does NOT reach CoverageGap (MUST FIX 2).
    var samplingAloneNotCoverage = s3.BuildDiagnosticLines("custom:fru-clear-stage", true)
        .Contains("fru.suppression.selectionChange.class=InsufficientEvidence");
    // recording a first-pass sample is inert outside the first re-armed pass.
    s3.RecordActiveNonDenyKeepPath("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_after_pass.sgb");
    var inertAfterCapture = !s3.ActiveNonDenyKeepPaths.Contains("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_after_pass.sgb");

    // --- a genuinely new scene generation drops the previous switch evidence; Reset() drops it too ---
    s.ArmForGeneration(6); // new generation, no forceReArm
    var clearedOnNewGeneration = s.SelectionChangeReArmCount == 0
        && !s.FirstReArmedPassCaptured
        && s.SelectionChangeEventToFirstPassMs == -1
        && s.SelectionChangeFirstBlockingGate == "none"
        && s.ActiveNonDenyKeepPathResolvedInactiveCount == 0
        && s.BuildDiagnosticLines("custom:fru-clear-stage", true)
            .Contains("fru.suppression.selectionChange.class=InsufficientEvidence");
    s2.Reset();
    var clearedOnReset = s2.BuildDiagnosticLines("custom:n4f4", false).ToArray() is var rl
        && rl.Contains("fru.suppression.selectionChange.reArmCount=0")
        && rl.Contains("fru.suppression.selectionChange.eventToFirstPassMs=-1")
        && rl.Contains("fru.suppression.selectionChange.activeNonDenyKeepPaths=none")
        && rl.Contains("fru.suppression.selectionChange.activeNonDenyKeepResolvedInactiveCount=0")
        && rl.Contains("fru.suppression.terminalAtPassCount=-1");

    return evidenceSurvivesForceReArm && capturingFlag && firstPassCaptured && promptClass && snapshotStable
        && blockedCounted && timingClass && coverageBounded && samplingAloneNotCoverage && inertAfterCapture
        && clearedOnNewGeneration && clearedOnReset;
});

Test(588, "Phase A selection-change diagnostic keys come from the single-source DiagnosticKeys array wired into the auto-report allowlist", () =>
{
    var s = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s.ArmForGeneration(3);
    var lines = s.BuildDiagnosticLines("custom:fru-clear-stage", true).ToArray();
    var emittedKeys = lines.Select(l => l[..l.IndexOf('=')]).ToArray();

    var everyEmittedAllowlisted = emittedKeys.All(k =>
        TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState.DiagnosticKeys.Contains(k));
    var everyAllowlistedEmitted = TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState.DiagnosticKeys
        .All(k => emittedKeys.Contains(k));
    var hasPhaseAKeys = emittedKeys.Contains("fru.suppression.selectionChange.class")
        && emittedKeys.Contains("fru.suppression.selectionChange.classReason")
        && emittedKeys.Contains("fru.suppression.selectionChange.classNote")
        && emittedKeys.Contains("fru.suppression.selectionChange.eventToFirstPassMs")
        && emittedKeys.Contains("fru.suppression.selectionChange.blockedFramesBeforeFirstPass")
        && emittedKeys.Contains("fru.suppression.selectionChange.activeNonDenyKeepPaths")
        && emittedKeys.Contains("fru.suppression.selectionChange.activeNonDenyKeepResolvedInactiveCount")
        && emittedKeys.Contains("fru.suppression.terminalAtPassCount");
    // SHOULD: the note line states the auto class must be read with the external "flicker was seen" observation.
    var classNoteWording = lines.Any(l =>
        l.StartsWith("fru.suppression.selectionChange.classNote=", StringComparison.Ordinal)
        && l.Contains("external observation", StringComparison.Ordinal)
        && l.Contains("does not standalone-prove", StringComparison.Ordinal));

    var root = FindRepositoryRoot();
    var quickCheckSource = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundQuickCheck.cs"));
    var wired = quickCheckSource.Contains(
        "IncludedKeys.UnionWith(TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState.DiagnosticKeys)",
        StringComparison.Ordinal);
    // the hand-listed fru.suppression.* literals were removed in favour of the single source.
    var noHandListedLiterals = !quickCheckSource.Contains("\"fru.suppression.candidate\"", StringComparison.Ordinal);

    var selected = TitleBackgroundAutomaticCheckDiagnosticSelector.Select(lines);
    var selectorKeepsPhaseA = selected.Any(l =>
        l.StartsWith("fru.suppression.selectionChange.class=", StringComparison.Ordinal))
        && selected.Any(l => l.StartsWith("fru.suppression.selectionChange.activeNonDenyKeepResolvedInactiveCount=", StringComparison.Ordinal))
        && selected.Any(l => l.StartsWith("fru.suppression.terminalAtPassCount=", StringComparison.Ordinal));

    return everyEmittedAllowlisted && everyAllowlistedEmitted && hasPhaseAKeys && classNoteWording
        && wired && noHandListedLiterals && selectorKeepsPhaseA;
});

Test(589, "MUST FIX 1: partial suppression/readback in the first re-armed pass is timing-gap; only a full write+confirm+no-still-active pass is deactivation-semantics", () =>
{
    var k1 = ((ulong)111u << 32) | 0u;
    var k2 = ((ulong)222u << 32) | 0u;

    static TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState Armed(int gen)
    {
        var st = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
        st.ArmForGeneration(gen);
        st.NoteSelectionChangeReArm(gen, gen, 100, 5);
        st.ArmForGeneration(gen, forceReArm: true);
        st.MarkFirstReArmedPassStarting(20); // prompt: 20ms, 0 blocked frames
        st.BeginPass();
        return st;
    }
    static string Class(TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState st)
        => st.BuildDiagnosticLines("custom:fru-clear-stage", true)
            .First(l => l.StartsWith("fru.suppression.selectionChange.class=", StringComparison.Ordinal))
            .Split('=')[1];

    // two active deny-covered instances, both written, only ONE confirms inactive, the other stays active.
    var partial = Armed(2);
    partial.RecordScanned(); partial.RecordScanned();
    partial.RecordMatched(k1); partial.TryConsumeWriteBudget(k1); partial.RecordWriteAttempted(k1, "SharedGroup"); partial.RecordConfirmedInactive(k1);
    partial.RecordMatched(k2); partial.TryConsumeWriteBudget(k2); partial.RecordWriteAttempted(k2, "SharedGroup"); partial.RecordStillActive(k2);
    partial.EndPass();
    var partialIsTiming = partial.FirstReArmedPassMatchedActiveBeforeWrite == 2
        && partial.FirstReArmedPassWrites == 2
        && partial.FirstReArmedPassConfirmedInactive == 1
        && partial.FirstReArmedPassStillActive == 1
        && Class(partial) == "TimingGap";

    // one active deny-covered instance, write budget exhausted so 0 writes -> partial -> timing-gap.
    var noWrite = Armed(3);
    noWrite.RecordScanned();
    noWrite.RecordMatched(k1);
    noWrite.RecordBudgetExhausted(k1); // no write, still active
    noWrite.EndPass();
    var noWriteIsTiming = noWrite.FirstReArmedPassMatchedActiveBeforeWrite == 1
        && noWrite.FirstReArmedPassWrites == 0
        && Class(noWrite) == "TimingGap";

    // full success: every active instance written, every one confirmed inactive, none still active.
    var full = Armed(4);
    full.RecordScanned(); full.RecordScanned();
    full.RecordMatched(k1); full.TryConsumeWriteBudget(k1); full.RecordWriteAttempted(k1, "SharedGroup"); full.RecordConfirmedInactive(k1);
    full.RecordMatched(k2); full.TryConsumeWriteBudget(k2); full.RecordWriteAttempted(k2, "SharedGroup"); full.RecordConfirmedInactive(k2);
    full.EndPass();
    var fullIsDeactivation = full.FirstReArmedPassMatchedActiveBeforeWrite == 2
        && full.FirstReArmedPassWrites == 2
        && full.FirstReArmedPassConfirmedInactive == 2
        && full.FirstReArmedPassStillActive == 0
        && Class(full) == "DeactivationSemantics";

    return partialIsTiming && noWriteIsTiming && fullIsDeactivation;
});

Test(590, "MUST FIX 2: coverage-gap needs a sampled non-deny SharedGroup confirmed active->inactive by read-only follow-up within the same bounded window", () =>
{
    const string p1 = "bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a5_bg01.sgb";

    static TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState Sampled(int gen)
    {
        var st = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
        st.ArmForGeneration(gen);
        st.NoteSelectionChangeReArm(gen, gen, 100, 5);
        st.ArmForGeneration(gen, forceReArm: true);
        st.MarkFirstReArmedPassStarting(30);
        st.BeginPass();
        st.RecordScanned();
        st.RecordActiveNonDenyKeepPath("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a5_bg01.sgb");
        st.RecordActiveNonDenyKeepPath("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a5_bg02.sgb");
        st.EndPass();
        return st;
    }
    static string[] Lines(TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState st)
        => st.BuildDiagnosticLines("custom:fru-clear-stage", true).ToArray();

    // no follow-up transition observed -> coverage-candidate, still InsufficientEvidence.
    var unconfirmed = Lines(Sampled(10));
    var staysInsufficient = unconfirmed.Contains("fru.suppression.selectionChange.class=InsufficientEvidence")
        && unconfirmed.Any(l => l.StartsWith("fru.suppression.selectionChange.classReason=coverage-candidate-unconfirmed", StringComparison.Ordinal))
        && unconfirmed.Contains("fru.suppression.selectionChange.activeNonDenyKeepResolvedInactiveCount=0");

    // a later pass in the same window observes p1 with NO active instance -> transition confirmed -> CoverageGap.
    var confirmed = Sampled(11);
    var followUpEnabled = confirmed.ShouldFollowUpNonDenyKeepPaths;
    confirmed.BeginPass();
    confirmed.RecordScanned();
    confirmed.RecordNonDenyKeepPathFollowUp("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_unsampled.sgb", isActive: false); // not sampled -> ignored
    confirmed.RecordNonDenyKeepPathFollowUp(p1, isActive: false); // sampled, no active instance this pass -> transition
    confirmed.EndPass();
    var confirmedLines = Lines(confirmed);
    var becomesCoverageGap = followUpEnabled
        && confirmed.ActiveNonDenyKeepPathResolvedInactiveCount == 1
        && confirmedLines.Contains("fru.suppression.selectionChange.class=CoverageGap")
        && confirmedLines.Any(l => l.StartsWith(
            "fru.suppression.selectionChange.classReason=active-non-deny-sharedgroup-went-inactive-within-window:1/2",
            StringComparison.Ordinal))
        && confirmedLines.Contains("fru.suppression.selectionChange.activeNonDenyKeepResolvedInactiveCount=1");

    // follow-up is inert before the first pass is captured (no sample set yet).
    var beforeCapture = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    beforeCapture.ArmForGeneration(12);
    beforeCapture.NoteSelectionChangeReArm(12, 12, 100, 5);
    beforeCapture.ArmForGeneration(12, forceReArm: true);
    var inertBeforeCapture = !beforeCapture.ShouldFollowUpNonDenyKeepPaths;
    beforeCapture.RecordNonDenyKeepPathFollowUp(p1, isActive: false);
    var inertBeforeCaptureNoRecord = beforeCapture.ActiveNonDenyKeepPathResolvedInactiveCount == 0;

    // a new scene generation clears the resolved-inactive set and the sample set.
    confirmed.ArmForGeneration(99);
    var clearedOnNewGeneration = confirmed.ActiveNonDenyKeepPathResolvedInactiveCount == 0
        && confirmed.ActiveNonDenyKeepPathSampleCount == 0;

    return staysInsufficient && becomesCoverageGap && inertBeforeCapture && inertBeforeCaptureNoRecord
        && clearedOnNewGeneration;
});

Test(591, "MUST FIX (review 5097939613): coverage follow-up aggregates per-path per-pass; a pass with any active instance of a sampled path does not resolve it", () =>
{
    // one sampled primary path, backed by two in-game SharedGroup instances.
    const string shared = "bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_shared.sgb";

    var st = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    st.ArmForGeneration(40);
    st.NoteSelectionChangeReArm(40, 40, 100, 5);
    st.ArmForGeneration(40, forceReArm: true);
    st.MarkFirstReArmedPassStarting(30);
    st.BeginPass();
    st.RecordScanned();
    st.RecordActiveNonDenyKeepPath(shared);
    st.EndPass();
    var sampledOnce = st.ActiveNonDenyKeepPathSampleCount == 1;

    // follow-up pass 1: two instances of the same primary path - one inactive, one still active.
    // per-path per-pass aggregation must NOT resolve while any instance of the path is active.
    st.BeginPass();
    st.RecordScanned();
    st.RecordScanned();
    st.RecordNonDenyKeepPathFollowUp(shared, isActive: false);
    st.RecordNonDenyKeepPathFollowUp(shared, isActive: true);
    st.EndPass();
    var notResolvedWhileOneActive = st.ActiveNonDenyKeepPathResolvedInactiveCount == 0
        && st.ShouldFollowUpNonDenyKeepPaths; // window still open, still chasing the transition
    var stillInsufficient = st.BuildDiagnosticLines("custom:fru-clear-stage", true)
        .Contains("fru.suppression.selectionChange.class=InsufficientEvidence");

    // follow-up pass 2: every observed instance of the path is inactive -> now resolved -> CoverageGap.
    st.BeginPass();
    st.RecordScanned();
    st.RecordScanned();
    st.RecordNonDenyKeepPathFollowUp(shared, isActive: false);
    st.RecordNonDenyKeepPathFollowUp(shared, isActive: false);
    st.EndPass();
    var resolvedWhenAllInactive = st.ActiveNonDenyKeepPathResolvedInactiveCount == 1
        && st.BuildDiagnosticLines("custom:fru-clear-stage", true)
            .Contains("fru.suppression.selectionChange.class=CoverageGap");

    // a pass where the sampled path is not observed at all must not resolve it.
    var st2 = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    st2.ArmForGeneration(41);
    st2.NoteSelectionChangeReArm(41, 41, 100, 5);
    st2.ArmForGeneration(41, forceReArm: true);
    st2.MarkFirstReArmedPassStarting(30);
    st2.BeginPass();
    st2.RecordScanned();
    st2.RecordActiveNonDenyKeepPath(shared);
    st2.EndPass();
    st2.BeginPass();
    st2.RecordScanned(); // sampled path not seen this pass
    st2.EndPass();
    var notResolvedWhenUnobserved = st2.ActiveNonDenyKeepPathResolvedInactiveCount == 0;

    return sampledOnce && notResolvedWhileOneActive && stillInsufficient
        && resolvedWhenAllInactive && notResolvedWhenUnobserved;
});

Test(592, "Phase A UX: SelectionChangeReportReady - when the final delta diagnostic is armed, publish waits for the bounded window terminal / session end, ignoring the old classifier", () =>
{
    static bool Ready(
        TitleBackgroundSceneObjectSelectionChangeClass c, bool completed, long elapsedMs, bool sessionEnding,
        int sample, bool followUpTerminal, bool finalArmed, bool finalComplete)
        => TitleBackgroundCharaSelectSceneObjectSuppressionLogic.SelectionChangeReportReady(
            c, completed, elapsedMs, sessionEnding, sample, followUpTerminal, finalArmed, finalComplete);

    var timeout = TitleBackgroundCharaSelectSceneObjectSuppressionLogic.SelectionChangeReportTimeoutMs;
    var insufficient = TitleBackgroundSceneObjectSelectionChangeClass.InsufficientEvidence;
    var coverageGap = TitleBackgroundSceneObjectSelectionChangeClass.CoverageGap;

    // session end is a hard stop regardless of everything.
    var sessionEndAlways =
        Ready(insufficient, false, 0, true, 12, false, true, false)
        && Ready(coverageGap, false, 0, true, 0, false, false, false);

    // final diagnostic armed: even a positive old class does NOT publish early; only window terminal does.
    var finalArmedWaitsForWindow =
        !Ready(coverageGap, true, timeout + 1, false, 0, true, /*finalArmed*/ true, /*finalComplete*/ false)
        && !Ready(insufficient, true, timeout + 1, false, 12, true, true, false)
        && Ready(insufficient, true, 0, false, 12, false, /*finalArmed*/ true, /*finalComplete*/ true)
        && Ready(coverageGap, false, 0, false, 0, false, true, true);

    // final diagnostic NOT armed: legacy behaviour (positive class immediate; samples wait for follow-up).
    var legacyPositiveImmediate =
        Ready(TitleBackgroundSceneObjectSelectionChangeClass.TimingGap, false, 10, false, 0, false, false, false)
        && Ready(TitleBackgroundSceneObjectSelectionChangeClass.DeactivationSemantics, false, 10, false, 0, false, false, false);
    var legacySamplesWaitForFollowUp =
        !Ready(insufficient, true, timeout + 1, false, 12, false, false, false)
        && Ready(insufficient, true, 0, false, 12, true, false, false);
    var legacyNoSamples =
        !Ready(insufficient, false, timeout - 1, false, 0, false, false, false)
        && Ready(insufficient, true, 0, false, 0, false, false, false);

    // ShouldArmCoverageFollowUp: after one switch + WRITE-window completion, the bounded window always arms.
    var armGate =
        TitleBackgroundCharaSelectSceneObjectSuppressionLogic.ShouldArmCoverageFollowUp(true, true)
        && !TitleBackgroundCharaSelectSceneObjectSuppressionLogic.ShouldArmCoverageFollowUp(true, false)
        && !TitleBackgroundCharaSelectSceneObjectSuppressionLogic.ShouldArmCoverageFollowUp(false, true);

    return sessionEndAlways && finalArmedWaitsForWindow
        && legacyPositiveImmediate && legacySamplesWaitForFollowUp && legacyNoSamples && armGate;
});

Test(593, "Phase A UX: FRU selection-change report uses a dedicated file, keeps only fru.suppression.* lines, and never reuses the auto-check report", () =>
{
    var s = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s.ArmForGeneration(5);
    s.NoteSelectionChangeReArm(5, 5, 1000, 12);
    s.ArmForGeneration(5, forceReArm: true);
    s.MarkFirstReArmedPassStarting(40);
    s.BeginPass();
    s.RecordScanned();
    s.RecordMatched(((ulong)7u << 32) | 0u);
    s.TryConsumeWriteBudget(((ulong)7u << 32) | 0u);
    s.RecordWriteAttempted(((ulong)7u << 32) | 0u, "SharedGroup");
    s.RecordConfirmedInactive(((ulong)7u << 32) | 0u);
    s.EndPass();

    var lines = s.BuildDiagnosticLines("custom:fru-clear-stage", true).ToArray();
    var report = TitleBackgroundSelectionChangeReportBuilder.Build(
        new DateTimeOffset(2026, 9, 3, 15, 30, 0, TimeSpan.Zero),
        "custom:fru-clear-stage",
        "classified",
        lines);
    var reportLines = report.Split(Environment.NewLine);

    var dedicatedFileName = TitleBackgroundSelectionChangeReportBuilder.FileName == "title-background-selection-change-diag.txt"
        && !string.Equals(
            TitleBackgroundSelectionChangeReportBuilder.FileName,
            TitleBackgroundAutomaticCheckReportBuilder.FileName,
            StringComparison.Ordinal);
    var hasHeader = reportLines[0] == "[XIV Mini Util] FRU selection-change diagnostic"
        && reportLines.Any(l => l.StartsWith("[XIV Mini Util] trigger=classified", StringComparison.Ordinal))
        && reportLines.Any(l => l.Contains("OneClick / automatic QuickCheck was not started", StringComparison.Ordinal)
            && l.Contains("config unchanged", StringComparison.Ordinal));
    var carriesClassification = reportLines.Any(l =>
        l.StartsWith("[XIV Mini Util] fru.suppression.selectionChange.class=", StringComparison.Ordinal));
    // body payload lines are exactly the fru.suppression.* diagnostic subset.
    var payload = reportLines
        .SkipWhile(l => l != "[XIV Mini Util] --- selectionChange ---")
        .Skip(1)
        .ToArray();
    var payloadOnlySuppression = payload.Length == lines.Length
        && payload.All(l => l.StartsWith("[XIV Mini Util] fru.suppression.", StringComparison.Ordinal));
    var noAutoCheckReuse = !report.Contains("title-background-auto-check", StringComparison.Ordinal)
        && !report.Contains("Title Background automatic check", StringComparison.Ordinal);

    return dedicatedFileName && hasHeader && carriesClassification && payloadOnlySuppression && noAutoCheckReuse;
});

Test(594, "Phase A UX: selection-change auto-copy reuses the Plugin.UiEvents clipboard queue pattern and starts no OneClick/QuickCheck and mutates no config", () =>
{
    var root = FindRepositoryRoot();
    string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    var uiEvents = Read("projects", "XIV-Mini-Util", "Plugin.UiEvents.cs");
    var serviceCtor = Read("projects", "XIV-Mini-Util", "Plugin.ServiceConstruction.cs");
    var lifecycle = Read("projects", "XIV-Mini-Util", "Plugin.Lifecycle.cs");
    var suppression = Read("projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.SceneObjectSuppression.cs");

    // clipboard handoff mirrors the existing auto-check pattern: a Draw handler consuming pending text.
    var handlerWired = uiEvents.Contains("private void CopyPendingFruSelectionChangeDiagnostic()", StringComparison.Ordinal)
        && uiEvents.Contains("TryConsumeFruSelectionChangeClipboardText(out var text)", StringComparison.Ordinal)
        && uiEvents.Contains("ImGui.SetClipboardText(text);", StringComparison.Ordinal)
        && serviceCtor.Contains("_pluginInterface.UiBuilder.Draw += CopyPendingFruSelectionChangeDiagnostic;", StringComparison.Ordinal)
        && lifecycle.Contains("_pluginInterface.UiBuilder.Draw -= CopyPendingFruSelectionChangeDiagnostic;", StringComparison.Ordinal);

    var consumeMirrorsPattern = suppression.Contains("internal bool TryConsumeFruSelectionChangeClipboardText(out string text)", StringComparison.Ordinal)
        && suppression.Contains("_fruSelectionChangePendingClipboardText = string.Empty;", StringComparison.Ordinal);

    // the publish path must not start OneClick / automatic QuickCheck and must not touch config.
    var noOneClickOrQuickCheck = !suppression.Contains("StartQuickCheck(", StringComparison.Ordinal)
        && !suppression.Contains("ArmAutomaticQuickCheck", StringComparison.Ordinal)
        && !suppression.Contains("RequestAutomaticQuickCheck", StringComparison.Ordinal)
        && !suppression.Contains("_automaticCheck", StringComparison.Ordinal);
    var noConfigMutation = !suppression.Contains("_configuration", StringComparison.Ordinal);

    // dedicated file name, not the auto-check report file.
    var dedicatedFile = suppression.Contains("TitleBackgroundSelectionChangeReportBuilder.FileName", StringComparison.Ordinal)
        && !suppression.Contains("TitleBackgroundAutomaticCheckReportBuilder.FileName", StringComparison.Ordinal);

    return handlerWired && consumeMirrorsPattern && noOneClickOrQuickCheck && noConfigMutation && dedicatedFile;
});

Test(595, "Phase A UX: READ-ONLY coverage follow-up runs after the WRITE window closes (not resuming it) and confirms CoverageGap on a whole-pass active->inactive transition", () =>
{
    var k = ((ulong)7u << 32) | 0u;
    const string zon11 = "bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a4_zon11.sgb";
    const string zon12 = "bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a4_zon12.sgb";
    const string rot01 = "bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a4_rot01.sgb";
    var streak = TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState.StableStreakTarget;

    var s = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
    s.ArmForGeneration(5);
    s.NoteSelectionChangeReArm(5, 5, 1000, 0);
    s.ArmForGeneration(5, forceReArm: true);
    s.MarkFirstReArmedPassStarting(0);

    // WRITE window: real-game shape - every deny-matched group already inactive, plus 3 non-deny samples.
    for (var i = 0; i < streak; i++)
    {
        s.BeginPass();
        s.RecordScanned();
        s.RecordMatched(k);
        s.RecordAlreadyInactive(k);
        if (i == 0)
        {
            s.RecordActiveNonDenyKeepPath(zon11);
            s.RecordActiveNonDenyKeepPath(zon12);
            s.RecordActiveNonDenyKeepPath(rot01);
        }
        s.EndPass();
    }

    var writeWindowStable = s.Completed && s.StopReason == "stable" && s.TerminalAtPassCount == streak
        && !s.ShouldRunPass() && s.PassCount == streak
        && s.TotalWriteCalls == 0
        && s.ActiveNonDenyKeepPathSampleCount == 3
        && s.ActiveNonDenyKeepPathResolvedInactiveCount == 0
        && s.SelectionChangeClass == TitleBackgroundSceneObjectSelectionChangeClass.InsufficientEvidence;

    // #1: the WRITE window never resumes. A same-generation re-arm keeps it closed; write counters frozen.
    s.ArmForGeneration(5);
    var writeWindowNotResumed = !s.ShouldRunPass() && s.Completed
        && s.PassCount == streak && s.TotalWriteCalls == 0 && s.StableStreak == streak;

    // #2: the READ-ONLY follow-up window arms and its passes run AFTER Completed.
    var shouldArm = TitleBackgroundCharaSelectSceneObjectSuppressionLogic.ShouldArmCoverageFollowUp(
        s.SelectionChangeReArmCount > 0, s.Completed);
    s.ArmCoverageFollowUp(0);
    var armed = shouldArm && s.CoverageFollowUpArmed && s.CoverageFollowUpActive && !s.CoverageFollowUpTerminal;

    s.RecordCoverageFollowUpElapsed(200);
    s.BeginCoverageFollowUpPass();
    s.RecordNonDenyKeepPathFollowUp(zon11, isActive: true);   // still active
    s.RecordNonDenyKeepPathFollowUp(zon12, isActive: false);  // whole pass saw no active instance -> resolves
    s.RecordNonDenyKeepPathFollowUp(rot01, isActive: true);
    s.EndCoverageFollowUpPass();
    var followUpPassRanAfterClose = s.CoverageFollowUpPassCount == 1
        && s.PassCount == streak            // WRITE pass count untouched
        && s.ActiveNonDenyKeepPathResolvedInactiveCount == 1
        && !s.CoverageFollowUpTerminal
        && s.SelectionChangeClass == TitleBackgroundSceneObjectSelectionChangeClass.CoverageGap;

    // #3: a later pass where the remaining samples are inactive-only resolves them all.
    s.RecordCoverageFollowUpElapsed(700);
    s.BeginCoverageFollowUpPass();
    s.RecordNonDenyKeepPathFollowUp(zon11, isActive: false);
    s.RecordNonDenyKeepPathFollowUp(rot01, isActive: false);
    s.EndCoverageFollowUpPass();
    var allResolved = s.ActiveNonDenyKeepPathResolvedInactiveCount == 3
        && s.SelectionChangeClass == TitleBackgroundSceneObjectSelectionChangeClass.CoverageGap;
    var reportReadyOnCoverageGap = TitleBackgroundCharaSelectSceneObjectSuppressionLogic.SelectionChangeReportReady(
        s.SelectionChangeClass, s.Completed, 999, false, s.ActiveNonDenyKeepPathSampleCount, s.CoverageFollowUpTerminal,
        finalDiagnosticArmed: false, finalDiagnosticComplete: false);

    var lines = s.BuildDiagnosticLines("custom:fru-clear-stage", true).ToArray();
    var diagOk = lines.Contains("fru.suppression.selectionChange.followUp.armed=True")
        && lines.Any(l => l.StartsWith("fru.suppression.selectionChange.followUp.passCount=2", StringComparison.Ordinal))
        && lines.Contains("fru.suppression.selectionChange.followUp.resolvedInactiveCount=3")
        && lines.Any(l => l.StartsWith("fru.suppression.selectionChange.followUp.resolvedPaths=bg/", StringComparison.Ordinal))
        && lines.Contains($"fru.suppression.selectionChange.followUp.durationMs={TitleBackgroundCharaSelectSceneObjectSuppressionLogic.CoverageFollowUpDurationMs}")
        && lines.Contains("fru.suppression.selectionChange.class=CoverageGap");

    return writeWindowStable && writeWindowNotResumed && armed
        && followUpPassRanAfterClose && allResolved && reportReadyOnCoverageGap && diagOk;
});

Test(596, "Phase A UX: coverage follow-up 2500ms timeout leaves class InsufficientEvidence but makes the report ready; session end stops it safely; Reset clears it", () =>
{
    var streak = TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState.StableStreakTarget;
    var dur = TitleBackgroundCharaSelectSceneObjectSuppressionLogic.CoverageFollowUpDurationMs;
    var insufficient = TitleBackgroundSceneObjectSelectionChangeClass.InsufficientEvidence;

    TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState StableWithSamples(int gen)
    {
        var s = new TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState();
        s.ArmForGeneration(gen);
        s.NoteSelectionChangeReArm(gen, gen, 1000, 0);
        s.ArmForGeneration(gen, forceReArm: true);
        s.MarkFirstReArmedPassStarting(0);
        var k = ((ulong)3u << 32) | 0u;
        for (var i = 0; i < streak; i++)
        {
            s.BeginPass();
            s.RecordScanned();
            s.RecordMatched(k);
            s.RecordAlreadyInactive(k);
            if (i == 0)
            {
                s.RecordActiveNonDenyKeepPath("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a4_zon15.sgb");
                s.RecordActiveNonDenyKeepPath("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a4_zon16.sgb");
            }
            s.EndPass();
        }
        return s;
    }

    // --- timeout path: 2500ms with the sampled paths never seen inactive -> InsufficientEvidence, but report ready ---
    var t = StableWithSamples(9);
    t.ArmCoverageFollowUp(0);
    // report is NOT ready while the follow-up window is still running (write-window stable alone is insufficient).
    var notReadyWhileRunning = !TitleBackgroundCharaSelectSceneObjectSuppressionLogic.SelectionChangeReportReady(
        t.SelectionChangeClass, t.Completed, 100_000, false, t.ActiveNonDenyKeepPathSampleCount, t.CoverageFollowUpTerminal,
        finalDiagnosticArmed: false, finalDiagnosticComplete: false);
    for (var i = 1; i <= 3; i++)
    {
        t.RecordCoverageFollowUpElapsed(600 * i);
        t.BeginCoverageFollowUpPass();
        t.RecordNonDenyKeepPathFollowUp("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a4_zon15.sgb", isActive: true);
        t.RecordNonDenyKeepPathFollowUp("bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a4_zon16.sgb", isActive: true);
        t.EndCoverageFollowUpPass();
    }
    t.RecordCoverageFollowUpElapsed(dur);
    t.StopCoverageFollowUp("followup-timeout");
    var timeoutInsufficient = t.ActiveNonDenyKeepPathResolvedInactiveCount == 0
        && t.SelectionChangeClass == insufficient
        && t.CoverageFollowUpTerminal
        && t.CoverageFollowUpStopReason == "followup-timeout"
        && t.CoverageFollowUpElapsedMs == dur
        && t.CoverageFollowUpPassCount == 3;
    var timeoutReportReady = TitleBackgroundCharaSelectSceneObjectSuppressionLogic.SelectionChangeReportReady(
        t.SelectionChangeClass, t.Completed, -1, false, t.ActiveNonDenyKeepPathSampleCount, t.CoverageFollowUpTerminal,
        finalDiagnosticArmed: false, finalDiagnosticComplete: false);

    // --- session end: safe stop + report ready regardless of the follow-up window ---
    var e = StableWithSamples(11);
    e.ArmCoverageFollowUp(0);
    e.RecordCoverageFollowUpElapsed(300);
    e.StopCoverageFollowUp("session-end");
    var sessionEndStopped = !e.CoverageFollowUpActive
        && e.CoverageFollowUpStopReason == "session-end"
        && e.CoverageFollowUpElapsedMs == 300
        && e.CoverageFollowUpTerminal;
    var sessionEndReportReady = TitleBackgroundCharaSelectSceneObjectSuppressionLogic.SelectionChangeReportReady(
        e.SelectionChangeClass, e.Completed, 50, sessionEnding: true, e.ActiveNonDenyKeepPathSampleCount, e.CoverageFollowUpTerminal,
        finalDiagnosticArmed: false, finalDiagnosticComplete: false);

    e.Reset();
    var clearedOnReset = !e.CoverageFollowUpArmed && !e.CoverageFollowUpActive
        && e.CoverageFollowUpStopReason == "not-run" && e.CoverageFollowUpElapsedMs == -1
        && e.CoverageFollowUpPassCount == 0
        && e.BuildDiagnosticLines("custom:n4f4", false).Contains("fru.suppression.selectionChange.followUp.armed=False");

    return notReadyWhileRunning && timeoutInsufficient && timeoutReportReady
        && sessionEndStopped && sessionEndReportReady && clearedOnReset;
});

Test(597, "Phase A UX: coverage follow-up clock advances with no native reads / identity gate; the scan is identity-gated and read-only; WRITE identity gates + SetActive path intact", () =>
{
    var root = FindRepositoryRoot();
    var suppression = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.SceneObjectSuppression.cs"));

    string Body(string signature)
    {
        var start = suppression.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var brace = suppression.IndexOf('{', start);
        if (brace < 0) return string.Empty;
        var depth = 0;
        for (var i = brace; i < suppression.Length; i++)
        {
            if (suppression[i] == '{') depth++;
            else if (suppression[i] == '}' && --depth == 0) return suppression[brace..(i + 1)];
        }
        return string.Empty;
    }

    var advance = Body("private void AdvanceFruCoverageFollowUpClock()");
    var scanPass = Body("private void ScanFruCoverageFollowUpPass(");
    var scanPaths = Body("private void ScanCoverageFollowUpSampledPaths(");

    // #5a: the clock advance has no identity gate and no native instance reads; it times out on its own.
    var clockNoNativeReads = advance.Length > 0
        && !advance.Contains("TryResolveAuthorizedFruActiveLayout", StringComparison.Ordinal)
        && !advance.Contains("GetPrimaryPath", StringComparison.Ordinal)
        && !advance.Contains("IsActive", StringComparison.Ordinal)
        && !advance.Contains("InstancesByType", StringComparison.Ordinal)
        && advance.Contains("Environment.TickCount64", StringComparison.Ordinal)
        && advance.Contains("StopCoverageFollowUp(\"followup-timeout\")", StringComparison.Ordinal);

    // #5b: the scan pass is gated by the shared identity check and bails before any pass begins when it fails.
    var gateIdx = scanPass.IndexOf("RecordGateStatus($\"coverage-followup:{gate}\")", StringComparison.Ordinal);
    var beginIdx = scanPass.IndexOf("BeginCoverageFollowUpPass()", StringComparison.Ordinal);
    var scanGateFirst = scanPass.Length > 0 && gateIdx >= 0 && beginIdx >= 0 && gateIdx < beginIdx
        && scanPass.Contains("TryResolveAuthorizedFruActiveLayout(candidate, out var activeLayout, out var gate)", StringComparison.Ordinal);

    // MUST FIX (review 5098578049): the identity resolve helper call is INSIDE the follow-up scan's
    // exception boundary; on exception it fails closed via RecordFailure + return and does no native write.
    var tryIdx = scanPass.IndexOf("try", StringComparison.Ordinal);
    var resolveIdx = scanPass.IndexOf("TryResolveAuthorizedFruActiveLayout(", StringComparison.Ordinal);
    var catchIdx = scanPass.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
    var scanFailIdx = scanPass.IndexOf("RecordFailure($\"coverage-followup-scan:{ex.GetType().Name}\")", StringComparison.Ordinal);
    var identityInsideExceptionBoundary = scanPass.Length > 0
        && tryIdx >= 0 && resolveIdx > tryIdx && catchIdx > resolveIdx && scanFailIdx > catchIdx
        && scanPass.IndexOf("return;", catchIdx, StringComparison.Ordinal) > scanFailIdx
        && !scanPass.Contains("SetActive", StringComparison.Ordinal);

    // #6 (read-only): the scan only uses GetPrimaryPath + IsActive; never SetActive / deny / write budget.
    var scanReadOnly = scanPaths.Length > 0
        && scanPaths.Contains("GetPrimaryPath()", StringComparison.Ordinal)
        && scanPaths.Contains("instance->IsActive", StringComparison.Ordinal)
        && !scanPaths.Contains("SetActive", StringComparison.Ordinal)
        && !scanPaths.Contains("DenyPathTokens", StringComparison.Ordinal)
        && !scanPaths.Contains("TryConsumeWriteBudget", StringComparison.Ordinal);

    // login / session end stops the follow-up window BEFORE the report is published.
    var stopIdx = suppression.IndexOf("StopCoverageFollowUp(\"session-end\")", StringComparison.Ordinal);
    var publishIdx = suppression.IndexOf("MaybePublishFruSelectionChangeReport(candidate.Id, sessionEnding: true)", StringComparison.Ordinal);
    var sessionEndOrder = stopIdx > 0 && publishIdx > stopIdx;

    // the WRITE window's identity gates + SetActive / write-budget path are unchanged (now via the shared helper).
    var writePathIntact = suppression.Contains("gateStatus = \"active-layout-null\";", StringComparison.Ordinal)
        && suppression.Contains("gateStatus = \"active-layout-not-ready\";", StringComparison.Ordinal)
        && suppression.Contains("gateStatus = \"loaded-layout-territory-mismatch\";", StringComparison.Ordinal)
        && suppression.Contains("gateStatus = \"loaded-layout-layer-mismatch\";", StringComparison.Ordinal)
        && suppression.Contains("gateStatus = \"authorized\";", StringComparison.Ordinal)
        && suppression.Contains("instance->SetActive(false);", StringComparison.Ordinal)
        && suppression.Contains("TryConsumeWriteBudget(key)", StringComparison.Ordinal);

    return clockNoNativeReads && scanGateFirst && identityInsideExceptionBoundary
        && scanReadOnly && sessionEndOrder && writePathIntact;
});

Test(598, "Phase A final diagnostic: keep-token exclusion drops _flo/_lig from the eligible SharedGroup candidate set without changing Evaluate() suppression", () =>
{
    static TitleBackgroundSceneObjectSuppressionVerdict V(string p)
        => TitleBackgroundCharaSelectSceneObjectSuppressionLogic.Evaluate(p, true).Verdict;

    const string flo = "bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a6_flo00.sgb";
    const string lig = "bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a1_lig02.sgb";
    const string zon = "bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a4_zon11.sgb";
    const string gmc = "bg/ex3/01_nvt_n4/shared/for_bg/sgbg_n4gw_a2_gmc01.sgb";

    // Evaluate() suppression semantics are unchanged: gmc still Suppress, flo/lig/zon still Keep.
    var evaluateUnchanged =
        V(gmc) == TitleBackgroundSceneObjectSuppressionVerdict.Suppress
        && V(flo) == TitleBackgroundSceneObjectSuppressionVerdict.Keep
        && V(lig) == TitleBackgroundSceneObjectSuppressionVerdict.Keep
        && V(zon) == TitleBackgroundSceneObjectSuppressionVerdict.Keep;

    // diagnostic-only keep-token detector.
    var keepTokenDetector =
        TitleBackgroundSelectionChangeDeltaLogic.MatchesKnownKeepToken(flo)
        && TitleBackgroundSelectionChangeDeltaLogic.MatchesKnownKeepToken(lig)
        && !TitleBackgroundSelectionChangeDeltaLogic.MatchesKnownKeepToken(zon)
        && !TitleBackgroundSelectionChangeDeltaLogic.MatchesKnownKeepToken("")
        && !TitleBackgroundSelectionChangeDeltaLogic.MatchesKnownKeepToken(null);

    // eligible delta path = SharedGroup + no-deny-token + not a known keep token.
    var eligibility =
        TitleBackgroundCharaSelectSceneObjectSuppressionLogic.IsEligibleDeltaSharedGroupPath(zon)
        && !TitleBackgroundCharaSelectSceneObjectSuppressionLogic.IsEligibleDeltaSharedGroupPath(flo)
        && !TitleBackgroundCharaSelectSceneObjectSuppressionLogic.IsEligibleDeltaSharedGroupPath(lig)
        && !TitleBackgroundCharaSelectSceneObjectSuppressionLogic.IsEligibleDeltaSharedGroupPath(gmc)   // deny-covered
        && !TitleBackgroundCharaSelectSceneObjectSuppressionLogic.IsEligibleDeltaSharedGroupPath("plugin/local/x.sgb"); // not a game asset

    return evaluateUnchanged && keepTokenDetector && eligibility;
});

Test(599, "Phase A final diagnostic: SharedGroup count-delta tracker detects 2->1, 0->1->0, appear/disappear, same-count stability; a partial pass never synthesises disappearance", () =>
{
    static TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState Armed()
    {
        var d = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
        // pre-re-arm ordinary pass establishes the baseline: pathA=2 active, pathB=0 active (present), pathC absent.
        d.BeginSharedGroupPass();
        d.RecordSharedGroupInstance("bg/a/pa.sgb", true);
        d.RecordSharedGroupInstance("bg/a/pa.sgb", true);
        d.RecordSharedGroupInstance("bg/a/pb.sgb", false);
        d.FinishSharedGroupPass(valid: true, elapsedMs: -1);
        d.ArmFromReArm(System.Array.Empty<TitleBackgroundVfxDetailEntry>(), vfxSnapshotReliable: true);
        return d;
    }

    // pathA 2 -> 1 (count delta), pathB 0 -> 1 -> 0 (transient), pathC appears (0 -> 1), pathD stable (never present).
    var d1 = Armed();
    // pass 1: A=1, B=1, C=1
    d1.BeginSharedGroupPass();
    d1.RecordSharedGroupInstance("bg/a/pa.sgb", true);
    d1.RecordSharedGroupInstance("bg/a/pb.sgb", true);
    d1.RecordSharedGroupInstance("bg/a/pc.sgb", true);
    d1.FinishSharedGroupPass(valid: true, elapsedMs: 100);
    // pass 2: A=1, B=0, C=1
    d1.BeginSharedGroupPass();
    d1.RecordSharedGroupInstance("bg/a/pa.sgb", true);
    d1.RecordSharedGroupInstance("bg/a/pb.sgb", false);
    d1.RecordSharedGroupInstance("bg/a/pc.sgb", true);
    d1.FinishSharedGroupPass(valid: true, elapsedMs: 200);
    d1.MarkWindowComplete();

    var lines1 = d1.BuildFinalDiagnosticLines().ToArray();
    // A (2->1), B (0->1->0) and C (appeared) all changed; tracked >= 3.
    var deltasDetected = d1.SharedGroupChangedCount == 3
        && d1.SharedGroupValidPassCount == 2
        && lines1.Any(l => l.Contains("path=bg/a/pa.sgb baseline=2", StringComparison.Ordinal) && l.Contains("min=1", StringComparison.Ordinal))
        && lines1.Any(l => l.Contains("path=bg/a/pb.sgb baseline=0", StringComparison.Ordinal) && l.Contains("max=1", StringComparison.Ordinal) && l.Contains("final=0", StringComparison.Ordinal))
        && lines1.Any(l => l.Contains("path=bg/a/pc.sgb baseline=0", StringComparison.Ordinal) && l.Contains("final=1", StringComparison.Ordinal))
        && lines1.Contains("fru.suppression.selectionChange.final.outcome=sharedgroup-delta");

    // same-count stability: baseline pathA=2 stays 2 for two passes -> no change.
    var d2 = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    d2.BeginSharedGroupPass();
    d2.RecordSharedGroupInstance("bg/a/pa.sgb", true);
    d2.RecordSharedGroupInstance("bg/a/pa.sgb", true);
    d2.FinishSharedGroupPass(valid: true, elapsedMs: -1);
    d2.ArmFromReArm(System.Array.Empty<TitleBackgroundVfxDetailEntry>(), vfxSnapshotReliable: true);
    for (var i = 0; i < 3; i++)
    {
        d2.BeginSharedGroupPass();
        d2.RecordSharedGroupInstance("bg/a/pa.sgb", true);
        d2.RecordSharedGroupInstance("bg/a/pa.sgb", true);
        d2.FinishSharedGroupPass(valid: true, elapsedMs: 100 * i);
    }
    d2.MarkWindowComplete();
    var stableNoDelta = d2.SharedGroupChangedCount == 0
        && d2.SharedGroupValidPassCount == 3;

    // disappearance: baseline pathA present, then a COMPLETE valid pass with no pathA -> current 0 -> changed.
    var d3 = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    d3.BeginSharedGroupPass();
    d3.RecordSharedGroupInstance("bg/a/pa.sgb", true);
    d3.FinishSharedGroupPass(valid: true, elapsedMs: -1);
    d3.ArmFromReArm(System.Array.Empty<TitleBackgroundVfxDetailEntry>(), vfxSnapshotReliable: true);
    d3.BeginSharedGroupPass();               // valid pass, pathA not observed
    d3.FinishSharedGroupPass(valid: true, elapsedMs: 50);
    var disappearanceDetected = d3.SharedGroupChangedCount == 1
        && d3.BuildFinalDiagnosticLines().Any(l => l.Contains("path=bg/a/pa.sgb baseline=1", StringComparison.Ordinal) && l.Contains("final=0", StringComparison.Ordinal));

    // a partial/failed pass must NOT synthesise disappearance (valid:false is dropped).
    var d4 = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    d4.BeginSharedGroupPass();
    d4.RecordSharedGroupInstance("bg/a/pa.sgb", true);
    d4.FinishSharedGroupPass(valid: true, elapsedMs: -1);
    d4.ArmFromReArm(System.Array.Empty<TitleBackgroundVfxDetailEntry>(), vfxSnapshotReliable: true);
    d4.BeginSharedGroupPass();
    d4.FinishSharedGroupPass(valid: false, elapsedMs: 50);   // partial scan -> ignored
    var partialNoSynthesis = d4.SharedGroupChangedCount == 0 && d4.SharedGroupValidPassCount == 0;

    return deltasDetected && stableNoDelta && disappearanceDetected && partialNoSynthesis;
});

Test(600, "Phase A final diagnostic: typed VFX delta compares managed snapshots and detects appear/disappear + active/loaded/gfx/path changes; a partial pass never synthesises disappearance", () =>
{
    static TitleBackgroundVfxDetailEntry E(uint key, bool active, bool loaded, bool gfx, uint hash)
        => new(((ulong)key << 32), key, 0u,
            TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(key, 0u),
            active, loaded, gfx, hash, $"bg/vfx/{key}.avfx");

    var baseline = new[]
    {
        E(1, active: true, loaded: true, gfx: true, hash: 0xAAu),   // will change active
        E(2, active: false, loaded: true, gfx: true, hash: 0xBBu),  // will change loaded + gfx + path
        E(3, active: true, loaded: true, gfx: true, hash: 0xCCu),   // will disappear
        E(4, active: true, loaded: true, gfx: true, hash: 0xDDu),   // unchanged
    };

    var d = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    d.ArmFromReArm(baseline, vfxSnapshotReliable: true);

    d.BeginVfxPass();
    d.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(1, 0u), isActive: false, loaded: true, gfx: true, pathHash: 0xAAu, "bg/vfx/1.avfx");
    d.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(2, 0u), isActive: false, loaded: false, gfx: false, pathHash: 0x99u, "bg/vfx/2b.avfx");
    d.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(4, 0u), isActive: true, loaded: true, gfx: true, pathHash: 0xDDu, "bg/vfx/4.avfx");
    d.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(5, 0u), isActive: true, loaded: true, gfx: true, pathHash: 0xEEu, "bg/vfx/5.avfx"); // appeared
    d.FinishVfxPass(valid: true);
    d.MarkWindowComplete();

    var lines = d.BuildFinalDiagnosticLines().ToArray();
    // uuid1 active changed, uuid2 loaded+gfx+path changed, uuid3 disappeared, uuid5 appeared; uuid4 unchanged.
    var detected = d.VfxChangedCount == 4
        && d.VfxValidPassCount == 1
        && d.VfxBaselineCount == 4
        && lines.Contains("fru.suppression.selectionChange.final.vfxChangedCount=4")
        && lines.Any(l => l.Contains($"uuid={TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(1, 0u)} change=active", StringComparison.Ordinal))
        && lines.Any(l => l.Contains("change=loaded+gfx+path", StringComparison.Ordinal))
        && lines.Any(l => l.Contains($"uuid={TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(3, 0u)} change=disappeared", StringComparison.Ordinal))
        && lines.Any(l => l.Contains($"uuid={TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(5, 0u)} change=appeared", StringComparison.Ordinal));

    // partial VFX pass -> no disappearance synthesised.
    var d2 = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    d2.ArmFromReArm(baseline, vfxSnapshotReliable: true);
    d2.BeginVfxPass();
    d2.FinishVfxPass(valid: false);
    var partialNoSynthesis = d2.VfxChangedCount == 0 && d2.VfxValidPassCount == 0;

    return detected && partialNoSynthesis;
});

Test(601, "Phase A final diagnostic: outcome precedence SharedGroup > VFX > no-safe-layout-delta > incomplete; report keys come from the single-source list", () =>
{
    // (sg, vfx, complete, sgPasses, vfxPasses, readFail, gateBlocked, sgBaseline, vfxBaseline)
    static string O(int sg, int vfx, bool complete, int sgPasses, int vfxPasses, int readFail,
        int gateBlocked = 0, bool sgBaseline = true, bool vfxBaseline = true)
        => TitleBackgroundSelectionChangeDeltaLogic.ClassifyFinalOutcome(
            sg, vfx, complete, sgPasses, vfxPasses, readFail, gateBlocked, sgBaseline, vfxBaseline);

    var precedence =
        O(1, 1, true, 5, 5, 0) == "sharedgroup-delta"          // SG wins even if VFX also changed
        && O(0, 1, false, 0, 0, 0) == "vfx-delta"              // VFX wins when no SG delta
        && O(0, 0, true, 3, 3, 0) == "no-safe-layout-delta"    // full valid observation, nothing changed
        && O(0, 0, true, 0, 3, 0) == "incomplete"              // no valid SG pass
        && O(0, 0, true, 3, 0, 0) == "incomplete"              // no valid VFX pass
        && O(0, 0, true, 3, 3, 2) == "incomplete"              // read failures
        && O(0, 0, false, 3, 3, 0) == "incomplete"             // window not complete
        && O(0, 0, true, 3, 3, 0, gateBlocked: 1) == "incomplete"          // MUST FIX #1: gate-blocked negative
        && O(0, 0, true, 3, 3, 0, sgBaseline: false) == "incomplete"       // SG baseline not available
        && O(0, 0, true, 3, 3, 0, vfxBaseline: false) == "incomplete";     // VFX baseline not available

    var d = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    d.ArmFromReArm(System.Array.Empty<TitleBackgroundVfxDetailEntry>(), vfxSnapshotReliable: true);
    d.MarkWindowComplete();
    var emitted = d.BuildFinalDiagnosticLines().Select(l => l[..l.IndexOf('=')]).ToArray();
    var everyEmittedListed = emitted.All(k => TitleBackgroundSelectionChangeDeltaLogic.DiagnosticKeys.Contains(k));
    var hasCoreKeys = emitted.Contains("fru.suppression.selectionChange.final.outcome")
        && emitted.Contains("fru.suppression.selectionChange.final.armed")
        && emitted.Contains("fru.suppression.selectionChange.final.complete")
        && emitted.Contains("fru.suppression.selectionChange.final.sharedGroupChangedCount")
        && emitted.Contains("fru.suppression.selectionChange.final.vfxChangedCount")
        && emitted.Contains("fru.suppression.selectionChange.final.readFailureCount")
        && emitted.Contains("fru.suppression.selectionChange.final.gateBlockedPassCount")
        && emitted.Contains("fru.suppression.selectionChange.final.sharedGroupBaselineAvailable")
        && emitted.Contains("fru.suppression.selectionChange.final.vfxBaselineAvailable");
    // an unarmed tracker reports armed=False / outcome=not-run and Reset clears it.
    d.Reset();
    var resetClears = d.BuildFinalDiagnosticLines().Contains("fru.suppression.selectionChange.final.armed=False")
        && d.BuildFinalDiagnosticLines().Contains("fru.suppression.selectionChange.final.outcome=not-run")
        && !d.Armed && !d.Complete;

    return precedence && everyEmittedListed && hasCoreKeys && resetClears;
});

Test(602, "Phase A final diagnostic: the VFX delta scan uses only the verified typed reads and no write; the WRITE suppression path and its identity gates are unchanged", () =>
{
    var root = FindRepositoryRoot();
    var suppression = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.SceneObjectSuppression.cs"));

    string Body(string signature)
    {
        var start = suppression.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var brace = suppression.IndexOf('{', start);
        if (brace < 0) return string.Empty;
        var depth = 0;
        for (var i = brace; i < suppression.Length; i++)
        {
            if (suppression[i] == '{') depth++;
            else if (suppression[i] == '}' && --depth == 0) return suppression[brace..(i + 1)];
        }
        return string.Empty;
    }

    var vfxScan = Body("private bool ScanSelectionChangeDeltaVfx(");
    var sgScan = Body("private bool ScanSelectionChangeDeltaSharedGroups(");

    // VFX delta scan: only the same typed reads ScanVfxInventory uses; absolutely no write ops.
    var vfxTypedReadOnly = vfxScan.Length > 0
        && vfxScan.Contains("instance->SubId", StringComparison.Ordinal)
        && vfxScan.Contains("instance->Id.InstanceKey", StringComparison.Ordinal)
        && vfxScan.Contains("instance->IsActive", StringComparison.Ordinal)
        && vfxScan.Contains("instance->GetPrimaryPath()", StringComparison.Ordinal)
        && vfxScan.Contains("instance->IsPrimaryLoaded()", StringComparison.Ordinal)
        && vfxScan.Contains("instance->GetGraphics() != null", StringComparison.Ordinal)
        && !vfxScan.Contains("SetActive", StringComparison.Ordinal)
        && !vfxScan.Contains("vfunc", StringComparison.Ordinal)
        && !vfxScan.Contains("Marshal.Write", StringComparison.Ordinal)
        && !vfxScan.Contains("->TriggerIndex", StringComparison.Ordinal);

    // SG delta scan: read-only (GetPrimaryPath + IsActive), no write / deny mutation.
    var sgReadOnly = sgScan.Length > 0
        && sgScan.Contains("instance->GetPrimaryPath()", StringComparison.Ordinal)
        && sgScan.Contains("instance->IsActive", StringComparison.Ordinal)
        && !sgScan.Contains("SetActive", StringComparison.Ordinal)
        && !sgScan.Contains("TryConsumeWriteBudget", StringComparison.Ordinal);

    // WRITE suppression path unchanged: deny match -> RecordMatched -> budget -> SetActive(false) -> readback.
    var writeSuppress = Body("private bool SuppressSharedGroups(");
    var writePathIntact = writeSuppress.Contains("RecordMatched(key)", StringComparison.Ordinal)
        && writeSuppress.Contains("TryConsumeWriteBudget(key)", StringComparison.Ordinal)
        && writeSuppress.Contains("instance->SetActive(false);", StringComparison.Ordinal)
        && writeSuppress.Contains("RecordConfirmedInactive(key)", StringComparison.Ordinal)
        // identity gate helper still holds the four ordered checks + authorized.
        && suppression.Contains("gateStatus = \"active-layout-null\";", StringComparison.Ordinal)
        && suppression.Contains("gateStatus = \"active-layout-not-ready\";", StringComparison.Ordinal)
        && suppression.Contains("gateStatus = \"loaded-layout-territory-mismatch\";", StringComparison.Ordinal)
        && suppression.Contains("gateStatus = \"loaded-layout-layer-mismatch\";", StringComparison.Ordinal);

    return vfxTypedReadOnly && sgReadOnly && writePathIntact;
});

Test(603, "MUST FIX (review 5098946239 #1): multiple re-arms during one switch burst keep the final-diagnostic baseline and accumulated SG/VFX delta", () =>
{
    static TitleBackgroundVfxDetailEntry V(uint key, bool active)
        => new(((ulong)key << 32), key, 0u,
            TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(key, 0u),
            active, true, true, 0xAAu, $"bg/vfx/{key}.avfx");

    var d = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    // pre-burst ordinary pass: pathA=2 active. VFX baseline: uuid1 active.
    d.BeginSharedGroupPass();
    d.RecordSharedGroupInstance("bg/a/pa.sgb", true);
    d.RecordSharedGroupInstance("bg/a/pa.sgb", true);
    d.FinishSharedGroupPass(valid: true, elapsedMs: -1);
    var vfxBaseline = new[] { V(1, active: true) };

    // first re-arm of the burst -> baseline snapshot.
    d.ArmFromReArm(vfxBaseline, vfxSnapshotReliable: true);

    // an early delta pass while the burst is still firing: pathA 2 -> 1, uuid1 active -> inactive.
    d.BeginSharedGroupPass();
    d.RecordSharedGroupInstance("bg/a/pa.sgb", true);
    d.FinishSharedGroupPass(valid: true, elapsedMs: 30);
    d.BeginVfxPass();
    d.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(1, 0u), isActive: false, loaded: true, gfx: true, pathHash: 0xAAu, "bg/vfx/1.avfx");
    d.FinishVfxPass(valid: true);

    var earlyDelta = d.SharedGroupChangedCount == 1 && d.VfxChangedCount == 1
        && d.SharedGroupValidPassCount == 1 && d.VfxValidPassCount == 1;

    // subsequent re-arms in the SAME burst (Armed && !Complete) are no-ops: baseline + accumulated delta survive.
    d.ArmFromReArm(new[] { V(1, active: true), V(9, active: true) }, vfxSnapshotReliable: true);   // would have added uuid9 / re-baselined
    d.ArmFromReArm(System.Array.Empty<TitleBackgroundVfxDetailEntry>(), vfxSnapshotReliable: true); // would have wiped the VFX baseline
    var survivedReArms = d.SharedGroupChangedCount == 1
        && d.VfxChangedCount == 1
        && d.SharedGroupValidPassCount == 1
        && d.VfxValidPassCount == 1
        && d.VfxBaselineCount == 1;   // still just uuid1, not re-snapshot to 2 or 0

    // after the window is terminal, a genuinely new switch DOES re-baseline.
    d.MarkWindowComplete();
    d.ArmFromReArm(new[] { V(1, active: true), V(2, active: true) }, vfxSnapshotReliable: true);
    var reBaselineAfterComplete = !d.WindowComplete && d.VfxBaselineCount == 2
        && d.SharedGroupChangedCount == 0 && d.VfxChangedCount == 0;

    return earlyDelta && survivedReArms && reBaselineAfterComplete;
});

Test(604, "MUST FIX (review 5098946239 #2): a pass with any per-instance read failure is diagnostic-invalid (no baseline->0 / no disappeared); a later clean pass still detects a real delta", () =>
{
    static TitleBackgroundVfxDetailEntry V(uint key)
        => new(((ulong)key << 32), key, 0u,
            TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(key, 0u),
            true, true, true, 0xAAu, $"bg/vfx/{key}.avfx");

    // --- SharedGroup: read failure in a pass must not turn baseline pathA into 0 ---
    var sg = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    sg.BeginSharedGroupPass();
    sg.RecordSharedGroupInstance("bg/a/pa.sgb", true);
    sg.FinishSharedGroupPass(valid: true, elapsedMs: -1);
    sg.ArmFromReArm(System.Array.Empty<TitleBackgroundVfxDetailEntry>(), vfxSnapshotReliable: true);

    // failed pass: the scan hit a per-instance read failure and pathA was not observed.
    sg.BeginSharedGroupPass();
    sg.RecordReadFailure();                       // one per-instance read failure this pass
    sg.FinishSharedGroupPass(valid: true, elapsedMs: 50);   // caller thinks the map iterated
    var sgFailedPassDropped = sg.SharedGroupChangedCount == 0
        && sg.SharedGroupValidPassCount == 0
        && sg.ReadFailureCount == 1;

    // later clean pass: pathA genuinely 2 -> 1 (well, 1 -> ... baseline is 1) drops to 0 -> real delta.
    sg.BeginSharedGroupPass();                    // clean pass, pathA absent -> current 0
    sg.FinishSharedGroupPass(valid: true, elapsedMs: 120);
    var laterCleanSgDelta = sg.SharedGroupValidPassCount == 1
        && sg.SharedGroupChangedCount == 1
        && sg.BuildFinalDiagnosticLines().Any(l => l.Contains("path=bg/a/pa.sgb baseline=1", StringComparison.Ordinal) && l.Contains("final=0", StringComparison.Ordinal));

    // --- VFX: read failure in a pass must not turn a baseline uuid into "disappeared" ---
    var vfx = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    vfx.ArmFromReArm(new[] { V(1), V(2) }, vfxSnapshotReliable: true);
    vfx.BeginVfxPass();
    vfx.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(1, 0u), isActive: true, loaded: true, gfx: true, pathHash: 0xAAu, "bg/vfx/1.avfx");
    vfx.RecordReadFailure();                      // uuid2's read threw
    vfx.FinishVfxPass(valid: true);
    var vfxFailedPassDropped = vfx.VfxChangedCount == 0
        && vfx.VfxValidPassCount == 0
        && vfx.ReadFailureCount == 1;

    // later clean pass: uuid2 genuinely gone -> disappeared detected.
    vfx.BeginVfxPass();
    vfx.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(1, 0u), isActive: true, loaded: true, gfx: true, pathHash: 0xAAu, "bg/vfx/1.avfx");
    vfx.FinishVfxPass(valid: true);
    var laterCleanVfxDelta = vfx.VfxValidPassCount == 1
        && vfx.VfxChangedCount == 1
        && vfx.BuildFinalDiagnosticLines().Any(l =>
            l.Contains($"uuid={TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(2, 0u)} change=disappeared", StringComparison.Ordinal));

    // the per-pass flag is scoped to the pass: a subsequent Begin*Pass clears it.
    var flagScoped = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    flagScoped.ArmFromReArm(System.Array.Empty<TitleBackgroundVfxDetailEntry>(), vfxSnapshotReliable: true);
    flagScoped.BeginSharedGroupPass();
    flagScoped.RecordReadFailure();
    flagScoped.FinishSharedGroupPass(valid: true, elapsedMs: 10);   // dropped
    flagScoped.BeginSharedGroupPass();
    flagScoped.RecordSharedGroupInstance("bg/a/px.sgb", true);       // clean pass
    flagScoped.FinishSharedGroupPass(valid: true, elapsedMs: 20);
    var flagCleared = flagScoped.SharedGroupValidPassCount == 1;

    return sgFailedPassDropped && laterCleanSgDelta && vfxFailedPassDropped
        && laterCleanVfxDelta && flagCleared;
});

Test(605, "MUST FIX (review 5099109840): baseline availability is explicit - no baseline -> no positive delta -> incomplete; a valid empty SG baseline still yields a real appearance delta; gate-blocked negative -> incomplete", () =>
{
    static TitleBackgroundVfxDetailEntry E(uint key)
        => new(((ulong)key << 32), key, 0u,
            TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(key, 0u),
            true, true, true, 0xAAu, $"bg/vfx/{key}.avfx");

    // --- no baseline at all (no valid ordinary SG pass, no reliable VFX snapshot): a post-event
    //     entry must NOT become a positive delta, and the outcome must be incomplete ---
    var nb = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    nb.ArmFromReArm(new[] { E(1) }, vfxSnapshotReliable: false);   // VFX snapshot present but NOT reliable
    var baselineFlags = !nb.SharedGroupBaselineAvailable && !nb.VfxBaselineAvailable;
    nb.BeginSharedGroupPass();
    nb.RecordSharedGroupInstance("bg/a/pnew.sgb", true);           // "new" path, but we have no baseline
    nb.FinishSharedGroupPass(valid: true, elapsedMs: 20);
    nb.BeginVfxPass();
    nb.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(1, 0u), isActive: false, loaded: true, gfx: true, pathHash: 0xAAu, "bg/vfx/1.avfx"); // would be "active" change
    nb.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(7, 0u), isActive: true, loaded: true, gfx: true, pathHash: 0xBBu, "bg/vfx/7.avfx");   // would be "appeared"
    nb.FinishVfxPass(valid: true);
    nb.MarkWindowComplete();
    var noBaselineNoPositiveDelta = nb.SharedGroupChangedCount == 0
        && nb.VfxChangedCount == 0
        && nb.Outcome == "incomplete"
        && nb.BuildFinalDiagnosticLines().Contains("fru.suppression.selectionChange.final.sharedGroupBaselineAvailable=False")
        && nb.BuildFinalDiagnosticLines().Contains("fru.suppression.selectionChange.final.vfxBaselineAvailable=False");

    // --- a VALID but empty SG baseline (a valid ordinary pass ran and saw no eligible path):
    //     a subsequent appearance IS a real delta ---
    var eb = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    eb.BeginSharedGroupPass();
    eb.FinishSharedGroupPass(valid: true, elapsedMs: -1);          // valid ordinary pass, zero eligible paths
    eb.ArmFromReArm(System.Array.Empty<TitleBackgroundVfxDetailEntry>(), vfxSnapshotReliable: false);
    var emptyButAvailable = eb.SharedGroupBaselineAvailable;
    eb.BeginSharedGroupPass();
    eb.RecordSharedGroupInstance("bg/a/pappear.sgb", true);
    eb.FinishSharedGroupPass(valid: true, elapsedMs: 40);
    var emptyBaselineRealDelta = emptyButAvailable
        && eb.SharedGroupChangedCount == 1
        && eb.BuildFinalDiagnosticLines().Any(l => l.Contains("path=bg/a/pappear.sgb baseline=0", StringComparison.Ordinal) && l.Contains("final=1", StringComparison.Ordinal));

    // --- gate-blocked negative run: valid scans, no delta, but gateBlockedPassCount > 0 -> incomplete ---
    var gb = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    gb.BeginSharedGroupPass();
    gb.FinishSharedGroupPass(valid: true, elapsedMs: -1);
    gb.ArmFromReArm(new[] { E(1) }, vfxSnapshotReliable: true);
    gb.BeginSharedGroupPass();
    gb.RecordSharedGroupInstance("bg/a/px.sgb", false);           // no delta
    gb.FinishSharedGroupPass(valid: true, elapsedMs: 30);
    gb.BeginVfxPass();
    gb.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(1, 0u), isActive: true, loaded: true, gfx: true, pathHash: 0xAAu, "bg/vfx/1.avfx"); // no change
    gb.FinishVfxPass(valid: true);
    gb.RecordGateBlockedPass();                                    // a frame where identity was not safe
    gb.MarkWindowComplete();
    var gateBlockedIncomplete = gb.SharedGroupChangedCount == 0
        && gb.VfxChangedCount == 0
        && gb.SharedGroupValidPassCount > 0
        && gb.VfxValidPassCount > 0
        && gb.GateBlockedPassCount == 1
        && gb.Outcome == "incomplete";

    // --- the same run with NO gate-blocked pass reaches no-safe-layout-delta (control) ---
    var ok = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    ok.BeginSharedGroupPass();
    ok.FinishSharedGroupPass(valid: true, elapsedMs: -1);
    ok.ArmFromReArm(new[] { E(1) }, vfxSnapshotReliable: true);
    ok.BeginSharedGroupPass();
    ok.RecordSharedGroupInstance("bg/a/px.sgb", false);
    ok.FinishSharedGroupPass(valid: true, elapsedMs: 30);
    ok.BeginVfxPass();
    ok.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(1, 0u), isActive: true, loaded: true, gfx: true, pathHash: 0xAAu, "bg/vfx/1.avfx");
    ok.FinishVfxPass(valid: true);
    ok.MarkWindowComplete();
    var cleanNegativeIsNoSafeDelta = ok.Outcome == "no-safe-layout-delta";

    return baselineFlags && noBaselineNoPositiveDelta && emptyBaselineRealDelta
        && gateBlockedIncomplete && cleanNegativeIsNoSafeDelta;
});

Test(606, "MUST FIX (review 5099281460 #1): RecordGateBlockedPass only counts while Armed && !Complete, and any counted gate-block keeps a negative run at incomplete", () =>
{
    // guard: unarmed -> no-op.
    var unarmed = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    unarmed.RecordGateBlockedPass();
    var unarmedNoCount = unarmed.GateBlockedPassCount == 0;

    // armed && !complete -> counts.
    var armed = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    armed.BeginSharedGroupPass();
    armed.FinishSharedGroupPass(valid: true, elapsedMs: -1);
    armed.ArmFromReArm(System.Array.Empty<TitleBackgroundVfxDetailEntry>(), vfxSnapshotReliable: true);
    armed.RecordGateBlockedPass();
    armed.RecordGateBlockedPass();
    var armedCounts = armed.GateBlockedPassCount == 2;

    // after the window is terminal -> no-op (a stale gate-block frame must not inflate the count).
    armed.MarkWindowComplete();
    armed.RecordGateBlockedPass();
    var terminalNoCount = armed.GateBlockedPassCount == 2;

    // an otherwise-clean negative run with a counted gate-block ends at incomplete (not no-safe-layout-delta).
    var run = new TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState();
    run.BeginSharedGroupPass();
    run.FinishSharedGroupPass(valid: true, elapsedMs: -1);
    run.ArmFromReArm(
        new[]
        {
            new TitleBackgroundVfxDetailEntry((1UL << 32), 1u, 0u,
                TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(1, 0u),
                true, true, true, 0xAAu, "bg/vfx/1.avfx"),
        },
        vfxSnapshotReliable: true);
    run.RecordGateBlockedPass();                       // a safety-gate return frame while observing
    run.BeginSharedGroupPass();
    run.RecordSharedGroupInstance("bg/a/px.sgb", false);
    run.FinishSharedGroupPass(valid: true, elapsedMs: 40);
    run.BeginVfxPass();
    run.RecordVfxInstance(TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(1, 0u), isActive: true, loaded: true, gfx: true, pathHash: 0xAAu, "bg/vfx/1.avfx");
    run.FinishVfxPass(valid: true);
    run.MarkWindowComplete();
    var negativeWithGateBlockIsIncomplete = run.SharedGroupChangedCount == 0
        && run.VfxChangedCount == 0
        && run.SharedGroupValidPassCount > 0
        && run.VfxValidPassCount > 0
        && run.GateBlockedPassCount == 1
        && run.Outcome == "incomplete"
        && run.BuildFinalDiagnosticLines().Contains("fru.suppression.selectionChange.final.gateBlockedPassCount=1");

    return unarmedNoCount && armedCounts && terminalNoCount && negativeWithGateBlockIsIncomplete;
});

Test(607, "MUST FIX (review 5099281460): every safety-gate return during the final diagnostic calls RecordGateBlockedPass; vfxSnapshotReliable requires the strict predicate; no new native scan / wait loop; WRITE path intact", () =>
{
    var root = FindRepositoryRoot();
    var src = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.SceneObjectSuppression.cs"));

    string Body(string signature)
    {
        var start = src.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var brace = src.IndexOf('{', start);
        if (brace < 0) return string.Empty;
        var depth = 0;
        for (var i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src[brace..(i + 1)];
        }
        return string.Empty;
    }

    var maintain = Body("private void MaintainFruSceneObjectSuppression()");

    // #1: each safety gate that RecordGateStatus(...)s and returns also records a gate-blocked pass.
    static bool GatePairsWithBlock(string body, string gateStatusToken)
    {
        var g = body.IndexOf(gateStatusToken, StringComparison.Ordinal);
        if (g < 0) return false;
        var ret = body.IndexOf("return;", g, StringComparison.Ordinal);
        var block = body.IndexOf("_charaSelectSelectionChangeDelta.RecordGateBlockedPass();", g, StringComparison.Ordinal);
        return ret >= 0 && block >= 0 && block < ret;
    }

    var gatesBlockCounted =
        GatePairsWithBlock(maintain, "RecordGateStatus(\"session-or-hook-not-ready\")")
        && GatePairsWithBlock(maintain, "RecordGateStatus(\"not-chara-select-map\")")
        && GatePairsWithBlock(maintain, "RecordGateStatus(\"scene-generation-not-observed\")")
        && GatePairsWithBlock(maintain, "$\"scene-not-authorized")
        && GatePairsWithBlock(maintain, "RecordGateStatus(writeGateStatus)");
    // the follow-up scan already records a gate-block on identity-gate failure.
    var followUpBlocks = Body("private void ScanFruCoverageFollowUpPass(")
        .Contains("_charaSelectSelectionChangeDelta.RecordGateBlockedPass();", StringComparison.Ordinal);

    // safety gate conditions themselves are unchanged.
    var gateConditionsIntact = maintain.Contains("!_charaSelectTitleBackgroundSessionActive", StringComparison.Ordinal)
        && maintain.Contains("_hookLifecycle.State != TitleBackgroundServiceState.Ready", StringComparison.Ordinal)
        && maintain.Contains("TitleBackgroundCharaSelectPlacementLogic.IsCharaSelectMap(lobbyMap)", StringComparison.Ordinal)
        && maintain.Contains("if (generation <= 0)", StringComparison.Ordinal)
        && maintain.Contains("_charaSelectStaticAnchor.TryGetAuthorizedAnchor(", StringComparison.Ordinal);

    // #2: strict vfxSnapshotReliable predicate.
    var strictReliable = maintain.Contains("vfxSnapshotReliable: _charaSelectVfxInventory.ArmedSceneGeneration == generation", StringComparison.Ordinal)
        && maintain.Contains("_charaSelectVfxInventory.Completed", StringComparison.Ordinal)
        && maintain.Contains("string.Equals(_charaSelectVfxInventory.StopReason, \"stable\", StringComparison.Ordinal)", StringComparison.Ordinal)
        && maintain.Contains("_charaSelectVfxInventory.ReadFailureCount == 0", StringComparison.Ordinal)
        && maintain.Contains("_charaSelectVfxInventory.DetailSnapshotCount > 0", StringComparison.Ordinal);

    // no new native scan / wait loop introduced by this change: the maintain method has no loop of its own
    // and does not touch InstancesByType (only the pre-existing scan helpers do).
    var noWaitLoopOrExtraScan = !maintain.Contains("while (", StringComparison.Ordinal)
        && !maintain.Contains("InstancesByType", StringComparison.Ordinal)
        && !maintain.Contains("GetPrimaryPath", StringComparison.Ordinal);

    // WRITE path unchanged.
    var writePathIntact = Body("private bool SuppressSharedGroups(")
            is var w
        && w.Contains("instance->SetActive(false);", StringComparison.Ordinal)
        && w.Contains("TryConsumeWriteBudget(key)", StringComparison.Ordinal)
        && w.Contains("RecordMatched(key)", StringComparison.Ordinal);

    return gatesBlockCounted && followUpBlocks && gateConditionsIntact
        && strictReliable && noWaitLoopOrExtraScan && writePathIntact;
});

    }
}
