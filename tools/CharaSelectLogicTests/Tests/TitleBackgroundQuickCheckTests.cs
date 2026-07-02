// Path: tools/CharaSelectLogicTests/Tests/TitleBackgroundQuickCheckTests.cs
// Description: Registers regression tests for the TitleBackgroundQuickCheck responsibility
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
    private static void AddTitleBackgroundQuickCheckTests(List<LogicTestCase> tests)
    {
        void Test(int order, string name, Func<bool> assertion) =>
            tests.Add(new LogicTestCase(order, name, assertion));

Test(37, "title background quickcheck detail lines include state flags", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        titleBackgroundOverrideEnabled: true,
        titleBackgroundCameraOverrideEnabled: true,
        legacySceneCompositionEnabled: false,
        integratedCompositionEnabled: true,
        shouldArmAdapter: true));
    return result.DetailLines.Any(l => l == "quickCheck.titleBackgroundOverrideEnabled=True")
        && result.DetailLines.Any(l => l == "quickCheck.titleBackgroundCameraOverrideEnabled=True")
        && result.DetailLines.Any(l => l == "quickCheck.legacySceneCompositionEnabled=False")
        && result.DetailLines.Any(l => l == "quickCheck.integratedCompositionEnabled=True")
        && result.DetailLines.Any(l => l == "quickCheck.shouldArmAdapter=True")
        && result.DetailLines.Any(l => l.StartsWith("quickCheck.shouldArmAdapter.reason=", StringComparison.Ordinal));
});

Test(56, "title background quickcheck ok accepts known background-only limitations", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        expectedBrightness: TitleBackgroundCharacterSelectExpectedBrightness.Dark,
        characterExpectedVisible: false,
        characterObserved: "not-observed",
        actorSourceAmbiguous: true,
        objectTableZeroTransformStubs: true));

    return result.Level is TitleBackgroundQuickCheckLevel.OK
        && !string.IsNullOrWhiteSpace(result.Reason)
        && result.Reason.Contains("background-only works", StringComparison.Ordinal);
});

Test(57, "title background quickcheck warns on sceneReady multiple", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(sceneReadyAcceptedCount: 2));
    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Warnings.Any(warning => warning.Contains("sceneReady accepted multiple", StringComparison.Ordinal))
        && result.Reason != "background was not applied";
});

Test(58, "title background quickcheck dark brightness is not ng", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        expectedBrightness: TitleBackgroundCharacterSelectExpectedBrightness.Dark));
    return result.Level is TitleBackgroundQuickCheckLevel.OK
        && result.DetailLines.Any(line => line == "knownLimitation.brightnessDark=True");
});

Test(59, "title background quickcheck warns when login not checked", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        isLoggedIn: false,
        charaSelectObserved: true,
        runState: TitleBackgroundQuickCheckRunState.CharaSelectObserved));
    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Reason.Contains("login transition has not been checked", StringComparison.Ordinal)
        && result.LoginTransitionStatus == "not checked";
});

Test(60, "title background quickcheck ng when candidate missing", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(candidateId: string.Empty));
    return result.Level == TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("candidate", StringComparison.Ordinal);
});

Test(61, "title background quickcheck ng when background not applied", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        backgroundApplied: false,
        backgroundObserved: false,
        overrideAppliedCount: 0));
    return result.Level == TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("not applied", StringComparison.Ordinal);
});

Test(62, "title background quickcheck ng when post login scene override remains active", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        sceneOverrideActiveAfterLogin: true,
        activeAfterLoginDetected: true));
    return result.Level == TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("leak", StringComparison.Ordinal);
});

Test(63, "title background quickcheck ng when phase2g applied after login", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(phase2GAppliedAfterLogin: true));
    return result.Level == TitleBackgroundQuickCheckLevel.NG
        && result.Reason.Contains("Phase2G", StringComparison.Ordinal);
});

Test(64, "title background quickcheck known limitations are not ng", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        expectedBrightness: TitleBackgroundCharacterSelectExpectedBrightness.Dark,
        characterExpectedVisible: false,
        characterObserved: "hidden",
        actorSourceAmbiguous: true,
        objectTableZeroTransformStubs: true));
    return result.Level == TitleBackgroundQuickCheckLevel.OK;
});

