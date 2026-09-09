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

        static TitleBackgroundColdStartOwnerSnapshot PlacementOwner() => new(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
            OverrideEnabled: true,
            V2Enabled: false,
            PlacementEnabled: true,
            PlacementCandidateId: TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
            PositionCaptured: false,
            ActualOwner: "placement",
            ExpectedOwner: "placement");

        static CharaSelectResolvedActorContext ValidActor(CharaSelectActorIdentityKey key) => new(
            (nint)0x2000,
            key,
            NormalizedIndex: 0,
            Source: CharaSelectIdentityResolveSource.CurrentCharacterMapping,
            CurrentCharacterAvailable: true,
            EntryAvailable: true,
            SelectedContentAvailable: true,
            MappingAvailable: true,
            MappingHit: true,
            ClientObjectIndexValid: true,
            ObjectResolved: true,
            IdentityConsistent: true,
            DrawReady: true,
            VisualStateCaptured: true,
            VisibilityRaw: 0,
            VisibilityHidden: false,
            ReadyToDrawFlag: true,
            RenderFlagsRaw: 0,
            RenderFlagsModelBitSet: false,
            DrawObjectPresent: true,
            ScaleFinitePositive: true,
            DrawOffsetFinite: true,
            DrawOffsetNonZero: false);

        // A same-tick correlation snapshot for a run where placement has applied+confirmed for
        // `appliedKey` at `appliedGeneration`, the resolver currently sees `resolvedKey`, and the
        // read-only transform read produced `driftMeters` (NaN = read failed / not computable).
        static TitleBackgroundColdStartPlacementTickSnapshot Tick(
            int sceneGeneration,
            CharaSelectActorIdentityKey resolvedKey,
            bool resolvedValid,
            bool transformReadOk,
            int applyCount,
            bool writeReadbackConfirmed,
            CharaSelectActorIdentityKey appliedKey,
            int appliedGeneration,
            float driftMeters,
            string writeStatus = "confirmed",
            bool posReadback = true,
            bool rotReadback = true,
            bool setterCompleted = true,
            int writeAttemptCount = 0,
            string trigger = "capture-complete") => new(
            SceneGeneration: sceneGeneration,
            ResolvedActorValid: resolvedValid,
            ResolvedActorKey: resolvedKey,
            TransformReadOk: transformReadOk,
            PlacementApplyCount: applyCount,
            PlacementLastWriteReadbackConfirmed: writeReadbackConfirmed,
            PlacementLastWriteStatus: writeStatus,
            PlacementLastWritePositionReadback: posReadback,
            PlacementLastWriteRotationReadback: rotReadback,
            PlacementLastWriteSetterCompleted: setterCompleted,
            PlacementWriteAttemptCount: writeAttemptCount,
            PlacementLastTrigger: trigger,
            PlacementLastAppliedActorKey: appliedKey,
            PlacementLastAppliedSceneGeneration: appliedGeneration,
            RetentionDriftMeters: driftMeters);

        static TitleBackgroundColdStartDiagnosisInput RetentionClassifyInput(
            bool retentionComparable,
            float retentionDriftMeters,
            bool driftExceededEver = false,
            int uniqueResolved = 1,
            int confirmedWriteKeys = 1,
            bool actorEpochChangedAfterWrite = false) => new(
            PlacementOwner(),
            PlacementOwner(),
            CharaSelectObserved: true,
            PlacementSceneGeneration: 1,
            ActiveSceneGeneration: 1,
            ResolverEverValid: true,
            DrawReadyEverTrue: true,
            StaticAnchorEvaluated: true,
            StaticAnchorAuthorized: true,
            StaticAnchorReason: "authorized",
            CaptureCompleted: true,
            CaptureTimedOut: false,
            PlacementWriteAttemptCount: 3,
            PlacementWriteConfirmed: true,
            UniqueResolvedActorCount: uniqueResolved,
            ConfirmedWriteKeyCount: confirmedWriteKeys,
            ActorEpochChangedAfterConfirmedWrite: actorEpochChangedAfterWrite,
            LoginObserved: true,
            LatestVisualCaptured: true,
            LatestVisualHidden: false,
            LatestVisualScaleFinitePositive: true,
            LatestVisualDrawOffsetFinite: true,
            RetentionTerminalComparable: retentionComparable,
            RetentionTerminalDriftMeters: retentionDriftMeters,
            RetentionDriftExceededEpsilonEver: driftExceededEver);

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

            return TitleBackgroundColdStartDiagnosticLogic.RecorderSchema == 3
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

        Test(648, "cold-start terminal report contains schema, arm result, counters and privacy-safe fields", () =>
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

            return report.Contains("coldStart.recorderSchema=3", StringComparison.Ordinal)
                && report.Contains("coldStart.armMode=Startup", StringComparison.Ordinal)
                && report.Contains("coldStart.startupArmStatus=Armed", StringComparison.Ordinal)
                && report.Contains("coldStart.startupArmReason=ok", StringComparison.Ordinal)
                && report.Contains("coldStart.evidenceNote=", StringComparison.Ordinal)
                && report.Contains("resolver.drawReadyAtFirstValid=", StringComparison.Ordinal)
                && report.Contains("resolver.drawReadyEverTrue=", StringComparison.Ordinal)
                && report.Contains("resolver.drawReadyTransitionCount=", StringComparison.Ordinal)
                && report.Contains("actor.identityEpoch=", StringComparison.Ordinal)
                && report.Contains("actor.recreationCount=", StringComparison.Ordinal)
                && report.Contains("placement.actorEpochChangedAfterConfirmedWrite=", StringComparison.Ordinal)
                && report.Contains("placement.writeConfirmedSemantics=cumulative-any-attempt", StringComparison.Ordinal)
                && report.Contains("retention.terminalDriftMeters=none", StringComparison.Ordinal)
                && report.Contains("retention.epsilonMeters=", StringComparison.Ordinal)
                && report.Contains("checkpoints.count=", StringComparison.Ordinal)
                && report.Contains("login.placementLoginStopInterpretation=", StringComparison.Ordinal)
                && !report.Contains("login.placementLoginStopped=", StringComparison.Ordinal)
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

        Test(650, "cold-start recorder is dev-plugin-only and both arm paths check the gate before any other state", () =>
        {
            var (releaseAllowed, releaseReason) = TitleBackgroundColdStartDiagnosticLogic.EvaluateDevGate(isDevPlugin: false);
            var (devAllowed, devReason) = TitleBackgroundColdStartDiagnosticLogic.EvaluateDevGate(isDevPlugin: true);

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
            var diagnosticText = File.ReadAllText(diagnosticPath);
            var serviceText = File.ReadAllText(servicePath);
            var constructionText = File.ReadAllText(constructionPath);

            return !releaseAllowed && releaseReason == "release-build"
                && devAllowed && devReason == "ok"
                && serviceText.Contains("private readonly bool _isDevPlugin;", StringComparison.Ordinal)
                && diagnosticText.Contains("TitleBackgroundColdStartDiagnosticLogic.EvaluateDevGate(_isDevPlugin)", StringComparison.Ordinal)
                && diagnosticText.Contains("if (!_isDevPlugin)", StringComparison.Ordinal)
                // review 5118977128 small cleanup: the dev gate must be checked before the recorder-loaded
                // log / startup snapshot, so a release plugin shows literally no recorder behavior.
                && diagnosticText.IndexOf("EvaluateDevGate(_isDevPlugin)", StringComparison.Ordinal)
                    < diagnosticText.IndexOf("Title Background cold-start recorder loaded", StringComparison.Ordinal)
                && constructionText.Contains("isDevPlugin: pluginInterface.IsDev", StringComparison.Ordinal);
        });

        Test(651, "cold-start classification uses only the documented GameObject.Visibility signal, never RenderFlags, for hidden/model verdicts", () =>
        {
            var owner = new TitleBackgroundColdStartOwnerSnapshot(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                OverrideEnabled: true,
                V2Enabled: false,
                PlacementEnabled: true,
                PlacementCandidateId: TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                PositionCaptured: true,
                ActualOwner: "placement",
                ExpectedOwner: "placement");

            TitleBackgroundColdStartDiagnosisInput BaseInput(
                bool visualCaptured,
                bool? visualHidden,
                bool scaleFinitePositive,
                bool drawOffsetFinite)
                => new(
                    owner,
                    owner,
                    CharaSelectObserved: true,
                    PlacementSceneGeneration: 1,
                    ActiveSceneGeneration: 1,
                    ResolverEverValid: true,
                    DrawReadyEverTrue: true,
                    StaticAnchorEvaluated: true,
                    StaticAnchorAuthorized: true,
                    StaticAnchorReason: "ok",
                    CaptureCompleted: true,
                    CaptureTimedOut: false,
                    PlacementWriteAttemptCount: 1,
                    PlacementWriteConfirmed: true,
                    UniqueResolvedActorCount: 1,
                    ConfirmedWriteKeyCount: 1,
                    ActorEpochChangedAfterConfirmedWrite: false,
                    LoginObserved: true,
                    LatestVisualCaptured: visualCaptured,
                    LatestVisualHidden: visualHidden,
                    LatestVisualScaleFinitePositive: scaleFinitePositive,
                    LatestVisualDrawOffsetFinite: drawOffsetFinite);

            // Documented Visibility byte == 1 (hidden) is the only signal that may classify "hidden".
            var hidden = TitleBackgroundColdStartDiagnosticLogic.Classify(
                BaseInput(visualCaptured: true, visualHidden: true, scaleFinitePositive: true, drawOffsetFinite: true));
            // Visibility byte == 0 (visible) with a bad transform still yields the transform candidate.
            var transformCandidate = TitleBackgroundColdStartDiagnosticLogic.Classify(
                BaseInput(visualCaptured: true, visualHidden: false, scaleFinitePositive: false, drawOffsetFinite: true));
            // Visibility byte == 0 (visible) and a normal transform falls back to the generic label.
            var genericFallback = TitleBackgroundColdStartDiagnosticLogic.Classify(
                BaseInput(visualCaptured: true, visualHidden: false, scaleFinitePositive: true, drawOffsetFinite: true));
            // An unconfirmed/unknown raw Visibility value (neither 0 nor 1) must NOT be treated as hidden.
            var unknownVisibility = TitleBackgroundColdStartDiagnosticLogic.Classify(
                BaseInput(visualCaptured: true, visualHidden: null, scaleFinitePositive: true, drawOffsetFinite: true));
            // Not captured must never be interpreted as hidden; it must fall back to the pre-existing label.
            var notCaptured = TitleBackgroundColdStartDiagnosticLogic.Classify(
                BaseInput(visualCaptured: false, visualHidden: null, scaleFinitePositive: false, drawOffsetFinite: false));

            // There must be no remaining "actor-model-render-disabled" classification anywhere: RenderFlags'
            // Model-bit direction is not documented strongly enough (review 5118977128 MUST FIX).
            var root = FindRepositoryRoot();
            var diagnosticPath = Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "TitleBackground",
                "TitleScreenBackgroundService.ColdStartDiagnostic.cs");
            var diagnosticText = File.ReadAllText(diagnosticPath);

            return hidden == "actor-visibility-hidden"
                && transformCandidate == "actor-visual-transform-candidate"
                && genericFallback == "post-placement-visual-candidate"
                && unknownVisibility == "post-placement-visual-candidate"
                && notCaptured == "post-placement-visual-candidate"
                && !diagnosticText.Contains("actor-model-render-disabled", StringComparison.Ordinal);
        });

        Test(652, "cold-start runtime state captures first-valid and latest visual snapshots from the documented Visibility byte only", () =>
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

            var key = new CharaSelectActorIdentityKey(100, 0, 0, 10);
            var baseActor = new CharaSelectResolvedActorContext(
                (nint)0x1000,
                key,
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
                DrawReady: true,
                VisualStateCaptured: true,
                VisibilityRaw: 0,
                VisibilityHidden: false,
                ReadyToDrawFlag: true,
                RenderFlagsRaw: 0,
                RenderFlagsModelBitSet: false,
                DrawObjectPresent: true,
                ScaleFinitePositive: true,
                DrawOffsetFinite: true,
                DrawOffsetNonZero: false);

            // First valid: visible (Visibility byte 0).
            state.RecordResolverAttempt(baseActor);
            // Second attempt: hidden (Visibility byte 1) — transition 1.
            var hidden = baseActor with { VisibilityRaw = 1, VisibilityHidden = true };
            state.RecordResolverAttempt(hidden);
            // Third attempt: visible again (transition 2), also the latest/terminal snapshot.
            state.RecordResolverAttempt(baseActor);
            // An uncaptured read must not overwrite the latest snapshot with misleading defaults.
            var uncaptured = default(CharaSelectResolvedActorContext);
            state.RecordResolverAttempt(uncaptured);

            return state.FirstValidVisualCaptured
                && state.FirstValidVisualHidden == false
                && state.LatestVisualCaptured
                && state.LatestVisualHidden == false
                && state.LatestVisualReadyToDrawFlag
                && state.VisualVisibilityTransitionCount == 2;
        });

        Test(653, "cold-start diagnostic file retention is bounded and rolls oldest-first without losing the previous-file contract", () =>
        {
            var root = FindRepositoryRoot();
            var diagnosticPath = Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "TitleBackground",
                "TitleScreenBackgroundService.ColdStartDiagnostic.cs");
            var diagnosticText = File.ReadAllText(diagnosticPath);

            return TitleScreenBackgroundService.ColdStartDiagnosticFileName == "title-background-cold-start-diag.txt"
                && TitleScreenBackgroundService.ColdStartDiagnosticPreviousFileName == "title-background-cold-start-diag.prev.txt"
                && TitleScreenBackgroundService.ColdStartDiagnosticPreviousFileName2 == "title-background-cold-start-diag.prev2.txt"
                && TitleScreenBackgroundService.ColdStartDiagnosticPreviousFileName3 == "title-background-cold-start-diag.prev3.txt"
                && TitleScreenBackgroundService.ColdStartDiagnosticPreviousFileName4 == "title-background-cold-start-diag.prev4.txt"
                && diagnosticText.Contains("ColdStartDiagnosticRotationFileNames.Length - 1; i > 0; i--", StringComparison.Ordinal);
        });

        Test(655, "cold-start visual-state read never derives visibility/model verdicts from RenderFlags", () =>
        {
            var root = FindRepositoryRoot();
            var resolverPath = Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "CharaSelect",
                "CharaSelectService.SelectedCharacterIdentity.cs");
            var resolverText = File.ReadAllText(resolverPath);

            // The only visibility-classifying read must be the documented GameObject.Visibility byte.
            var visibilityHiddenSwitchIndex = resolverText.IndexOf(
                "visibilityRaw switch", StringComparison.Ordinal);
            var readyToDrawIndex = resolverText.IndexOf(
                "ObjectTargetableFlags.ReadyToDraw", StringComparison.Ordinal);

            return visibilityHiddenSwitchIndex >= 0
                && readyToDrawIndex >= 0
                && !resolverText.Contains("ActorVisible", StringComparison.Ordinal)
                && !resolverText.Contains("ModelRenderDisabled", StringComparison.Ordinal)
                && resolverText.Contains("RenderFlagsModelBitSet", StringComparison.Ordinal);
        });

        Test(656, "cold-start resolver/visual observation is never hard-stopped by a fixed attempt count", () =>
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

            var key = new CharaSelectActorIdentityKey(100, 0, 0, 10);
            var actor = new CharaSelectResolvedActorContext(
                (nint)0x1000,
                key,
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
                DrawReady: true,
                VisualStateCaptured: true,
                VisibilityRaw: 0,
                VisibilityHidden: false,
                ReadyToDrawFlag: true,
                RenderFlagsRaw: 0,
                RenderFlagsModelBitSet: false,
                DrawObjectPresent: true,
                ScaleFinitePositive: true,
                DrawOffsetFinite: true,
                DrawOffsetNonZero: false);

            // The old hard-stop kicked in at 120 attempts; prove observation still records well past it
            // (a full 10-minute run at 60fps is ~36000 frames, so 300 is a representative sample).
            const int attemptsPastOldBudget = 300;
            for (var i = 0; i < attemptsPastOldBudget; i++)
            {
                state.RecordResolverAttempt(actor);
            }

            var root = FindRepositoryRoot();
            var diagnosticPath = Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "TitleBackground",
                "TitleScreenBackgroundService.ColdStartDiagnostic.cs");
            var diagnosticText = File.ReadAllText(diagnosticPath);

            return state.ResolverAttemptCount == attemptsPastOldBudget
                && state.LatestVisualCaptured
                // The fixed-count gate must no longer exist anywhere in the recorder source.
                && !diagnosticText.Contains("ResolverAttemptBudget", StringComparison.Ordinal)
                && !diagnosticText.Contains("CanAttemptResolver", StringComparison.Ordinal);
        });

        Test(657, "cold-start persistent ever-anomaly facts survive a later recovered/visible terminal sample", () =>
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

            var key = new CharaSelectActorIdentityKey(100, 0, 0, 10);
            var visible = new CharaSelectResolvedActorContext(
                (nint)0x1000,
                key,
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
                DrawReady: true,
                VisualStateCaptured: true,
                VisibilityRaw: 0,
                VisibilityHidden: false,
                ReadyToDrawFlag: true,
                RenderFlagsRaw: 0,
                RenderFlagsModelBitSet: false,
                DrawObjectPresent: true,
                ScaleFinitePositive: true,
                DrawOffsetFinite: true,
                DrawOffsetNonZero: false);

            // Transient anomaly mid-run: hidden, no DrawObject, not ready-to-draw.
            var transientAnomaly = visible with
            {
                VisibilityRaw = 1,
                VisibilityHidden = true,
                ReadyToDrawFlag = false,
                DrawObjectPresent = false,
            };

            // Recovers before the terminal sample.
            state.RecordResolverAttempt(visible);
            state.RecordResolverAttempt(transientAnomaly);
            state.RecordResolverAttempt(visible);

            // The latest/terminal snapshot shows a fully recovered, unremarkable state...
            return state.LatestVisualHidden == false
                && state.LatestVisualDrawObjectPresent
                && state.LatestVisualReadyToDrawFlag
                // ...but the persistent ever-facts still remember the transient anomaly happened.
                && state.VisibilityHiddenEverTrue
                && state.DrawObjectEverAbsentWhileCaptured
                && state.ReadyToDrawEverFalseWhileCaptured;
        });

        Test(658, "cold-start ever-anomaly facts are evidence only and are not fed into the terminal classifier", () =>
        {
            var root = FindRepositoryRoot();
            var diagnosticPath = Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "TitleBackground",
                "TitleScreenBackgroundService.ColdStartDiagnostic.cs");
            var diagnosticText = File.ReadAllText(diagnosticPath);

            var classifyBody = ExtractMethodBody(
                diagnosticText,
                "public static string Classify(in TitleBackgroundColdStartDiagnosisInput input)");

            return diagnosticText.Contains("VisibilityHiddenEverTrue", StringComparison.Ordinal)
                && diagnosticText.Contains("DrawObjectEverAbsentWhileCaptured", StringComparison.Ordinal)
                && diagnosticText.Contains("ReadyToDrawEverFalseWhileCaptured", StringComparison.Ordinal)
                && !classifyBody.Contains("VisibilityHiddenEverTrue", StringComparison.Ordinal)
                && !classifyBody.Contains("DrawObjectEverAbsentWhileCaptured", StringComparison.Ordinal)
                && !classifyBody.Contains("ReadyToDrawEverFalseWhileCaptured", StringComparison.Ordinal);
        });

        Test(659, "cold-start retention does not false-positive on an ordinary character switch or scene switch", () =>
        {
            var applied = new CharaSelectActorIdentityKey(100, 0, 0, 10);
            var switched = new CharaSelectActorIdentityKey(200, 1, 1, 20);

            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            state.Arm(PlacementOwner(), PlacementOwner(), ColdStartArmMode.Startup);
            state.RecordResolverAttempt(ValidActor(switched));

            // Different resolved actor than the last confirmed apply -> not comparable, no drift.
            state.RecordPlacementCorrelation(Tick(
                sceneGeneration: 1, resolvedKey: switched, resolvedValid: true, transformReadOk: true,
                applyCount: 1, writeReadbackConfirmed: true, appliedKey: applied, appliedGeneration: 1,
                driftMeters: 4.2f));

            // Same actor but a different (newer) scene generation than the applied one -> not comparable.
            state.RecordPlacementCorrelation(Tick(
                sceneGeneration: 2, resolvedKey: applied, resolvedValid: true, transformReadOk: true,
                applyCount: 1, writeReadbackConfirmed: true, appliedKey: applied, appliedGeneration: 1,
                driftMeters: 4.2f));

            var classified = TitleBackgroundColdStartDiagnosticLogic.Classify(
                RetentionClassifyInput(state.RetentionTerminalComparable, state.RetentionTerminalDriftMeters));

            return state.RetentionNotComparableCount == 2
                && !state.RetentionTerminalComparable
                && float.IsNaN(state.RetentionTerminalDriftMeters)
                && float.IsNaN(state.RetentionLastValidDriftMeters)
                && state.RetentionComparableSampleCountTotal == 0
                && classified != "placement-retention-drift";
        });

        Test(660, "cold-start retention records a same-actor post-apply displacement that later recovers as evidence, not a terminal drift", () =>
        {
            var key = new CharaSelectActorIdentityKey(100, 0, 0, 10);
            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            state.Arm(PlacementOwner(), PlacementOwner(), ColdStartArmMode.Startup);
            state.RecordResolverAttempt(ValidActor(key));

            state.RecordPlacementCorrelation(Tick(
                sceneGeneration: 1, resolvedKey: key, resolvedValid: true, transformReadOk: true,
                applyCount: 1, writeReadbackConfirmed: true, appliedKey: key, appliedGeneration: 1,
                driftMeters: 0.2f));
            state.RecordPlacementCorrelation(Tick(
                sceneGeneration: 1, resolvedKey: key, resolvedValid: true, transformReadOk: true,
                applyCount: 1, writeReadbackConfirmed: true, appliedKey: key, appliedGeneration: 1,
                driftMeters: 0.001f));

            var checkpoints = string.Join("\n", state.Checkpoints);
            var classified = TitleBackgroundColdStartDiagnosticLogic.Classify(
                RetentionClassifyInput(state.RetentionTerminalComparable, state.RetentionTerminalDriftMeters, state.RetentionDriftExceededEpsilonEverTrue));

            return state.RetentionTerminalComparable
                && state.RetentionTerminalDriftMeters <= TitleBackgroundColdStartDiagnosticLogic.RetentionDriftEpsilonMeters
                && state.RetentionDriftExceededEpsilonEverTrue
                && state.RetentionMaxDriftMeters >= 0.19f
                && state.RetentionComparableSampleCountTotal == 2
                && checkpoints.Contains("retention-drift-exceeded", StringComparison.Ordinal)
                && checkpoints.Contains("retention-drift-recovered", StringComparison.Ordinal)
                && classified != "placement-retention-drift";
        });

        Test(661, "cold-start classifier reports placement-retention-drift only for a comparable terminal sample past epsilon", () =>
        {
            var sustained = TitleBackgroundColdStartDiagnosticLogic.Classify(
                RetentionClassifyInput(retentionComparable: true, retentionDriftMeters: 0.5f));
            var recovered = TitleBackgroundColdStartDiagnosticLogic.Classify(
                RetentionClassifyInput(retentionComparable: true, retentionDriftMeters: 0.001f, driftExceededEver: true));
            var notComparable = TitleBackgroundColdStartDiagnosticLogic.Classify(
                RetentionClassifyInput(retentionComparable: false, retentionDriftMeters: float.NaN, driftExceededEver: true));

            return sustained == "placement-retention-drift"
                && recovered == "post-placement-visual-candidate"
                && notComparable == "post-placement-visual-candidate";
        });

        Test(662, "cold-start retention never converts a failed read or an unconfirmed placement into 0 / retained", () =>
        {
            var key = new CharaSelectActorIdentityKey(100, 0, 0, 10);
            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            state.Arm(PlacementOwner(), PlacementOwner(), ColdStartArmMode.Startup);
            state.RecordResolverAttempt(ValidActor(key));

            // Transform read failed this tick.
            state.RecordPlacementCorrelation(Tick(
                sceneGeneration: 1, resolvedKey: key, resolvedValid: true, transformReadOk: false,
                applyCount: 1, writeReadbackConfirmed: true, appliedKey: key, appliedGeneration: 1,
                driftMeters: float.NaN));
            // A placement write attempt exists but was never readback-confirmed.
            state.RecordPlacementCorrelation(Tick(
                sceneGeneration: 1, resolvedKey: key, resolvedValid: true, transformReadOk: true,
                applyCount: 1, writeReadbackConfirmed: false, appliedKey: key, appliedGeneration: 1,
                driftMeters: 0.3f));

            var report = state.Complete("post-placement-visual-candidate");

            return state.RetentionNotComparableCount == 2
                && state.RetentionComparableSampleCountTotal == 0
                && float.IsNaN(state.RetentionLastValidDriftMeters)
                && float.IsNaN(state.RetentionMaxDriftMeters)
                && !state.RetentionDriftExceededEpsilonEverTrue
                && report.Contains("retention.lastValidDriftMeters=none", StringComparison.Ordinal)
                && report.Contains("retention.maxDriftMeters=none", StringComparison.Ordinal)
                && report.Contains("retention.notComparableCount=2", StringComparison.Ordinal);
        });

        Test(663, "cold-start retention closes the interval and re-baselines when the confirmed placement target changes", () =>
        {
            var key = new CharaSelectActorIdentityKey(100, 0, 0, 10);
            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            state.Arm(PlacementOwner(), PlacementOwner(), ColdStartArmMode.Startup);
            state.RecordResolverAttempt(ValidActor(key));

            // Interval 1: one comparable sample well past epsilon.
            state.RecordPlacementCorrelation(Tick(
                sceneGeneration: 1, resolvedKey: key, resolvedValid: true, transformReadOk: true,
                applyCount: 1, writeReadbackConfirmed: true, appliedKey: key, appliedGeneration: 1,
                driftMeters: 0.3f));

            // Placement re-applies (apply count 1 -> 2): interval 1 closes, a fresh baseline starts.
            state.RecordPlacementCorrelation(Tick(
                sceneGeneration: 1, resolvedKey: key, resolvedValid: true, transformReadOk: true,
                applyCount: 2, writeReadbackConfirmed: true, appliedKey: key, appliedGeneration: 1,
                driftMeters: 0.002f));

            return state.RetentionClosedIntervalCount == 1
                && state.RetentionIntervalComparableSampleCount == 1
                && state.RetentionTerminalDriftMeters <= TitleBackgroundColdStartDiagnosticLogic.RetentionDriftEpsilonMeters
                && state.RetentionComparableSampleCountTotal == 2;
        });

        Test(664, "cold-start checkpoint history is bounded with an explicit dropped count and keeps latest state", () =>
        {
            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            state.Arm(PlacementOwner(), PlacementOwner(), ColdStartArmMode.Startup);

            const int distinctKeys = 30;
            for (var i = 0; i < distinctKeys; i++)
            {
                var key = new CharaSelectActorIdentityKey((ulong)(100 + i), (short)i, (ushort)i, (uint)(10 + i));
                state.RecordResolverAttempt(ValidActor(key));
                state.RecordPlacementCorrelation(Tick(
                    sceneGeneration: 1, resolvedKey: key, resolvedValid: true, transformReadOk: true,
                    applyCount: 0, writeReadbackConfirmed: false, appliedKey: default, appliedGeneration: 0,
                    driftMeters: float.NaN));
            }

            var report = state.Complete("post-placement-visual-candidate");

            // 1 identity-first + 29 identity-changed = 30 checkpoint events, cap 24.
            return state.Checkpoints.Count == 24
                && state.CheckpointDroppedCount == 6
                && state.ActorIdentityEpoch == distinctKeys
                && report.Contains("checkpoints.count=24", StringComparison.Ordinal)
                && report.Contains("checkpoints.dropped=6", StringComparison.Ordinal)
                && report.Contains("checkpoints.note=event-order-within-a-single-frame-is-unknown", StringComparison.Ordinal);
        });

        Test(665, "cold-start per-attempt write observations track latest status distinctly from cumulative confirmation and are bounded", () =>
        {
            var key = new CharaSelectActorIdentityKey(100, 0, 0, 10);
            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            state.Arm(PlacementOwner(), PlacementOwner(), ColdStartArmMode.Startup);
            state.RecordResolverAttempt(ValidActor(key));

            state.RecordPlacementCorrelation(Tick(
                sceneGeneration: 1, resolvedKey: key, resolvedValid: true, transformReadOk: true,
                applyCount: 0, writeReadbackConfirmed: false, appliedKey: default, appliedGeneration: 0,
                driftMeters: float.NaN, writeStatus: "confirmed", posReadback: true, rotReadback: true,
                writeAttemptCount: 1));
            state.RecordPlacementCorrelation(Tick(
                sceneGeneration: 1, resolvedKey: key, resolvedValid: true, transformReadOk: true,
                applyCount: 0, writeReadbackConfirmed: false, appliedKey: default, appliedGeneration: 0,
                driftMeters: float.NaN, writeStatus: "position-readback-mismatch", posReadback: false, rotReadback: false,
                writeAttemptCount: 2));
            // Counter jumps 2 -> 5: two attempts happened between recorder ticks.
            state.RecordPlacementCorrelation(Tick(
                sceneGeneration: 1, resolvedKey: key, resolvedValid: true, transformReadOk: true,
                applyCount: 0, writeReadbackConfirmed: false, appliedKey: default, appliedGeneration: 0,
                driftMeters: float.NaN, writeStatus: "confirmed", posReadback: true, rotReadback: true,
                writeAttemptCount: 5));

            var observations = string.Join("\n", state.WriteObservations);

            return state.WriteObservations.Count == 3
                && observations.Contains("idx=2;status=position-readback-mismatch;posReadback=False", StringComparison.Ordinal)
                && observations.Contains("idx=5;status=confirmed", StringComparison.Ordinal)
                && observations.Contains("priorUnobservedAttempts=2", StringComparison.Ordinal)
                && state.LatestWriteStatus == "confirmed"
                && state.LatestWritePositionReadback;
        });

        Test(666, "cold-start fresh runtime state renders empty/none retention + checkpoint fields", () =>
        {
            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            state.Arm(PlacementOwner(), PlacementOwner(), ColdStartArmMode.Startup);
            state.RecordScene("custom:fru-clear-stage", 1238, 0, 1, 1, "placement", false, true, false);
            var report = state.Complete("post-placement-visual-candidate");

            return report.Contains("retention.terminalComparable=False", StringComparison.Ordinal)
                && report.Contains("retention.terminalDriftMeters=none", StringComparison.Ordinal)
                && report.Contains("retention.comparableSampleCount=0", StringComparison.Ordinal)
                && report.Contains("retention.notComparableCount=0", StringComparison.Ordinal)
                && report.Contains("retention.closedIntervalCount=0", StringComparison.Ordinal)
                && report.Contains("checkpoints.count=0", StringComparison.Ordinal)
                && report.Contains("checkpoints.dropped=0", StringComparison.Ordinal)
                && report.Contains("placement.writeAttemptObservations.count=0", StringComparison.Ordinal);
        });

        Test(667, "cold-start login-stop is a split representation, never a bare stopped claim", () =>
        {
            var nonProof = TitleBackgroundColdStartDiagnosticLogic.LoginStopInterpretation(
                logoutTransitionObserved: false, loginStopLatch: false);
            var proofSet = TitleBackgroundColdStartDiagnosticLogic.LoginStopInterpretation(
                logoutTransitionObserved: true, loginStopLatch: true);
            var proofUnset = TitleBackgroundColdStartDiagnosticLogic.LoginStopInterpretation(
                logoutTransitionObserved: true, loginStopLatch: false);

            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            state.Arm(PlacementOwner(), PlacementOwner(), ColdStartArmMode.Startup);
            state.RecordLoginEvidence(
                sceneOverrideActive: false,
                v2PostLoginWritesStopped: true,
                placementLoginStopLatch: false,
                placementLogoutTransitionObserved: false,
                placementWriteAttemptCountAtLoginFrame: 0);
            var report = state.Complete("post-placement-visual-candidate");

            return nonProof == "latch-not-applicable-requires-proof-run-logout-observation"
                && proofSet == "latch-set"
                && proofUnset == "latch-expected-but-unset"
                && report.Contains("login.placementLoginStopLatch=False", StringComparison.Ordinal)
                && report.Contains("login.placementLogoutTransitionObserved=False", StringComparison.Ordinal)
                && report.Contains("login.placementLoginStopInterpretation=latch-not-applicable-requires-proof-run-logout-observation", StringComparison.Ordinal)
                && report.Contains("login.placementWriteObservationEndsAtLoginFrame=True", StringComparison.Ordinal)
                && report.Contains("login.placementPostLoginWriteGate=stop-on-logged-in-gate-early-return", StringComparison.Ordinal)
                && report.Contains("login.placementWriteAttemptDeltaAtLoginFrame=0", StringComparison.Ordinal)
                && !report.Contains("login.placementLoginStopped=", StringComparison.Ordinal);
        });

        Test(668, "cold-start identity checkpoints use run-local anonymous slots (A->B->A) and never emit raw ids", () =>
        {
            var a = new CharaSelectActorIdentityKey(111, 0, 0, 10);
            var b = new CharaSelectActorIdentityKey(222, 1, 1, 20);

            var state = new TitleBackgroundColdStartDiagnosticRuntimeState();
            state.Arm(PlacementOwner(), PlacementOwner(), ColdStartArmMode.Startup);

            foreach (var key in new[] { a, b, a })
            {
                state.RecordResolverAttempt(ValidActor(key));
                state.RecordPlacementCorrelation(Tick(
                    sceneGeneration: 1, resolvedKey: key, resolvedValid: true, transformReadOk: true,
                    applyCount: 0, writeReadbackConfirmed: false, appliedKey: default, appliedGeneration: 0,
                    driftMeters: float.NaN));
            }

            var report = state.Complete("post-placement-visual-candidate");
            var checkpoints = string.Join("\n", state.Checkpoints);

            return state.ActorIdentityEpoch == 3
                && state.ActorRecreationCount == 2
                && checkpoints.Contains("identity-first;epoch=1;anonSlot=0", StringComparison.Ordinal)
                && checkpoints.Contains("anonSlot=1;returnedToSlot=False", StringComparison.Ordinal)
                && checkpoints.Contains("anonSlot=0;returnedToSlot=True", StringComparison.Ordinal)
                && checkpoints.Contains("changed=[content=", StringComparison.Ordinal)
                && !report.Contains("0x", StringComparison.Ordinal)
                && !report.Contains("111", StringComparison.Ordinal)
                && !report.Contains("222", StringComparison.Ordinal);
        });

        Test(669, "cold-start classifier separates a bare post-write identity-epoch change (evidence) from an unmatched resolved identity (H7)", () =>
        {
            // Sample-log shape: epoch changed after a confirmed write, but every resolved identity
            // did get its own confirmed write (unique == confirmedKeys). NOT actor-recreation.
            var bareEpochChange = TitleBackgroundColdStartDiagnosticLogic.Classify(
                RetentionClassifyInput(
                    retentionComparable: false,
                    retentionDriftMeters: float.NaN,
                    uniqueResolved: 2,
                    confirmedWriteKeys: 2,
                    actorEpochChangedAfterWrite: true));

            // A resolved identity that never received its own confirmed write -> H7.
            var unmatchedIdentity = TitleBackgroundColdStartDiagnosticLogic.Classify(
                RetentionClassifyInput(
                    retentionComparable: false,
                    retentionDriftMeters: float.NaN,
                    uniqueResolved: 3,
                    confirmedWriteKeys: 2,
                    actorEpochChangedAfterWrite: true));

            return bareEpochChange == "post-placement-visual-candidate"
                && unmatchedIdentity == "actor-recreation";
        });

        Test(670, "cold-start retention drift compares the same Character.Position field the placement write path writes and reads back", () =>
        {
            var root = FindRepositoryRoot();
            var probePath = Path.Combine(
                root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundCharacterSourceProbe.cs");
            var diagnosticPath = Path.Combine(
                root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.ColdStartDiagnostic.cs");
            var probeText = File.ReadAllText(probePath);
            var diagnosticText = File.ReadAllText(diagnosticPath);

            // Pure helper behaviour.
            var euclid = TitleBackgroundColdStartDiagnosticLogic.ComputeRetentionDrift(
                new System.Numerics.Vector3(0f, 0f, 0f), new System.Numerics.Vector3(3f, 0f, 4f));
            var nan = TitleBackgroundColdStartDiagnosticLogic.ComputeRetentionDrift(
                new System.Numerics.Vector3(float.NaN, 0f, 0f), System.Numerics.Vector3.Zero);

            return Math.Abs(euclid - 5f) < 0.0001f
                && float.IsNaN(nan)
                // Write path: GameObject.SetPosition, readback from Character.Position.
                && probeText.Contains("character->SetPosition(position.X, position.Y, position.Z)", StringComparison.Ordinal)
                && probeText.Contains("readBackPosition = new Vector3(", StringComparison.Ordinal)
                // Read path used by the retention observation: the same Character.Position field.
                && probeText.Contains("position = new Vector3(character->Position.X, character->Position.Y, character->Position.Z)", StringComparison.Ordinal)
                && diagnosticText.Contains("TryReadCharaSelectCharacterTransform reads the same Character.Position field", StringComparison.Ordinal)
                && diagnosticText.Contains("_charaSelectPlacement.LastAppliedPosition", StringComparison.Ordinal)
                && TitleBackgroundColdStartDiagnosticLogic.RetentionDriftEpsilonMeters
                    == TitleBackgroundCharaSelectPlacementLogic.CapturePositionEpsilon;
        });

        Test(671, "cold-start does no post-login actor/camera re-read: the retention transform read is pre-login CharaSelect only", () =>
        {
            var root = FindRepositoryRoot();
            var diagnosticPath = Path.Combine(
                root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.ColdStartDiagnostic.cs");
            var diagnosticText = File.ReadAllText(diagnosticPath);

            var updateBody = ExtractMethodBody(diagnosticText, "private void OnColdStartDiagnosticFrameworkUpdate(IFramework _)");
            var loginBranchStart = updateBody.IndexOf("if (_clientState.IsLoggedIn)", StringComparison.Ordinal);
            var charaSelectGuard = updateBody.IndexOf("currentMap != GameLobbyType.CharaSelect", StringComparison.Ordinal);
            var transformRead = updateBody.IndexOf("TryReadCharaSelectCharacterTransform(", StringComparison.Ordinal);
            var loginEvidence = updateBody.IndexOf("RecordLoginEvidence(", StringComparison.Ordinal);

            // Exactly one transform read in the whole recorder, and it sits inside the pre-login
            // Character-Select branch (after the logged-in early return and after the lobby guard).
            return CountOccurrences(diagnosticText, "TryReadCharaSelectCharacterTransform(") == 1
                && loginBranchStart >= 0 && charaSelectGuard >= 0 && transformRead >= 0 && loginEvidence >= 0
                && loginEvidence < charaSelectGuard
                && charaSelectGuard < transformRead
                && !diagnosticText.Contains("SceneCamera", StringComparison.Ordinal);
        });
    }
}
