// Path: tools/CharaSelectLogicTests/Tests/TitleBackgroundColdStartDiagnosticTests.cs
// Description: Cold-start Character Select Phase A diagnostic の純粋判定と安全契約を固定する
// Reason: 診断自身が再現条件を正規化せず、owner migration gap を証拠から分類できることを最小テストする
using XivMiniUtil;
using XivMiniUtil.Services.TitleBackground;

internal static partial class TestRunner
{
    private static void AddTitleBackgroundColdStartDiagnosticTests(List<LogicTestCase> tests)
    {
        void Test(int order, string name, Func<bool> assertion) =>
            tests.Add(new LogicTestCase(order, name, assertion));

        Test(640, "cold-start snapshot identifies stale FRU v2 owner without mutating config", () =>
        {
            var configuration = new Configuration();
            TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(
                configuration,
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId);

            // Reproduce the pre-PR#4 saved owner state without changing candidate/scene metadata.
            configuration.TitleBackgroundV2Enabled = true;
            configuration.TitleBackgroundCharaSelectPlacementEnabled = false;
            configuration.TitleBackgroundCharaSelectPlacementCandidateId = string.Empty;
            configuration.TitleBackgroundCharaSelectPlacementPositionCaptured = false;

            var beforeV2 = configuration.TitleBackgroundV2Enabled;
            var beforePlacement = configuration.TitleBackgroundCharaSelectPlacementEnabled;
            var beforePositionCaptured = configuration.TitleBackgroundCharaSelectPlacementPositionCaptured;
            var snapshot = TitleBackgroundColdStartDiagnosticLogic.CaptureOwnerSnapshot(configuration);

            return snapshot.CandidateId == TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId
                && snapshot.ActualOwner == "v2"
                && snapshot.ExpectedOwner == "placement"
                && configuration.TitleBackgroundV2Enabled == beforeV2
                && configuration.TitleBackgroundCharaSelectPlacementEnabled == beforePlacement
                && configuration.TitleBackgroundCharaSelectPlacementPositionCaptured == beforePositionCaptured;
        });

        Test(641, "cold-start diagnosis prioritizes confirmed owner migration gap", () =>
        {
            var before = new TitleBackgroundColdStartOwnerSnapshot(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                true,
                true,
                false,
                string.Empty,
                false,
                "v2",
                "placement");
            var after = before;
            var input = new TitleBackgroundColdStartDiagnosisInput(
                before,
                after,
                CharaSelectObserved: true,
                PlacementSceneGeneration: 1,
                ActiveSceneGeneration: 0,
                ResolverEverValid: true,
                StaticAnchorEvaluated: false,
                StaticAnchorAuthorized: false,
                StaticAnchorReason: "not-run",
                CaptureCompleted: false,
                CaptureTimedOut: false,
                PlacementWriteAttemptCount: 0,
                PlacementWriteConfirmed: false,
                UniqueResolvedActorCount: 0,
                ConfirmedWriteKeyCount: 0,
                LoginObserved: true);

            return TitleBackgroundColdStartDiagnosticLogic.Classify(input) == "owner-migration-gap";
        });

        Test(642, "cold-start diagnosis distinguishes actor resolver from later placement stages", () =>
        {
            var owner = new TitleBackgroundColdStartOwnerSnapshot(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                true,
                false,
                true,
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                false,
                "placement",
                "placement");
            var input = new TitleBackgroundColdStartDiagnosisInput(
                owner,
                owner,
                CharaSelectObserved: true,
                PlacementSceneGeneration: 1,
                ActiveSceneGeneration: 1,
                ResolverEverValid: false,
                StaticAnchorEvaluated: false,
                StaticAnchorAuthorized: false,
                StaticAnchorReason: "not-run",
                CaptureCompleted: false,
                CaptureTimedOut: false,
                PlacementWriteAttemptCount: 0,
                PlacementWriteConfirmed: false,
                UniqueResolvedActorCount: 0,
                ConfirmedWriteKeyCount: 0,
                LoginObserved: true);

            return TitleBackgroundColdStartDiagnosticLogic.Classify(input) == "actor-resolver";
        });

        Test(643, "cold-start diagnostic is FRU pre-login only and plugin dispose unsubscribes it", () =>
        {
            var root = FindRepositoryRoot();
            var servicePath = Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "TitleBackground",
                "TitleScreenBackgroundService.ColdStartDiagnostic.cs");
            var constructionPath = Path.Combine(root, "projects", "XIV-Mini-Util", "Plugin.ServiceConstruction.cs");
            var lifecyclePath = Path.Combine(root, "projects", "XIV-Mini-Util", "Plugin.Lifecycle.cs");
            var serviceText = File.ReadAllText(servicePath);
            var constructionText = File.ReadAllText(constructionPath);
            var lifecycleText = File.ReadAllText(lifecyclePath);

            var fru = new TitleBackgroundColdStartOwnerSnapshot(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                true,
                true,
                false,
                string.Empty,
                false,
                "v2",
                "placement");
            var other = fru with { CandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId };

            return TitleBackgroundColdStartDiagnosticLogic.ShouldArm(false, fru)
                && !TitleBackgroundColdStartDiagnosticLogic.ShouldArm(true, fru)
                && !TitleBackgroundColdStartDiagnosticLogic.ShouldArm(false, other)
                && serviceText.Contains("_framework.Update += OnColdStartDiagnosticFrameworkUpdate", StringComparison.Ordinal)
                && serviceText.Contains("_framework.Update -= OnColdStartDiagnosticFrameworkUpdate", StringComparison.Ordinal)
                && !serviceText.Contains("ApplySimpleAutoSetup(", StringComparison.Ordinal)
                && !serviceText.Contains("ContentId", StringComparison.Ordinal)
                && !serviceText.Contains("CharacterAddress", StringComparison.Ordinal)
                && constructionText.Contains("CaptureOwnerSnapshot(_configuration)", StringComparison.Ordinal)
                && constructionText.Contains("StartColdStartDiagnostic(titleBackgroundColdStartBefore)", StringComparison.Ordinal)
                && lifecycleText.Contains("_titleScreenBackgroundService.StopColdStartDiagnostic()", StringComparison.Ordinal);
        });
    }
}
