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
    // a signature resolver. Character placement writes (the n4f4 compositing path) are
    // intentionally allowed via the dedicated TrySetCurrentCharacterDrawPosition method.
    return statsIndex >= 0 && nativeIndex > statsIndex
        && gateIndex >= 0 && captureIndex > gateIndex
        && probe.Contains("CharaSelectCharacterList.GetCurrentCharacter()", StringComparison.Ordinal)
        && !probe.Contains("TitleBackgroundAddressResolver", StringComparison.Ordinal)
        && probe.Contains("TrySetCurrentCharacterDrawPosition", StringComparison.Ordinal);
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

Test(176, "title background generated curve self-test treats final yaw pitch distance mismatch as non-blocking", () =>
{
    return TitleBackgroundCameraProbeReport.IsGeneratedCurveSelfTestSuccess(
        sceneVerdict: "observed",
        generatedCurveOverrideVerdict: "observed",
        finalLookAtYMatchesGeneratedCurveVerdict: "observed",
        finalYawPitchDistanceMatchesPresetVerdict: "not-observed");
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
        && delivery.OverrideCandidate.Available.Count == 2
        && delivery.OverrideCandidate.Available[0].Id == "custom:n4f4";
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

Test(247, "title background phase2q docs mention xmutbgdiag after login", () =>
{
    var root = FindRepositoryRoot();
    var text = File.ReadAllText(Path.Combine(root, "docs", "title-background-character-select-bright-candidates.md"));

    return text.Contains("`/xmutbgdiag` cannot be run from Character Select", StringComparison.Ordinal)
        && text.Contains("Run `/xmutbgdiag` after login", StringComparison.Ordinal)
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
    var diagText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.TitleBackgroundDiagnostics.cs"));
    var quickCheckText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.QuickCheck.cs"));
    var completeBody = ExtractMethodBody(quickCheckText, "private void CompleteAutomaticQuickCheck(bool partial)");
    // 2026-07-03: 実機3点検証(残差0.002、world/lobby恒等)を経て PersistentApplyEnabled は解禁(true)。
    // Evaluate gate を通った場合のみ通常セッションでも適用される。手動の「立ち位置保存」ボタン(UI操作)は
    // 依然として出さない(UI操作数4の契約維持)。永続化は run 成功時の自動保存のみが経路。
    // 保存処理は settings snapshot 復元の後・reload の前に走ること、保存条件に run-scoped placement count と
    // source 検証が含まれることをソース文字列検査でロックする。
    return TitleBackgroundExperimentalWorldPlacementLogic.PersistentApplyEnabled
        && !normal.Contains("DrawTitleBackgroundSimpleStandingPositionButton", StringComparison.Ordinal)
        && !diagText.Contains("DrawTitleBackgroundSimpleStandingPositionButton", StringComparison.Ordinal)
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
        && body.Contains("TitleBackgroundEnvironmentNoonWriter.TryApplyNoon()", StringComparison.Ordinal);
});

Test(441, "environment noon toggle is absent from the normal title background screen", () =>
{
    var root = FindRepositoryRoot();
    var normalText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.TitleBackground.cs"));
    var diagnosticsText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.TitleBackgroundDiagnostics.cs"));
    return !normalText.Contains("TitleBackgroundEnvironmentNoonEnabled", StringComparison.Ordinal)
        && diagnosticsText.Contains("TitleBackgroundEnvironmentNoonEnabled", StringComparison.Ordinal);
});

Test(442, "environment noon writer only writes DayTimeSeconds to the noon constant", () =>
{
    var root = FindRepositoryRoot();
    var writerText = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundEnvironmentNoonWriter.cs"));
    var body = ExtractMethodBody(writerText, "public static bool TryApplyNoon()");
    return body.Contains("manager->DayTimeSeconds = NoonDayTimeSeconds", StringComparison.Ordinal)
        && !body.Contains("ActiveWeather", StringComparison.Ordinal)
        && !body.Contains("EnvState", StringComparison.Ordinal)
        && !body.Contains("Exposure", StringComparison.Ordinal);
});

    }
}