Test(65, "title background quickcheck adapter stopping after login is not ng", () =>
{
    return TitleBackgroundQuickCheckEvaluator.IsSafeAfterLoginAdapterState("Stopping")
        && !TitleBackgroundQuickCheckEvaluator.IsUnsafeAfterLoginAdapterState("Stopping");
});

Test(66, "title background quickcheck run-scoped ignores historical last override", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        overrideAppliedCount: 0,
        backgroundApplied: false,
        backgroundObserved: false));
    return result.Level != TitleBackgroundQuickCheckLevel.OK
        && result.Reason.Contains("background was not applied", StringComparison.Ordinal);
});

Test(67, "title background quickcheck started while already logged in is warn", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        startedLoggedIn: true,
        charaSelectObserved: false,
        runState: TitleBackgroundQuickCheckRunState.Armed));
    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Reason.Contains("already logged in", StringComparison.Ordinal)
        && result.Level != TitleBackgroundQuickCheckLevel.OK;
});

Test(68, "title background quickcheck proper clean flow is ok", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        runScoped: true,
        startedLoggedIn: false,
        charaSelectObserved: true,
        runState: TitleBackgroundQuickCheckRunState.LoggedInObserved,
        overrideAppliedCount: 1,
        isLoggedIn: true));
    return result.Level == TitleBackgroundQuickCheckLevel.OK;
});

Test(69, "title background quickcheck warns when character visible top-down with default framing", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterVisualStatus: TitleBackgroundCharacterVisualStatus.VisibleTopDown,
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.Default));
    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Warnings.Any(w => w.Contains("framing needs adjustment", StringComparison.Ordinal))
        && result.NextAction.Contains("Lower camera", StringComparison.Ordinal);
});

Test(70, "title background quickcheck warns when character visible top-down with non-default framing", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterVisualStatus: TitleBackgroundCharacterVisualStatus.VisibleTopDown,
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.LowerCamera));
    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Warnings.Any(w => w.Contains("framing needs adjustment", StringComparison.Ordinal))
        && result.NextAction.Contains("still needs tuning", StringComparison.Ordinal);
});

Test(71, "title background quickcheck warns when character visible too small with default framing", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterVisualStatus: TitleBackgroundCharacterVisualStatus.VisibleButTooSmall,
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.Default));
    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.NextAction.Contains("Lower camera", StringComparison.Ordinal);
});

Test(72, "title background quickcheck warns when character not visible in frame", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterVisualStatus: TitleBackgroundCharacterVisualStatus.NotVisible));
    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Warnings.Any(w => w.Contains("not visible or offscreen", StringComparison.Ordinal))
        && result.NextAction.Contains("try another camera framing", StringComparison.Ordinal);
});

Test(73, "title background quickcheck warns when character offscreen", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterVisualStatus: TitleBackgroundCharacterVisualStatus.Offscreen));
    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.NextAction.Contains("try another camera framing", StringComparison.Ordinal);
});

Test(74, "title background quickcheck detail lines include framing offset and profile source", () =>
{
    var defaultResult = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.Default));
    var lowerResult = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.LowerCamera));
    var candidateResult = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        candidateRecommendedFraming: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended));
    return defaultResult.DetailLines.Any(l => l == "camera.framingOffset=0.000")
        && defaultResult.DetailLines.Any(l => l == "camera.profileSource=default")
        && lowerResult.DetailLines.Any(l => l == "camera.framingOffset=-0.300")
        && lowerResult.DetailLines.Any(l => l == "camera.profileSource=user-selected")
        && candidateResult.DetailLines.Any(l => l == "camera.profileSource=candidate-recommended")
        && defaultResult.DetailLines.Any(l => l.StartsWith("camera.recommendedFraming=", StringComparison.Ordinal))
        && defaultResult.DetailLines.Any(l => l.StartsWith("camera.recommendedAction=", StringComparison.Ordinal));
});

