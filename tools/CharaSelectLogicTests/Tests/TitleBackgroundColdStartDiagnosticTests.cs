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

        Test(643, "cold-start diagnostic is FRU pre-login only and the owning service releases it on dispose", () =>
        {
            var root = FindRepositoryRoot();
            var diagnosticPath = Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "TitleBackground",
                "TitleScreenBackgroundService.ColdStartDiagnostic.cs");
            var servicePath = Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "TitleBackground",
                "TitleScreenBackgroundService.cs");
            var constructionPath = Path.Combine(root, "projects", "XIV-Mini-Util", "Plugin.ServiceConstruction.cs");
            var lifecyclePath = Path.Combine(root, "projects", "XIV-Mini-Util", "Plugin.Lifecycle.cs");
            var diagnosticText = File.ReadAllText(diagnosticPath);
            var serviceText = File.ReadAllText(servicePath);
            var constructionText = File.ReadAllText(constructionPath);
            var lifecycleText = File.ReadAllText(lifecyclePath);

            var disposeStart = serviceText.IndexOf("public void Dispose()", StringComparison.Ordinal);
            var disposeBody = disposeStart >= 0
                ? serviceText.Substring(disposeStart, Math.Min(1200, serviceText.Length - disposeStart))
                : string.Empty;

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
                && diagnosticText.Contains("_framework.Update += OnColdStartDiagnosticFrameworkUpdate", StringComparison.Ordinal)
                && diagnosticText.Contains("_framework.Update -= OnColdStartDiagnosticFrameworkUpdate", StringComparison.Ordinal)
                && !diagnosticText.Contains("ApplySimpleAutoSetup(", StringComparison.Ordinal)
                && !diagnosticText.Contains("ContentId", StringComparison.Ordinal)
                && !diagnosticText.Contains("CharacterAddress", StringComparison.Ordinal)
                && constructionText.Contains("CaptureOwnerSnapshot(_configuration)", StringComparison.Ordinal)
                && constructionText.Contains("StartColdStartDiagnostic(titleBackgroundColdStartBefore)", StringComparison.Ordinal)
                // The service that owns the subscription unsubscribes itself; the Plugin-level explicit stop is redundant.
                && disposeBody.Contains("StopColdStartDiagnostic()", StringComparison.Ordinal)
                && !lifecycleText.Contains("_titleScreenBackgroundService.StopColdStartDiagnostic()", StringComparison.Ordinal);
        });

        Test(644, "cold-start session end without login finishes the run as insufficient-evidence", () =>
        {
            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            var owner = new TitleBackgroundColdStartOwnerSnapshot(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                true,
                true,
                false,
                string.Empty,
                false,
                "v2",
                "placement");
            state.Arm(owner, owner);
            state.RecordScene("custom:fru", 0, 0, 1, 0, "v2", true, false, false);

            var report = state.Complete("insufficient-evidence");

            var root = FindRepositoryRoot();
            var servicePath = Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "TitleBackground",
                "TitleScreenBackgroundService.cs");
            var diagnosticPath = Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "TitleBackground",
                "TitleScreenBackgroundService.ColdStartDiagnostic.cs");
            var serviceText = File.ReadAllText(servicePath);
            var diagnosticText = File.ReadAllText(diagnosticPath);

            return report.Contains("coldStart.diagnosis=insufficient-evidence", StringComparison.Ordinal)
                && !state.Active
                && state.Completed
                // session-end path is wired through the existing CharaSelect session teardown, only when not logging in,
                // and only after the first CharaSelect was observed.
                && serviceText.Contains("NotifyColdStartDiagnosticTitleBackgroundSessionEnded();", StringComparison.Ordinal)
                && serviceText.Contains("reason != \"world-login-transition\"", StringComparison.Ordinal)
                && diagnosticText.Contains("!_coldStartDiagnostic.CharaSelectObserved", StringComparison.Ordinal)
                && diagnosticText.Contains("if (!_coldStartDiagnostic.Active)", StringComparison.Ordinal);
        });
    }
}
