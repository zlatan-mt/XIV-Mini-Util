// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectPlacementDiagnostics.cs
// Description: TitleEdit-informed Character Select placement path の 1 クリックレポート行を
//              単一ソースから機械生成する（実装と allowlist の乖離を防ぐ, skill §3）。
// Reason: PR #7 根本修正。raw config と actual run ownership を混同しない。
//         v2.active / placement.active は raw config ではなく実 owner semantics に合わせる。
//         completed-run proof snapshot（frozen）から出す（login/reset/restore で live が消えても正しい）。
//         character 名 / contentId / raw pointer は出さない。
namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundCharaSelectPlacementDiagnostics
{
    // allowlist と emitter の単一ソース。ここへ追加すれば自動で auto-copy report に載る。
    public static readonly string[] Keys =
    [
        // actual run ownership（raw config と区別する）。
        "automaticRun.engineOwner",
        "automaticRun.placementProofArmed",
        "automaticRun.v2Suppressed",
        "automaticRun.preLoginPlacementObserved",
        "automaticRun.reportSource",
        // lifecycle 診断（次の実機 1 回で失敗ステージを一意に特定する）。
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
        // owner semantics に合わせた active フラグ。
        "charaselect.placement.enabled",
        "charaselect.placement.active",
        "charaselect.placement.candidate",
        "charaselect.placement.candidateMatches",
        "charaselect.placement.targetScene",
        "charaselect.placement.targetPosition",
        "charaselect.placement.targetRotation",
        "charaselect.placement.resolve.source",
        "charaselect.placement.resolve.entryAvailable",
        "charaselect.placement.resolve.selectedContentAvailable",
        "charaselect.placement.resolve.mappingAvailable",
        "charaselect.placement.resolve.mappingHit",
        "charaselect.placement.resolve.clientObjectIndexValid",
        "charaselect.placement.resolve.objectResolved",
        "charaselect.placement.resolve.drawReady",
        "charaselect.placement.resolve.retryCount",
        "charaselect.placement.characterResolveStatus",
        "charaselect.placement.capture.positionCaptured",
        "charaselect.placement.capture.zeroPositionAccepted",
        "charaselect.placement.capture.stableSamples",
        "charaselect.placement.capture.timedOut",
        "charaselect.placement.applyCount",
        "charaselect.placement.trigger",
        "charaselect.placement.write.attemptCount",
        "charaselect.placement.write.retryBudget",
        "charaselect.placement.write.setterCallCompleted",
        "charaselect.placement.write.positionReadbackConfirmed",
        "charaselect.placement.write.rotationReadbackConfirmed",
        "charaselect.placement.write.confirmed",
        "charaselect.placement.write.status",
        "charaselect.placement.lastReason",
        "charaselect.placement.lastPreLoginReason",
        "charaselect.placement.legacyOwnershipInactive",
        "charaselect.placement.loginStopped",
        "charaselect.placement.disposeState",
        // Elpis candidate-specific source-backed layout / same-terrain position.
        "characterPlace.sourceMode",
        "characterPlace.sourceCandidateMatch",
        "characterPlace.sourceTerritoryTypeId",
        "characterPlace.sourceTerritoryMatch",
        "characterPlace.sourceTerritoryPathMatch",
        "characterPlace.sourceLayoutReady",
        "characterPlace.sourceLayoutInitState",
        "characterPlace.sourceLayoutTerritoryTypeId",
        "characterPlace.sourceLayoutTerritoryMatch",
        "characterPlace.sourceLayoutLayerFilterKey",
        "characterPlace.sourceLayerFilterKeyAvailable",
        "characterPlace.sourcePositionCaptured",
        "characterPlace.sourcePositionAuthorized",
        "characterPlace.sourceTarget",
        "characterPlace.sourceFailureReason",
        "characterPlace.sourceCaptureAttemptCount",
        "characterPlace.sourceCaptureRetryBudget",
        "characterPlace.sourceCaptureRetryPending",
        "characterPlace.sourceCaptureRetryExhausted",
        "characterPlace.loadedLayoutTerritoryTypeId",
        "characterPlace.loadedLayoutLayerFilterKey",
        "characterPlace.loadedLayoutTerritoryMatch",
        "characterPlace.loadedLayoutLayerFilterKeyMatch",
        // FRU user-approved static anchor（source-backed marker ではない）。
        "characterPlace.placementSourceMode",
        "characterPlace.staticAnchorCandidate",
        "characterPlace.staticAnchorHasAnchor",
        "characterPlace.staticAnchor",
        "characterPlace.staticAnchorFinite",
        "characterPlace.staticAnchorProvenance",
        "characterPlace.staticAnchorPreLogin",
        "characterPlace.staticAnchorCharaSelect",
        "characterPlace.staticAnchorSceneOverrideApplied",
        "characterPlace.staticAnchorExpectedScenePath",
        "characterPlace.staticAnchorAppliedScenePath",
        "characterPlace.staticAnchorExpectedTerritory",
        "characterPlace.staticAnchorAppliedTerritory",
        "characterPlace.staticAnchorExpectedLayoutTerritory",
        "characterPlace.staticAnchorLoadedLayoutTerritory",
        "characterPlace.staticAnchorExpectedLayerFilter",
        "characterPlace.staticAnchorAppliedLayerFilter",
        "characterPlace.staticAnchorLoadedLayerFilter",
        "characterPlace.staticAnchorActiveLayoutAvailable",
        "characterPlace.staticAnchorLayoutInitState",
        "characterPlace.staticAnchorLayoutReady",
        "characterPlace.staticAnchorSceneGeneration",
        "characterPlace.staticAnchorAuthorized",
        "characterPlace.staticAnchorAuthorizationReason",
        "characterPlace.staticAnchorFirstFailedGate",
    ];

    public static IEnumerable<string> Build(
        TitleBackgroundCharaSelectPlacementProofSnapshot proof,
        bool reportFromCompletedRunSnapshot,
        bool placementConfigEnabled,
        string candidateId,
        bool candidateMatches,
        string targetScene,
        string targetPosition,
        string targetRotation,
        bool disposed)
    {
        static string N(string value) => string.IsNullOrWhiteSpace(value) ? "none" : value;

        // REPORT SEMANTICS（PR #7 cleanup）: 「pre-login の最終 gate / reason 状態」を表すキーは、
        // placement が今回 run で resolve→capture→write→readback まで確定した後に起きる login 遷移中
        // （actor が破棄され zone load 中。IsLoggedIn はまだ false）の待機フレームで
        // object-resolve-timeout 等へ上書きされることがあり、placementProofVerdict=PASS と矛盾して見える。
        // confirmed proof が成立した run では、その上書き前の意味のある最終状態
        // （gate=ready / reason=applied）を出す。runtime の gate/placement ロジックは一切変更しない
        // （表示レイヤのみ）。firstPreLoginGateReason は初期の解決待ちの記録なので raw のまま残す。
        var placementConfirmedThisRun = proof.ConfirmedPlacementProof.Valid
            || (proof.ApplyCount > 0 && proof.WriteConfirmed);

        // owner semantics（raw config ではない）。
        yield return $"automaticRun.engineOwner={N(proof.EngineOwner)}";
        yield return $"automaticRun.placementProofArmed={proof.PlacementProofArmed}";
        yield return $"automaticRun.v2Suppressed={(string.Equals(proof.EngineOwner, "placement-proof", StringComparison.Ordinal))}";
        yield return $"automaticRun.preLoginPlacementObserved={proof.PreLoginPlacementObserved}";
        yield return $"automaticRun.reportSource={(reportFromCompletedRunSnapshot ? "completed-run-proof" : "live")}";
        yield return $"automaticRun.preLoginFrameworkFrameCount={proof.PreLoginFrameworkFrameCount}";
        yield return $"automaticRun.placementMaintainCallCount={proof.PlacementMaintainCallCount}";
        yield return $"automaticRun.placementGateEvaluationCount={proof.PlacementGateEvaluationCount}";
        yield return $"automaticRun.firstPreLoginGateReason={N(proof.FirstPreLoginGateReason)}";
        yield return $"automaticRun.lastPreLoginGateReason={(placementConfirmedThisRun ? "ready" : N(proof.LastPreLoginGateReason))}";
        yield return $"automaticRun.charaSelectSessionObserved={proof.CharaSelectSessionObserved}";
        yield return $"automaticRun.sceneGenerationObserved={proof.SceneGenerationObserved}";
        yield return $"automaticRun.quickCheckCollectingObserved={proof.QuickCheckCollectingObserved}";
        yield return $"automaticRun.logoutTransitionObserved={proof.LogoutTransitionObserved}";
        yield return $"automaticRun.ownerNotPlacementWhileArmedCount={proof.OwnerNotPlacementWhileArmedCount}";
        yield return $"automaticRun.attachedToActiveScene={proof.AttachedToActiveScene}";

        yield return $"charaselect.placement.enabled={placementConfigEnabled}";
        // active は owner semantics（proof or persistent placement）。
        yield return $"charaselect.placement.active={(string.Equals(proof.EngineOwner, "placement-proof", StringComparison.Ordinal) || string.Equals(proof.EngineOwner, "placement", StringComparison.Ordinal))}";
        yield return $"charaselect.placement.candidate={N(candidateId)}";
        yield return $"charaselect.placement.candidateMatches={candidateMatches}";
        yield return $"charaselect.placement.targetScene={N(targetScene)}";
        yield return $"charaselect.placement.targetPosition={N(targetPosition)}";
        yield return $"charaselect.placement.targetRotation={N(targetRotation)}";
        yield return $"charaselect.placement.resolve.source={N(proof.ResolveSource)}";
        yield return $"charaselect.placement.resolve.entryAvailable={proof.EntryAvailable}";
        yield return $"charaselect.placement.resolve.selectedContentAvailable={proof.SelectedContentAvailable}";
        yield return $"charaselect.placement.resolve.mappingAvailable={proof.MappingAvailable}";
        yield return $"charaselect.placement.resolve.mappingHit={proof.MappingHit}";
        yield return $"charaselect.placement.resolve.clientObjectIndexValid={proof.ClientObjectIndexValid}";
        yield return $"charaselect.placement.resolve.objectResolved={proof.ObjectResolved}";
        yield return $"charaselect.placement.resolve.drawReady={proof.DrawReady}";
        yield return $"charaselect.placement.resolve.retryCount={proof.RetryCount}";
        yield return $"charaselect.placement.characterResolveStatus={N(proof.CharacterResolveStatus)}";
        yield return $"charaselect.placement.capture.positionCaptured={proof.PositionCaptured}";
        yield return $"charaselect.placement.capture.zeroPositionAccepted={proof.ZeroPositionAccepted}";
        yield return $"charaselect.placement.capture.stableSamples={proof.CaptureStableSamples}";
        yield return $"charaselect.placement.capture.timedOut={proof.CaptureTimedOut}";
        yield return $"charaselect.placement.applyCount={proof.ApplyCount}";
        yield return $"charaselect.placement.trigger={N(proof.Trigger)}";
        yield return $"charaselect.placement.write.attemptCount={proof.WriteAttemptCount}";
        yield return $"charaselect.placement.write.retryBudget={proof.WriteRetryBudget}";
        yield return $"charaselect.placement.write.setterCallCompleted={proof.SetterCallCompleted}";
        yield return $"charaselect.placement.write.positionReadbackConfirmed={proof.PositionReadbackConfirmed}";
        yield return $"charaselect.placement.write.rotationReadbackConfirmed={proof.RotationReadbackConfirmed}";
        yield return $"charaselect.placement.write.confirmed={proof.WriteConfirmed}";
        yield return $"charaselect.placement.write.status={N(proof.WriteStatus)}";
        yield return $"charaselect.placement.lastReason={N(proof.LastReason)}";
        yield return $"charaselect.placement.lastPreLoginReason={(placementConfirmedThisRun ? "applied" : N(proof.LastPreLoginReason))}";
        yield return $"charaselect.placement.legacyOwnershipInactive={proof.LegacyOwnershipInactive}";
        yield return $"charaselect.placement.loginStopped={proof.LoginStopped}";
        yield return $"charaselect.placement.disposeState={(disposed ? "disposed" : "active")}";
    }
}