Test(75, "title background quickcheck top-down does not become ng when background is safe", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterVisualStatus: TitleBackgroundCharacterVisualStatus.VisibleTopDown,
        sceneOverrideActiveAfterLogin: false,
        activeAfterLoginDetected: false,
        phase2GAppliedAfterLogin: false));
    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Level != TitleBackgroundQuickCheckLevel.NG;
});

Test(76, "title background quickcheck ui simple mode is bounded", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundLastQuickCheckResult = TitleBackgroundQuickCheckLevel.OK,
        TitleBackgroundLastQuickCheckCandidateId = "custom:n4f4",
        TitleBackgroundLastQuickCheckReason = "background-only works",
    };
    var items = TitleBackgroundQuickCheckUiPresenter.GetSimpleModeItems(configuration);
    return items.Contains("Character Select Background")
        && items.Contains("Status")
        && items.Contains("Automatic Check")
        && items.Contains("Copy Last Report")
        && !items.Contains("Start QuickCheck")
        && !items.Contains("Run Check")
        && !items.Contains("Capture legacy visible camera")
        && !items.Contains("Save as n4f4 visible profile")
        && !items.Contains("Clear captured profile")
        && !items.Contains("raw bridge diagnostics")
        && !items.Contains("raw camera delta")
        && !TitleBackgroundQuickCheckUiPresenter.IsExperimentalModeVisibleInSimple(TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground);
});

Test(79, "title background selection does not start a hidden quickcheck", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(
            Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground"),
            "TitleScreenBackgroundService*.cs").Select(File.ReadAllText));
    var setup = ExtractMethodBody(serviceText, "internal TitleBackgroundSimpleUiSummary RunSimpleAutoSetup()");

    return setup.Contains("ApplySimpleAutoSetup()", StringComparison.Ordinal)
        && !setup.Contains("StartQuickCheck()", StringComparison.Ordinal);
});

Test(81, "title background automatic check report is ready to paste", () =>
{
    var report = TitleBackgroundAutomaticCheckReportBuilder.Build(
        new DateTimeOffset(2026, 6, 22, 21, 30, 0, TimeSpan.FromHours(9)),
        ["[XMU QuickCheck] WARN", "Background: applied"],
        ["sceneReadySignal.acceptedCount=1", "transition.verdict.loginTransitionSafety=safe"]);

    return report.Contains("Title Background automatic check", StringComparison.Ordinal)
        && report.Contains("runId=none", StringComparison.Ordinal)
        && report.Contains("completion=complete", StringComparison.Ordinal)
        && report.Contains("--- QuickCheck ---", StringComparison.Ordinal)
        && report.Contains("[XIV Mini Util] Background: applied", StringComparison.Ordinal)
        && report.Contains("--- Diagnostic ---", StringComparison.Ordinal)
        && report.Contains("[XIV Mini Util] transition.verdict.loginTransitionSafety=safe", StringComparison.Ordinal);
});

