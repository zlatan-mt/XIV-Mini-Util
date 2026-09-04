// Path: tools/CharaSelectLogicTests/Tests/TitleBackgroundColdStartDiagnosticTests.cs
// Description: Cold-start Character Select Phase A diagnostic の純粋判定と安全契約を固定する
// Reason: 診断自身が再現条件を正規化せず、owner migration gap を証拠から分類できることを最小テストする
using XivMiniUtil;
using XivMiniUtil.Services.CharaSelect;
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
                DrawReadyEverTrue: true,
                StaticAnchorEvaluated: false,
                StaticAnchorAuthorized: false,
                StaticAnchorReason: "not-run",
                CaptureCompleted: false,
                CaptureTimedOut: false,
                PlacementWriteAttemptCount: 0,
                PlacementWriteConfirmed: false,
                UniqueResolvedActorCount: 0,
                ConfirmedWriteKeyCount: 0,
                ActorEpochChangedAfterConfirmedWrite: false,
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
                DrawReadyEverTrue: false,
                StaticAnchorEvaluated: false,
                StaticAnchorAuthorized: false,
                StaticAnchorReason: "not-run",
                CaptureCompleted: false,
                CaptureTimedOut: false,
                PlacementWriteAttemptCount: 0,
                PlacementWriteConfirmed: false,
                UniqueResolvedActorCount: 0,
                ConfirmedWriteKeyCount: 0,
                ActorEpochChangedAfterConfirmedWrite: false,
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
            state.Arm(owner, owner, ColdStartArmMode.Startup);
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

        Test(645, "cold-start startup arm evaluation distinguishes arm/skip/blocked reasons and schema presence", () =>
        {
            var fru = new TitleBackgroundColdStartOwnerSnapshot(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                OverrideEnabled: true,
                V2Enabled: true,
                PlacementEnabled: false,
                PlacementCandidateId: string.Empty,
                PositionCaptured: false,
                ActualOwner: "v2",
                ExpectedOwner: "placement");

            var (statusArmed, reasonArmed) = TitleBackgroundColdStartDiagnosticLogic.EvaluateStartupArm(
                isLoggedIn: false,
                fru,
                automaticCheckRequested: false,
                placementProofArmed: false);

            var (statusLogin, reasonLogin) = TitleBackgroundColdStartDiagnosticLogic.EvaluateStartupArm(
                isLoggedIn: true,
                fru,
                automaticCheckRequested: false,
                placementProofArmed: false);

            var (statusDisabled, reasonDisabled) = TitleBackgroundColdStartDiagnosticLogic.EvaluateStartupArm(
                isLoggedIn: false,
                fru with { OverrideEnabled = false },
                automaticCheckRequested: false,
                placementProofArmed: false);

            var (statusOther, reasonOther) = TitleBackgroundColdStartDiagnosticLogic.EvaluateStartupArm(
                isLoggedIn: false,
                fru with { CandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId },
                automaticCheckRequested: false,
                placementProofArmed: false);

            var (statusBlocked, reasonBlocked) = TitleBackgroundColdStartDiagnosticLogic.EvaluateStartupArm(
                isLoggedIn: false,
                fru,
                automaticCheckRequested: true,
                placementProofArmed: false);

            return TitleBackgroundColdStartDiagnosticLogic.RecorderSchema == 2
                && statusArmed == ColdStartArmStatus.Armed && reasonArmed == "ok"
                && statusLogin == ColdStartArmStatus.Skipped && reasonLogin == "already-logged-in"
                && statusDisabled == ColdStartArmStatus.Skipped && reasonDisabled == "override-disabled"
                && statusOther == ColdStartArmStatus.Skipped && reasonOther == "candidate-not-fru"
                && statusBlocked == ColdStartArmStatus.Blocked && reasonBlocked == "automatic-check-active";
        });

        Test(646, "cold-start fallback arm eligibility gates on lobby, candidate and safe transaction state", () =>
        {
            var fru = new TitleBackgroundColdStartOwnerSnapshot(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                OverrideEnabled: true,
                V2Enabled: false,
                PlacementEnabled: true,
                PlacementCandidateId: TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                PositionCaptured: false,
                ActualOwner: "placement",
                ExpectedOwner: "placement");

            var (okEligible, okReason) = TitleBackgroundColdStartDiagnosticLogic.EvaluateFallbackArm(
                isLoggedIn: false,
                GameLobbyType.CharaSelect,
                startup: fru,
                current: fru,
                automaticCheckActive: false,
                probeTransactionActive: false);

            var (loginEligible, _) = TitleBackgroundColdStartDiagnosticLogic.EvaluateFallbackArm(
                isLoggedIn: true,
                GameLobbyType.CharaSelect,
                startup: fru,
                current: fru,
                automaticCheckActive: false,
                probeTransactionActive: false);

            var (lobbyEligible, _) = TitleBackgroundColdStartDiagnosticLogic.EvaluateFallbackArm(
                isLoggedIn: false,
                GameLobbyType.Title,
                startup: fru,
                current: fru,
                automaticCheckActive: false,
                probeTransactionActive: false);

            var (disabledEligible, _) = TitleBackgroundColdStartDiagnosticLogic.EvaluateFallbackArm(
                isLoggedIn: false,
                GameLobbyType.CharaSelect,
                startup: fru with { OverrideEnabled = false },
                current: fru with { OverrideEnabled = false },
                automaticCheckActive: false,
                probeTransactionActive: false);

            var (otherEligible, _) = TitleBackgroundColdStartDiagnosticLogic.EvaluateFallbackArm(
                isLoggedIn: false,
                GameLobbyType.CharaSelect,
                startup: fru with { CandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId },
                current: fru with { CandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId },
                automaticCheckActive: false,
                probeTransactionActive: false);

            var (transEligible, _) = TitleBackgroundColdStartDiagnosticLogic.EvaluateFallbackArm(
                isLoggedIn: false,
                GameLobbyType.CharaSelect,
                startup: fru,
                current: fru,
                automaticCheckActive: true,
                probeTransactionActive: false);

            return okEligible && okReason == "ok"
                && !loginEligible
                && !lobbyEligible
                && !disabledEligible
                && !otherEligible
                && !transEligible;
        });

        Test(647, "cold-start runtime state tracks DrawReady counters and pointer-free actor recreation epoch", () =>
        {
            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            var owner = new TitleBackgroundColdStartOwnerSnapshot(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                OverrideEnabled: true,
                V2Enabled: false,
                PlacementEnabled: true,
                PlacementCandidateId: TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                PositionCaptured: false,
                ActualOwner: "placement",
                ExpectedOwner: "placement");

            state.Arm(owner, owner, ColdStartArmMode.Startup);

            // Valid context 1: DrawReady = false
            var key1 = new CharaSelectActorIdentityKey(100, 0, 0, 10);
            var actor1 = new CharaSelectResolvedActorContext(
                (nint)0x1000,
                key1,
                NormalizedIndex: 0,
                Source: CharaSelectIdentityResolveSource.SelectedCharacterIndex,
                CurrentCharacterAvailable: true,
                EntryAvailable: true,
                SelectedContentAvailable: true,
                MappingAvailable: true,
                MappingHit: true,
                ClientObjectIndexValid: true,
                ObjectResolved: true,
                IdentityConsistent: true,
                DrawReady: false);
            state.RecordResolverAttempt(actor1);

            // Valid context 1 again: DrawReady = true (transition)
            var actor1Ready = actor1 with { DrawReady = true };
            state.RecordResolverAttempt(actor1Ready);

            // Actor recreation: new identity key
            var key2 = new CharaSelectActorIdentityKey(200, 1, 1, 20);
            var actor2 = actor1 with { IdentityKey = key2, DrawReady = true };
            state.RecordResolverAttempt(actor2);

            return state.DrawReadyAtFirstValid == false
                && state.DrawReadyEverTrue
                && state.DrawReadyTransitionCount == 1
                && state.ActorIdentityEpoch == 2
                && state.ActorRecreationCount == 1;
        });

        Test(648, "cold-start terminal report contains Phase A2 schema, arm result, counters and privacy-safe fields", () =>
        {
            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            var owner = new TitleBackgroundColdStartOwnerSnapshot(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                OverrideEnabled: true,
                V2Enabled: false,
                PlacementEnabled: true,
                PlacementCandidateId: TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                PositionCaptured: false,
                ActualOwner: "placement",
                ExpectedOwner: "placement");

            state.RecordStartupSnapshot(owner);
            state.RecordStartupArmResult(ColdStartArmStatus.Armed, "ok");
            state.Arm(owner, owner, ColdStartArmMode.Startup);
            state.RecordScene("custom:n4gw", 816, 0, 1, 1, "placement", false, true, false);

            var report = state.Complete("post-placement-visual-candidate");

            return report.Contains("coldStart.recorderSchema=2", StringComparison.Ordinal)
                && report.Contains("coldStart.armMode=Startup", StringComparison.Ordinal)
                && report.Contains("coldStart.startupArmStatus=Armed", StringComparison.Ordinal)
                && report.Contains("coldStart.startupArmReason=ok", StringComparison.Ordinal)
                && report.Contains("resolver.drawReadyAtFirstValid=", StringComparison.Ordinal)
                && report.Contains("resolver.drawReadyEverTrue=", StringComparison.Ordinal)
                && report.Contains("resolver.drawReadyTransitionCount=", StringComparison.Ordinal)
                && report.Contains("actor.identityEpoch=", StringComparison.Ordinal)
                && report.Contains("actor.recreationCount=", StringComparison.Ordinal)
                && report.Contains("placement.actorEpochChangedAfterConfirmedWrite=", StringComparison.Ordinal)
                && !report.Contains("0x", StringComparison.Ordinal)
                && TitleScreenBackgroundService.ColdStartDiagnosticPreviousFileName == "title-background-cold-start-diag.prev.txt";
        });

        Test(649, "cold-start fallback arm blocks when startup state changed before first scene", () =>
        {
            var fruCurrent = new TitleBackgroundColdStartOwnerSnapshot(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                OverrideEnabled: true,
                V2Enabled: false,
                PlacementEnabled: true,
                PlacementCandidateId: TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                PositionCaptured: false,
                ActualOwner: "placement",
                ExpectedOwner: "placement");

            // Case 1: startup had override disabled, then user enabled FRU before reaching Character Select
            var startupDisabled = fruCurrent with { OverrideEnabled = false };
            var (case1Eligible, case1Reason) = TitleBackgroundColdStartDiagnosticLogic.EvaluateFallbackArm(
                isLoggedIn: false,
                GameLobbyType.CharaSelect,
                startup: startupDisabled,
                current: fruCurrent,
                automaticCheckActive: false,
                probeTransactionActive: false);

            // Case 2: startup had a different candidate, then user switched to FRU before reaching Character Select
            var startupOtherCandidate = fruCurrent with
            {
                CandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId,
            };
            var (case2Eligible, case2Reason) = TitleBackgroundColdStartDiagnosticLogic.EvaluateFallbackArm(
                isLoggedIn: false,
                GameLobbyType.CharaSelect,
                startup: startupOtherCandidate,
                current: fruCurrent,
                automaticCheckActive: false,
                probeTransactionActive: false);

            // Fail-closed case: startup snapshot missing / empty
            var startupEmpty = default(TitleBackgroundColdStartOwnerSnapshot);
            var (emptyEligible, emptyReason) = TitleBackgroundColdStartDiagnosticLogic.EvaluateFallbackArm(
                isLoggedIn: false,
                GameLobbyType.CharaSelect,
                startup: startupEmpty,
                current: fruCurrent,
                automaticCheckActive: false,
                probeTransactionActive: false);

            return !case1Eligible && case1Reason == "startup-state-changed"
                && !case2Eligible && case2Reason == "startup-state-changed"
                && !emptyEligible && emptyReason == "startup-state-changed";
        });
    }
}