Test(83, "automatic check settings snapshot restores every temporary setup field", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundOverrideEnabled = false,
        TitleBackgroundCameraOverrideEnabled = false,
        TitleBackgroundIntegratedCompositionEnabled = false,
        CharaSelectSceneCompositionEnabled = true,
        TitleBackgroundSelectedPresetId = "original-preset",
        TitleBackgroundCharacterSelectOverrideCandidateId = "original-candidate",
        TitleBackgroundTerritoryPath = "original/path",
        TitleBackgroundTerritoryTypeId = 123,
        TitleBackgroundLayoutTerritoryTypeId = 124,
        TitleBackgroundLayoutLayerFilterKey = 125,
        TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.ResolveOnly,
        TitleBackgroundCharacterSelectBackgroundMode = TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly,
        TitleBackgroundCharacterSelectLightingMode = TitleBackgroundCharacterSelectLightingMode.DiagnosticsOnly,
        TitleBackgroundCharaSelectCameraFramingMode = TitleBackgroundCharaSelectCameraFramingMode.Default,
        TitleBackgroundFixOnPassiveObservationEnabled = true,
        TitleBackgroundFixOnFocusAnchorOverrideEnabled = false,
        TitleBackgroundCharaSelectAnchorEnabled = true,
        TitleBackgroundCharaSelectAnchorCandidateId = "anchor-candidate",
        TitleBackgroundCharaSelectAnchorX = 1,
        TitleBackgroundCharaSelectAnchorY = 2,
        TitleBackgroundCharaSelectAnchorZ = 3,
        TitleBackgroundCharaSelectAnchorRotation = 4,
        TitleBackgroundCharaSelectAnchorFrame = "world",
        TitleBackgroundCharaSelectViewEnabled = true,
        TitleBackgroundCharaSelectViewCandidateId = "view-candidate",
        TitleBackgroundCharaSelectViewCameraX = 5,
        TitleBackgroundCharaSelectViewCameraY = 6,
        TitleBackgroundCharaSelectViewCameraZ = 7,
        TitleBackgroundCharaSelectViewFocusX = 8,
        TitleBackgroundCharaSelectViewFocusY = 9,
        TitleBackgroundCharaSelectViewFocusZ = 10,
        TitleBackgroundCharaSelectViewFovY = 1.1f,
    };
    var snapshot = TitleBackgroundAutomaticCheckSettingsSnapshot.Capture(configuration);

    TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(configuration);
    configuration.CharaSelectSceneCompositionEnabled = false;
    configuration.TitleBackgroundFixOnPassiveObservationEnabled = false;
    configuration.TitleBackgroundCharaSelectAnchorEnabled = false;
    configuration.TitleBackgroundCharaSelectViewEnabled = false;
    configuration.TitleBackgroundCharaSelectViewCameraX = 0;
    snapshot.ApplyTo(configuration);

    return configuration.TitleBackgroundOverrideEnabled == false
        && configuration.TitleBackgroundCameraOverrideEnabled == false
        && configuration.TitleBackgroundIntegratedCompositionEnabled == false
        && configuration.CharaSelectSceneCompositionEnabled
        && configuration.TitleBackgroundSelectedPresetId == "original-preset"
        && configuration.TitleBackgroundCharacterSelectOverrideCandidateId == "original-candidate"
        && configuration.TitleBackgroundTerritoryPath == "original/path"
        && configuration.TitleBackgroundTerritoryTypeId == 123
        && configuration.TitleBackgroundLayoutTerritoryTypeId == 124
        && configuration.TitleBackgroundLayoutLayerFilterKey == 125
        && configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.ResolveOnly
        && configuration.TitleBackgroundCharacterSelectBackgroundMode == TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly
        && configuration.TitleBackgroundCharacterSelectLightingMode == TitleBackgroundCharacterSelectLightingMode.DiagnosticsOnly
        && configuration.TitleBackgroundCharaSelectCameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.Default
        && configuration.TitleBackgroundFixOnPassiveObservationEnabled
        && !configuration.TitleBackgroundFixOnFocusAnchorOverrideEnabled
        && configuration.TitleBackgroundCharaSelectAnchorEnabled
        && configuration.TitleBackgroundCharaSelectAnchorCandidateId == "anchor-candidate"
        && configuration.TitleBackgroundCharaSelectAnchorX == 1
        && configuration.TitleBackgroundCharaSelectAnchorY == 2
        && configuration.TitleBackgroundCharaSelectAnchorZ == 3
        && configuration.TitleBackgroundCharaSelectAnchorRotation == 4
        && configuration.TitleBackgroundCharaSelectAnchorFrame == "world"
        && configuration.TitleBackgroundCharaSelectViewEnabled
        && configuration.TitleBackgroundCharaSelectViewCandidateId == "view-candidate"
        && configuration.TitleBackgroundCharaSelectViewCameraX == 5
        && configuration.TitleBackgroundCharaSelectViewCameraY == 6
        && configuration.TitleBackgroundCharaSelectViewCameraZ == 7
        && configuration.TitleBackgroundCharaSelectViewFocusX == 8
        && configuration.TitleBackgroundCharaSelectViewFocusY == 9
        && configuration.TitleBackgroundCharaSelectViewFocusZ == 10
        && Math.Abs(configuration.TitleBackgroundCharaSelectViewFovY - 1.1f) < 0.0001f;
});

Test(84, "automatic check recovery journal round trips original settings", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundOverrideEnabled = true,
        TitleBackgroundCharacterSelectOverrideCandidateId = "custom:n4f4",
        TitleBackgroundLayoutLayerFilterKey = 51,
    };
    var journal = TitleBackgroundAutomaticCheckRecoveryJournal.Create(
        "run-123",
        new DateTimeOffset(2026, 6, 27, 12, 0, 0, TimeSpan.FromHours(9)),
        configuration);
    var restored = TitleBackgroundAutomaticCheckRecoveryJournal.Deserialize(
        TitleBackgroundAutomaticCheckRecoveryJournal.Serialize(journal));

    return restored != null
        && restored.SchemaVersion == TitleBackgroundAutomaticCheckRecoveryJournal.CurrentSchemaVersion
        && restored.RunId == "run-123"
        && restored.OriginalSettings.OverrideEnabled
        && restored.OriginalSettings.CandidateId == "custom:n4f4"
        && restored.OriginalSettings.LayoutLayerFilterKey == 51;
});

Test(85, "automatic check restore reapplies chara select runtime state", () =>
{
    var root = FindRepositoryRoot();
    var quickCheckSource = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleScreenBackgroundService.QuickCheck.cs"));
    var charaSelectSource = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "CharaSelect",
        "CharaSelectService.SceneComposition.cs"));

    return quickCheckSource.Contains(
            "_charaSelectService?.ReapplyCompositionRuntimeStateFromConfiguration()",
            StringComparison.Ordinal)
        && charaSelectSource.Contains(
            "internal void ReapplyCompositionRuntimeStateFromConfiguration()",
            StringComparison.Ordinal)
        && charaSelectSource.Contains("ApplySceneCompositionRuntimeState()", StringComparison.Ordinal)
        && charaSelectSource.Contains(
            "ApplyTitleBackgroundCharacterCompositionBridgeRuntimeState()",
            StringComparison.Ordinal);
});

Test(86, "automatic check report distinguishes settings restore and runtime reload", () =>
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleScreenBackgroundService.QuickCheck.cs"));

    return source.Contains("settingsRestored={restoreResult.SettingsRestored}", StringComparison.Ordinal)
        && source.Contains("runtimeReloaded={restoreResult.RuntimeReloaded}", StringComparison.Ordinal)
        && source.Contains(
            "new AutomaticCheckRestoreResult(true, false)",
            StringComparison.Ordinal);
});

Test(88, "title background automatic check times out only after login", () =>
{
    var now = new DateTimeOffset(2026, 6, 22, 21, 30, 10, TimeSpan.FromHours(9));
    var loginObservedAt = now - TitleBackgroundAutomaticCheckLogic.LoginTransitionTimeout;

    return !TitleBackgroundAutomaticCheckLogic.ShouldForcePartialCompletion(
            TitleBackgroundAutomaticCheckState.Collecting,
            isLoggedIn: false,
            loginObservedAt,
            now)
        && !TitleBackgroundAutomaticCheckLogic.ShouldForcePartialCompletion(
            TitleBackgroundAutomaticCheckState.Collecting,
            isLoggedIn: true,
            loginObservedAt.AddMilliseconds(1),
            now)
        && TitleBackgroundAutomaticCheckLogic.ShouldForcePartialCompletion(
            TitleBackgroundAutomaticCheckState.Collecting,
            isLoggedIn: true,
            loginObservedAt,
            now);
});

Test(90, "title background quickcheck prioritizes unresolved native character source", () =>
{
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterExpectedVisible: false,
        actorSourceAmbiguous: true,
        objectTableZeroTransformStubs: true,
        cameraFramingMode: TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        cameraProfileSource: "candidate",
        cameraFramesCharacter: "False",
        cameraVisibleProfileResolved: false,
        cameraVisibleProfileApplied: false));

    return result.Level == TitleBackgroundQuickCheckLevel.WARN
        && result.Reason == "native character source is unresolved"
        && result.NextAction.Contains("automatically copied report", StringComparison.Ordinal)
        && !result.NextAction.Contains("legacy", StringComparison.OrdinalIgnoreCase);
});

Test(91, "title background automatic check completes and queues clipboard on framework update", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground"), "TitleScreenBackgroundService*.cs").Select(File.ReadAllText));
    var pluginText = string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(
            Path.Combine(root, "projects", "XIV-Mini-Util"),
            "Plugin*.cs").Select(File.ReadAllText));
    var update = ExtractMethodBody(serviceText, "private void UpdateAutomaticQuickCheck()");
    var complete = ExtractMethodBody(serviceText, "private void CompleteAutomaticQuickCheck(bool partial)");

    return update.Contains("LoggedInObserved", StringComparison.Ordinal)
        && update.Contains("CompleteAutomaticQuickCheck(forcePartial)", StringComparison.Ordinal)
        && complete.Contains("GetDiagnosticLines(automaticInvocation: true)", StringComparison.Ordinal)
        && complete.Contains("PublishAutomaticCheckReport(report, \"complete\")", StringComparison.Ordinal)
        && pluginText.Contains("ImGui.SetClipboardText(text)", StringComparison.Ordinal)
        && pluginText.Contains("CopyPendingTitleBackgroundAutomaticCheckReport", StringComparison.Ordinal);
});

Test(95, "title background quickcheck suppresses unresolved reason when character composited", () =>
{
    var resolved = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterExpectedVisible: false,
        actorSourceAmbiguous: true,
        objectTableZeroTransformStubs: true,
        characterCompositedApplied: true));

    return resolved.Reason != "native character source is unresolved"
        && resolved.Reason != "captured legacy visible camera profile is missing"
        && !resolved.Warnings.Any(warning => warning.Contains("captured legacy visible camera profile", StringComparison.Ordinal))
        && !resolved.NextAction.Contains("native character source investigation", StringComparison.Ordinal)
        && !resolved.NextAction.Contains("legacy shooting composition", StringComparison.Ordinal);
});

Test(100, "automatic check sceneReady verdict ignores historical cumulative count", () =>
{
    // 累積3回受理でも current run の開始時点が2なら run-scoped は1回 → 複数受理ではない。
    var runScopedCount = TitleBackgroundAutomaticCheckLogic.ResolveVerdictSceneReadyAcceptedCount(
        automaticInvocation: true,
        runScopedActive: true,
        cumulativeAcceptedCount: 3,
        runStartAcceptedCount: 2);
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(
        summary,
        lastOverrideApplied: true,
        transitionSafety: "safe",
        sceneReadyAcceptedMultipleTimes: runScopedCount > 1);

    return runScopedCount == 1
        && delivery.Safety.Verdict == "safe"
        && delivery.TransitionSafetyVerdict == "safe"
        && delivery.NextAction != "unsafe-stop"
        && delivery.DeliveryVerdict != "unsafe";
});

Test(101, "automatic check sceneReady verdict keeps warning when current run accepts multiple times", () =>
{
    // current run 内で2回受理されたら run-scoped でも複数受理 → 安全側の警告を維持する。
    var runScopedCount = TitleBackgroundAutomaticCheckLogic.ResolveVerdictSceneReadyAcceptedCount(
        automaticInvocation: true,
        runScopedActive: true,
        cumulativeAcceptedCount: 4,
        runStartAcceptedCount: 2);
    var summary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(
    [
        CharacterPlacementFrameFromCandidates([]),
    ]);
    var delivery = Delivery(
        summary,
        lastOverrideApplied: true,
        transitionSafety: "safe",
        sceneReadyAcceptedMultipleTimes: runScopedCount > 1);

    return runScopedCount == 2
        && delivery.Safety.Verdict == "warning"
        && delivery.Safety.Reason == "scene-ready-accepted-multiple-times"
        && delivery.Safety.BlocksBackgroundCandidatePromotion
        && delivery.TransitionSafetyVerdict == "warning-scene-ready-accepted-multiple-times";
});

Test(112, "automatic check preserves an enabled focus override", () =>
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleScreenBackgroundService.QuickCheck.cs"));
    var prepare = ExtractMethodBody(source, "private void PrepareAutomaticQuickCheckDiagnostics()");

    return prepare.Contains(
            "TitleBackgroundFixOnFocusAnchorOverrideEnabled",
            StringComparison.Ordinal)
        && !prepare.Contains(
            "TitleBackgroundFixOnFocusAnchorOverrideEnabled = false",
            StringComparison.Ordinal);
});

Test(117, "title background simple check summarizes quickcheck levels", () =>
{
    var ok = TitleBackgroundQuickCheckUiPresenter.BuildSimpleCheckResultLine(
        TitleBackgroundQuickCheckLevel.OK,
        "n4f4 background is working");
    var warn = TitleBackgroundQuickCheckUiPresenter.BuildSimpleCheckResultLine(
        TitleBackgroundQuickCheckLevel.WARN,
        "camera does not frame the character");
    var ng = TitleBackgroundQuickCheckUiPresenter.BuildSimpleCheckResultLine(
        TitleBackgroundQuickCheckLevel.NG,
        "candidate is not selected");

    return ok.StartsWith("OK:", StringComparison.Ordinal)
        && warn.StartsWith("WARN:", StringComparison.Ordinal)
        && warn.Contains("camera", StringComparison.Ordinal)
        && ng.StartsWith("NG:", StringComparison.Ordinal);
});

Test(118, "title background simple status reflects quickcheck ok warn ng", () =>
{
    static Configuration Config(TitleBackgroundQuickCheckLevel level, string reason) => new()
    {
        TitleBackgroundOverrideEnabled = true,
        TitleBackgroundCameraOverrideEnabled = true,
        TitleBackgroundIntegratedCompositionEnabled = true,
        TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.CharaSelectOnly,
        TitleBackgroundCharacterSelectOverrideCandidateId = "custom:n4f4",
        TitleBackgroundCharaSelectCameraFramingMode = TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended,
        TitleBackgroundLastQuickCheckResult = level,
        TitleBackgroundLastQuickCheckReason = reason,
    };

    var ok = TitleBackgroundQuickCheckUiPresenter.BuildSimpleSummary(Config(TitleBackgroundQuickCheckLevel.OK, "visible"));
    var warn = TitleBackgroundQuickCheckUiPresenter.BuildSimpleSummary(Config(TitleBackgroundQuickCheckLevel.WARN, "camera does not frame the character"));
    var ng = TitleBackgroundQuickCheckUiPresenter.BuildSimpleSummary(Config(TitleBackgroundQuickCheckLevel.NG, "setup failed"));

    return ok.Status == TitleBackgroundSimpleUiStatus.Working
        && ok.ResultLine.StartsWith("OK:", StringComparison.Ordinal)
        && warn.Status == TitleBackgroundSimpleUiStatus.Failed
        && warn.ResultLine.StartsWith("WARN:", StringComparison.Ordinal)
        && ng.Status == TitleBackgroundSimpleUiStatus.Failed
        && ng.ResultLine.StartsWith("NG:", StringComparison.Ordinal);
});

Test(120, "title background quickcheck advanced mode does not duplicate candidate selector", () =>
{
    var advancedItems = TitleBackgroundQuickCheckUiPresenter.GetAdvancedModeItems(new Configuration());
    return !advancedItems.Contains("Background Candidate")
        && advancedItems.Contains("Effective Candidate Details");
});

Test(121, "title background quickcheck candidate label includes useful metadata", () =>
{
    var candidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault();
    var label = TitleBackgroundQuickCheckUiPresenter.BuildCandidateLabel(candidate);
    return label.Contains(candidate.Id, StringComparison.Ordinal)
        && label.Contains(candidate.DisplayName, StringComparison.Ordinal)
        && label.Contains("Verified", StringComparison.Ordinal)
        && label.Contains(candidate.ExpectedBrightness.ToString(), StringComparison.Ordinal);
});

Test(122, "title background quickcheck status summary reflects ok warn ng", () =>
{
    var ok = TitleBackgroundQuickCheckUiPresenter.BuildSummary(new Configuration
    {
        TitleBackgroundLastQuickCheckResult = TitleBackgroundQuickCheckLevel.OK,
        TitleBackgroundLastQuickCheckReason = "background-only works",
    });
    var warn = TitleBackgroundQuickCheckUiPresenter.BuildSummary(new Configuration
    {
        TitleBackgroundLastQuickCheckResult = TitleBackgroundQuickCheckLevel.WARN,
        TitleBackgroundLastQuickCheckReason = "brightness is dark",
    });
    var ng = TitleBackgroundQuickCheckUiPresenter.BuildSummary(new Configuration
    {
        TitleBackgroundLastQuickCheckResult = TitleBackgroundQuickCheckLevel.NG,
        TitleBackgroundLastQuickCheckReason = "candidate is not selected",
    });

    return ok.StatusLine.Contains("OK", StringComparison.Ordinal)
        && warn.StatusLine.Contains("WARN", StringComparison.Ordinal)
        && ng.StatusLine.Contains("NG", StringComparison.Ordinal);
});

Test(123, "title background quickcheck background mode labels are user facing", () =>
{
    return TitleBackgroundQuickCheckUiPresenter.GetBackgroundModeUiLabel(TitleBackgroundCharacterSelectBackgroundMode.Disabled) == "Off"
        && TitleBackgroundQuickCheckUiPresenter.GetBackgroundModeUiLabel(TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly) == "Background only"
        && TitleBackgroundQuickCheckUiPresenter.GetBackgroundModeUiLabel(TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly).Contains("recommended", StringComparison.Ordinal)
        && TitleBackgroundQuickCheckUiPresenter.GetBackgroundModeUiLabel(TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground).Contains("Experimental", StringComparison.Ordinal);
});

Test(361, "automatic check does not force passive when view override is configured", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground"), "TitleScreenBackgroundService*.cs").Select(File.ReadAllText));
    var body = ExtractMethodBody(serviceText, "private void PrepareAutomaticQuickCheckDiagnostics()");
    return body.Contains("TitleBackgroundCharaSelectViewEnabled", StringComparison.Ordinal);
});

Test(380, "recovery snapshot round-trips world experimental fields", () =>
{
    var source = new Configuration
    {
        TitleBackgroundCharaSelectAnchorTerritoryTypeId = 816,
        TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = true,
    };
    var snapshot = TitleBackgroundAutomaticCheckSettingsSnapshot.Capture(source);
    var dest = new Configuration();
    snapshot.ApplyTo(dest);
    return dest.TitleBackgroundCharaSelectAnchorTerritoryTypeId == 816
        && dest.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled;
});

Test(400, "automatic report queues clipboard before best-effort file persistence", () =>
{
    var body = ReadServiceMethodBody(
        "TitleScreenBackgroundService.QuickCheck.cs",
        "private void PublishAutomaticCheckReport(string report, string context)");
    var clipboardIndex = body.IndexOf("_automaticCheck.PendingClipboardText = report", StringComparison.Ordinal);
    var fileWriteIndex = body.IndexOf("File.WriteAllText(", StringComparison.Ordinal);
    return clipboardIndex >= 0
        && fileWriteIndex >= 0
        && clipboardIndex < fileWriteIndex
        && body.Contains("catch (Exception ex)", StringComparison.Ordinal);
});

Test(402, "automatic failures always publish a minimal clipboard fallback", () =>
{
    var completion = ReadServiceMethodBody(
        "TitleScreenBackgroundService.QuickCheck.cs",
        "private void CompleteAutomaticQuickCheck(bool partial)");
    var oneClickFailure = ReadServiceMethodBody(
        "TitleScreenBackgroundService.OneClickVerification.cs",
        "private void EmitOneClickFailureReport(string reason, string detail)");
    var fallback = ReadServiceMethodBody(
        "TitleScreenBackgroundService.QuickCheck.cs",
        "private string BuildAutomaticCheckFailureFallback(string reason, string detail)");
    return completion.Contains("BuildAutomaticCheckFailureFallback(\"completion-exception\"", StringComparison.Ordinal)
        && oneClickFailure.Contains("BuildAutomaticCheckFailureFallback(reason", StringComparison.Ordinal)
        && fallback.Contains("completion=failed", StringComparison.Ordinal)
        && fallback.Contains("result=FAILED", StringComparison.Ordinal)
        && fallback.Contains("runId=", StringComparison.Ordinal);
});

    }
}
