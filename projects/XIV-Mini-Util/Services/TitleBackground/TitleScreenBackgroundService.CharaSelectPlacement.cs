// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.CharaSelectPlacement.cs
// Description: TitleEdit-informed Character Select placement path の runtime 部分。
//              CharaSelectService.TryResolveSelectedCharacterActor() が返す canonical identity を使い、
//              source-backed な scene-local 位置へキャラを (capture 完了) / (新 scene generation) /
//              (選択キャラ変更) のときだけ native GameObject.SetPosition + actor SetRotation で配置する。
// Reason: identity 責務は CharaSelectService へ一本化（PR #7 根本修正 点1〜点4）。TitleBackground は
//         独自 resolver を持たない。新規 native hook は追加しない（既存 detour を再利用）。
//         camera には一切書かない（FixOn passthrough が配置キャラを追従する）。
//         Position==(0,0,0) は正常値として許可する（点5）。
using ClientVector3 = System.Numerics.Vector3;
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    // SelectedCharacterChanged イベントで立てる。次の Maintain 呼び出しで消費し、1 回だけ再適用する。
    private bool _charaSelectSelectionChangePending;
    // object 生成が遅い場合の read-only bounded retry カウンタ（点3）。
    private int _charaSelectPlacementResolveRetries;
    private const int CharaSelectPlacementResolveRetryBudget = 120;

    // CharaSelectService.SelectedCharacterChanged の購読ハンドラ。detour スレッド上で呼ばれるため
    // フラグを立てるだけ（native 書込は Framework tick の Maintain で行う）。
    private void OnCharaSelectSelectionChanged()
    {
        _charaSelectSelectionChangePending = true;
        // キャラ切替でゲームが n4gw の SharedGroup を再 active 化しうるため、FRU suppression の
        // bounded window を次 pass で re-arm する（同一 scene generation でも）。
        // detour スレッドからの set は Framework スレッドの read→clear と race しうるので atomic に立てる。
        System.Threading.Interlocked.Exchange(ref _fruSuppressionSelectionChangePending, 1);
    }

    // actor pointer を触る TitleBackground 側の経路（新 placement path も legacy 診断経路も）は、
    // すべてこの canonical resolver だけを通す。独自の actor 探索・raw pointer bypass は持たない
    // （PR #7 根本修正 点1・点2: unresolved native source => no write / capture）。
    // 返り値 true は context.Valid を含意する。CharacterAddress は呼び出し frame 内だけで使う。
    private bool TryResolveCharaSelectActorContext(out CharaSelectResolvedActorContext actor)
    {
        actor = default;
        return _charaSelectService?.TryResolveCurrentCharaSelectActor(out actor) == true && actor.Valid;
    }

    // OnFrameworkUpdate から毎フレーム呼ぶが、実際の native 書込は bounded トリガのときだけ行う。login で恒久停止。
    private void MaintainTitleEditInformedCharaSelectPlacement()
    {
        var proofArmed = _automaticCheck.PlacementProofArmed;
        var preLogin = !_clientState.IsLoggedIn;
        if (proofArmed && preLogin)
        {
            _charaSelectPlacement.RecordPreLoginFrameworkFrame();
        }

        try
        {
            // 「メソッド自体が呼ばれていない」を切り分けるため、最初の guard より前で必ずカウント。
            _charaSelectPlacement.RecordMaintainCall();

            // legacy camera adapter は新エンジン owner のとき arm されず SceneGeneration が 0 のままになるため、
            // placement path は実際の CharaSelect scene 差し替え回数を数える独立カウンタで gate する。
            var runtimeSceneGeneration = _charaSelectPlacement.SceneGeneration;
            TryReadCurrentLobbyMap(out var currentMap);
            var isCharaSelectMap = TitleBackgroundCharaSelectPlacementLogic.IsCharaSelectMap(currentMap);

            if (!IsCharaSelectPlacementActive)
            {
                _charaSelectSelectionChangePending = false;
                _charaSelectPlacementResolveRetries = 0;
                if (proofArmed)
                {
                    // proof は armed なのに owner が placement でない = ownership 解決の不整合。
                    _charaSelectPlacement.RecordOwnerNotPlacementWhileArmed();
                    if (preLogin)
                    {
                        var ownerName = TitleBackgroundCharaSelectEngineOwnerLogic.Describe(CharaSelectEngineOwner);
                        _charaSelectPlacement.RecordSkip($"engine-owner-{ownerName}");
                        _charaSelectPlacement.RecordGateEvaluation($"engine-owner-{ownerName}", preLogin: true);
                    }
                }

                return;
            }

            // Gap B: proof arm より前に CharaSelect scene が既に生成済みだと、session/generation が
            // まだ本 path 用に確定していない（または adapter generation が後からずれている）ことがある。
            // 新 hook / scene reload を足さず、現在 active な CharaSelect scene generation へ
            // read-only で attach / 再同期する。CharaSelect map のときだけ。login 中はしない。
            var attachedThisFrame = false;
            if (isCharaSelectMap
                && !_clientState.IsLoggedIn
                && runtimeSceneGeneration > 0
                && (!_charaSelectTitleBackgroundSessionActive
                    || _activeCharaSelectSceneGeneration != runtimeSceneGeneration))
            {
                var previousGeneration = _activeCharaSelectSceneGeneration;
                _charaSelectTitleBackgroundSessionActive = true;
                _activeCharaSelectSceneGeneration = runtimeSceneGeneration;
                attachedThisFrame = true;
                RecordTransitionEvent(
                    "charaselect placement attached to active scene",
                    $"generation={runtimeSceneGeneration}; previous={previousGeneration}");
            }

            _charaSelectPlacement.ObserveLifecycle(
                charaSelectSessionActive: _charaSelectTitleBackgroundSessionActive,
                activeSceneGeneration: _activeCharaSelectSceneGeneration,
                quickCheckCollecting: _automaticCheck.State == TitleBackgroundAutomaticCheckState.Collecting,
                logoutTransition: proofArmed && preLogin && isCharaSelectMap,
                attachedToActiveScene: attachedThisFrame || _charaSelectPlacement.AttachedToActiveScene);

            var activeCandidateId = ResolveCurrentOverrideCandidate().Id;
            var placementCandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(
                _configuration.TitleBackgroundCharaSelectPlacementCandidateId);
            var candidateMatches = !string.IsNullOrEmpty(placementCandidateId)
                && string.Equals(placementCandidateId, activeCandidateId, StringComparison.Ordinal);

            // この frame の Capture/Write/Readback が共有する唯一の canonical actor context。
            // CharacterAddress はこのメソッドを越えて保存しない。
            var actor = default(CharaSelectResolvedActorContext);
            _ = _charaSelectService?.TryResolveCurrentCharaSelectActor(out actor);
            var resolvedContext = new TitleBackgroundResolvedActorContext(
                actor,
                PlacementPathActive: true,
                PreLogin: preLogin,
                ServiceReady: _hookLifecycle.State == TitleBackgroundServiceState.Ready,
                HookProbeMode: IsHookProbeMode(),
                CharaSelectSessionActive: _charaSelectTitleBackgroundSessionActive,
                ActiveSceneGeneration: _activeCharaSelectSceneGeneration,
                RuntimeSceneGeneration: runtimeSceneGeneration,
                IsCharaSelectMap: isCharaSelectMap,
                CandidateId: placementCandidateId,
                CandidateMatches: candidateMatches);
            var actorIdentityChanged = actor.Valid
                && actor.IdentityKey != _charaSelectPlacement.LastAppliedActorKey;

            // Resolver の待機は capture の安定サンプル時間とは別の bounded budget にする。
            // mapping 未生成・object 未生成のどちらも同じ run-scoped retry として数える。
            if (!actor.Valid)
            {
                _charaSelectPlacementResolveRetries = Math.Min(
                    CharaSelectPlacementResolveRetryBudget,
                    _charaSelectPlacementResolveRetries + 1);
            }
            else
            {
                _charaSelectPlacementResolveRetries = 0;
            }

            var resolveTimedOut = _charaSelectPlacementResolveRetries >= CharaSelectPlacementResolveRetryBudget;

            // resolver 診断は pre-login のものだけ記録する。login フレームでは
            // TryResolveSelectedCharacterActor が「login 済み」ガードで即 false を返すため、
            // ここで記録すると直前まで正しかった pre-login の resolve 結果（source / mappingHit 等）を
            // None で上書きし、直後の MarkLoginStopped→CaptureCompletedRunProofSnapshot が
            // その誤った値を freeze してしまう（点9 / Review 3: 1 レポートで失敗ステージを一意特定）。
            if (preLogin)
            {
                _charaSelectPlacement.RecordCharacterResolve(
                    resolvedContext,
                    _charaSelectPlacementResolveRetries);
            }

            // resolver-valid actor から stable capture を先に作り、同じ context でのみ後段 write を許可する。
            CaptureCharaSelectPlacementProof(resolvedContext);
            var captureProofValid = _charaSelectPlacement.CaptureProofMatches(resolvedContext);
            // FRU など user-approved static anchor 候補は、ここで scene/layout identity を read-only 評価して
            // anchor を認可する（login 後は no-op で直前の pre-login 評価を凍結）。
            EvaluateFruStaticAnchorAuthorization(runtimeSceneGeneration, isCharaSelectMap);
            var model = BuildCurrentCharaSelectLocationModel();

            var gate = TitleBackgroundCharaSelectPlacementLogic.ResolveGate(
                placementPathActive: true,
                serviceReady: _hookLifecycle.State == TitleBackgroundServiceState.Ready,
                hookProbeMode: IsHookProbeMode(),
                loggedIn: _clientState.IsLoggedIn,
                charaSelectSessionActive: _charaSelectTitleBackgroundSessionActive,
                activeSceneGeneration: _activeCharaSelectSceneGeneration,
                runtimeSceneGeneration: runtimeSceneGeneration,
                isCharaSelectMap: isCharaSelectMap,
                candidateMatches: candidateMatches,
                hasSourceBackedPosition: model.HasSourceBackedPosition,
                characterResolved: actor.Valid && !resolveTimedOut,
                captureProofValid: captureProofValid);

            _charaSelectPlacement.RecordGateEvaluation(
                resolveTimedOut ? "object-resolve-timeout" : gate.Reason,
                preLogin: preLogin);

            switch (gate.Decision)
            {
                case TitleBackgroundCharaSelectEngineDecision.Stop:
                    if (_clientState.IsLoggedIn)
                    {
                        // OneClick は通常ログイン中に開始される。まだ 1 度も logout→CharaSelect を
                        // 観測していない（LogoutTransitionObserved=false）なら、これは run 開始時点の
                        // ログインであって terminal login ではない。freeze せず待機する。
                        if (!_charaSelectPlacement.LogoutTransitionObserved)
                        {
                            _charaSelectPlacement.RecordSkip("awaiting-logout");
                            _charaSelectSelectionChangePending = false;
                            return;
                        }

                        if (!_charaSelectPlacement.LoginStopped)
                        {
                            // 1. pre-login proof を freeze  2. completed-run snapshot を確定  3. native write 停止
                            //    （runtime reset は report 生成後の CompleteAutomaticQuickCheck finally で行う）。
                            _charaSelectPlacement.MarkLoginStopped();
                            CaptureCompletedRunProofSnapshot("login");
                            RecordTransitionEvent("charaselect placement stopped", gate.Reason);
                        }
                    }
                    else
                    {
                        _charaSelectPlacement.RecordSkip(gate.Reason);
                    }

                    _charaSelectSelectionChangePending = false;
                    return;
                case TitleBackgroundCharaSelectEngineDecision.Skip:
                    _charaSelectPlacement.RecordSkip(
                        resolveTimedOut ? "object-resolve-timeout" : gate.Reason);
                    return;
            }

            var captureJustCompleted = _charaSelectPlacement.CaptureCompletionPendingApply;
            var placementSelectionChangePending = _charaSelectSelectionChangePending
                || actorIdentityChanged;

            if (!TitleBackgroundCharaSelectPlacementLogic.ShouldWritePlacement(
                    _activeCharaSelectSceneGeneration,
                    _charaSelectPlacement.LastAppliedSceneGeneration,
                    actor.IdentityKey,
                    _charaSelectPlacement.LastAppliedActorKey,
                    captureJustCompleted,
                    placementSelectionChangePending))
            {
                _charaSelectSelectionChangePending = false;
                _charaSelectPlacement.RecordSkip("already-applied");
                return;
            }

            var trigger = TitleBackgroundCharaSelectPlacementLogic.ResolvePlacementTrigger(
                _activeCharaSelectSceneGeneration,
                _charaSelectPlacement.LastAppliedSceneGeneration,
                actor.IdentityKey,
                _charaSelectPlacement.LastAppliedActorKey,
                captureJustCompleted,
                placementSelectionChangePending);

            var writeAuthorization = TitleBackgroundCharaSelectPlacementLogic.EvaluateWriteAuthorization(
                resolvedContext.Valid,
                captureProofValid,
                _charaSelectPlacement.CaptureGenerationMatches(resolvedContext),
                _charaSelectPlacement.CaptureActorIdentityMatches(resolvedContext),
                _charaSelectPlacement.CaptureCandidateMatches(resolvedContext));
            _charaSelectPlacement.RecordWriteAuthorization(
                writeAuthorization,
                actor.Source.ToString());
            if (!writeAuthorization.Allowed)
            {
                _charaSelectPlacement.RecordSkip($"write-{writeAuthorization.Reason}");
                return;
            }

            if (!_charaSelectPlacement.CanAttemptPlacementWrite(
                    _activeCharaSelectSceneGeneration,
                    actor.IdentityKey,
                    placementCandidateId))
            {
                _charaSelectSelectionChangePending = false;
                _charaSelectPlacement.RecordSkip("write-retry-exhausted");
                return;
            }

            var position = model.Position;
            var positionSetterReturned = TitleBackgroundCharacterSourceProbe.TrySetCharaSelectCharacterPosition(
                actor,
                position,
                out var positionReadBack,
                out var positionSetterCompleted);
            var positionReadBackConfirmed = positionSetterReturned
                && TitleBackgroundCharaSelectPlacementLogic.IsPositionReadbackWithinEpsilon(
                    position,
                    positionReadBack);
            if (!positionReadBackConfirmed)
            {
                _charaSelectPlacement.RecordPlacementWriteAttempt(
                    _activeCharaSelectSceneGeneration,
                    actor.IdentityKey,
                    placementCandidateId,
                    positionSetterCompleted,
                    positionReadBackConfirmed,
                    rotationReadbackConfirmed: false,
                    status: positionSetterCompleted ? "position-readback-mismatch" : "position-setter-failed");
                var failure = positionSetterCompleted
                    ? "position-readback-mismatch"
                    : "position-setter-failed";
                _charaSelectPlacement.RecordSkip($"write-{failure}");
                RecordTransitionEvent("charaselect placement failed", failure);
                return;
            }

            // rotation は actor 経路（SetRotation(float)）で書く。DrawObject quaternion は自前合成しない。
            var rotationSetterReturned = TitleBackgroundCharacterSourceProbe.TrySetCharaSelectCharacterRotation(
                actor,
                model.Rotation,
                out var rotationReadBack,
                out var rotationSetterCompleted);
            var rotationReadBackConfirmed = rotationSetterReturned
                && TitleBackgroundCharaSelectPlacementLogic.IsRotationReadbackWithinEpsilon(
                    model.Rotation,
                    rotationReadBack);
            if (!rotationReadBackConfirmed)
            {
                _charaSelectPlacement.RecordPlacementWriteAttempt(
                    _activeCharaSelectSceneGeneration,
                    actor.IdentityKey,
                    placementCandidateId,
                    positionSetterCompleted && rotationSetterCompleted,
                    positionReadBackConfirmed,
                    rotationReadBackConfirmed,
                    status: rotationSetterCompleted ? "rotation-readback-mismatch" : "rotation-setter-failed");
                var failure = rotationSetterCompleted
                    ? "rotation-readback-mismatch"
                    : "rotation-setter-failed";
                _charaSelectPlacement.RecordSkip($"write-{failure}");
                RecordTransitionEvent("charaselect placement failed", failure);
                return;
            }

            _charaSelectPlacement.RecordPlacementWriteAttempt(
                _activeCharaSelectSceneGeneration,
                actor.IdentityKey,
                placementCandidateId,
                setterCallCompleted: positionSetterCompleted && rotationSetterCompleted,
                positionReadbackConfirmed: positionReadBackConfirmed,
                rotationReadbackConfirmed: rotationReadBackConfirmed,
                status: "confirmed");
            _charaSelectPlacement.RecordConfirmedPlacementProof(
                resolvedContext,
                model.Position,
                model.Rotation);

            _charaSelectSelectionChangePending = false;
            var frame = GetCurrentPhase2CFrame() ?? -1;
            _charaSelectPlacement.RecordPlacementApplied(
                _activeCharaSelectSceneGeneration,
                actor.IdentityKey,
                placementCandidateId,
                new ClientVector3(position.X, position.Y, position.Z),
                model.Rotation,
                frame,
                trigger: trigger.ToString());
            RecordTransitionEvent(
                "charaselect placement applied",
                $"gen={_activeCharaSelectSceneGeneration}; count={_charaSelectPlacement.PlacementApplyCount}; trigger={trigger}");
        }
        catch (Exception ex)
        {
            _charaSelectPlacement.RecordSkip($"exception:{ex.GetType().Name}");
            MarkRuntimeError(ex, nameof(MaintainTitleEditInformedCharaSelectPlacement));
        }
    }

    // auto-copy report / fallback の固定ヘッダ用: 実 owner・proof arm 状態・直近の character 解決結果を
    // diagnostic selector を通さずに 1 行で出す（raw pointer / ContentId / 名前は出さない）。
    // completed-run proof snapshot があればそこから（live runtime が reset されても正しい）。
    internal string BuildCharaSelectPlacementRuntimeProofLine()
    {
        var completed = _automaticCheck.CompletedRunProof;
        var hasCompletedProof = completed.HasValue;
        var completedProof = completed.GetValueOrDefault();
        var owner = hasCompletedProof && !string.IsNullOrWhiteSpace(completedProof.EngineOwner)
            ? completedProof.EngineOwner
            : TitleBackgroundCharaSelectEngineOwnerLogic.Describe(CharaSelectEngineOwner);
        var placementOwner = string.Equals(owner, "placement-proof", StringComparison.Ordinal)
            || string.Equals(owner, "placement", StringComparison.Ordinal);
        var proofArmed = hasCompletedProof
            ? completedProof.PlacementProofArmed
            : _automaticCheck.PlacementProofArmed;
        var resolveStatus = hasCompletedProof ? completedProof.CharacterResolveStatus : _charaSelectPlacement.LastCharacterResolveStatus;
        var resolveSource = hasCompletedProof ? completedProof.ResolveSource : _charaSelectPlacement.LastResolveSource;
        var applyCount = hasCompletedProof ? completedProof.ApplyCount : _charaSelectPlacement.PlacementApplyCount;
        var trigger = hasCompletedProof ? completedProof.Trigger : _charaSelectPlacement.LastPlacementTrigger;
        var lastReason = hasCompletedProof ? completedProof.LastReason : _charaSelectPlacement.LastReason;
        var candidate = hasCompletedProof && !string.IsNullOrWhiteSpace(completedProof.CandidateId)
            ? completedProof.CandidateId
            : _configuration.TitleBackgroundCharaSelectPlacementCandidateId;
        var positionCaptured = hasCompletedProof
            ? completedProof.PositionCaptured
            : _configuration.TitleBackgroundCharaSelectPlacementPositionCaptured;
        var candidateMatches = hasCompletedProof
            ? completedProof.CandidateMatches
            : !string.IsNullOrEmpty(candidate)
                && string.Equals(
                    TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(candidate),
                    ResolveCurrentOverrideCandidate().Id,
                    StringComparison.Ordinal);
        var targetScene = hasCompletedProof && !string.IsNullOrWhiteSpace(completedProof.TargetScene)
            ? completedProof.TargetScene
            : _validatedTerritoryPath;
        var targetPosition = hasCompletedProof && completedProof.PositionCaptured
            ? FormatVector(completedProof.TargetPosition)
            : positionCaptured
                ? FormatVector(new ClientVector3(
                    _configuration.TitleBackgroundCharaSelectPlacementPositionX,
                    _configuration.TitleBackgroundCharaSelectPlacementPositionY,
                    _configuration.TitleBackgroundCharaSelectPlacementPositionZ))
                : "none";
        var targetRotation = hasCompletedProof && completedProof.PositionCaptured
            ? completedProof.TargetRotation.ToString("0.###")
            : positionCaptured
                ? _configuration.TitleBackgroundCharaSelectPlacementRotation.ToString("0.###")
                : "none";
        var writeConfirmed = hasCompletedProof
            ? completedProof.WriteConfirmed
            : _charaSelectPlacement.LastWriteReadbackConfirmed;
        var writeStatus = hasCompletedProof ? completedProof.WriteStatus : _charaSelectPlacement.LastWriteStatus;
        var writeAttempts = hasCompletedProof
            ? completedProof.WriteAttemptCount
            : _charaSelectPlacement.PlacementWriteAttemptCount;
        var promotionStatus = _automaticCheck.PlacementPromotionStatus;
        var promotionReason = _automaticCheck.PlacementPromotionReason;

        return "charaselect.placement.proof "
            + $"engineOwner={FormatNone(owner)} "
            + $"active={placementOwner} "
            + $"placementProofArmed={proofArmed} "
            + $"reportSource={(hasCompletedProof ? "completed-run-proof" : "live")} "
            + $"enabled={_configuration.TitleBackgroundCharaSelectPlacementEnabled} "
            + $"v2Enabled={_configuration.TitleBackgroundV2Enabled} "
            + $"overrideEnabled={_configuration.TitleBackgroundOverrideEnabled} "
            + $"candidate={FormatNone(candidate)} "
            + $"candidateMatches={candidateMatches} "
            + $"targetScene={FormatNone(targetScene)} "
            + $"targetPosition={targetPosition} "
            + $"targetRotation={targetRotation} "
            + $"positionCaptured={positionCaptured} "
            + $"sceneGenerationObserved={(hasCompletedProof ? completedProof.SceneGenerationObserved : _charaSelectPlacement.SceneGenerationObserved)} "
            + $"captureStableSamples={(hasCompletedProof ? completedProof.CaptureStableSamples : _charaSelectPlacement.CaptureStableSamplesAtPersist)} "
            + $"characterResolveStatus={FormatNone(resolveStatus)} "
            + $"resolveSource={FormatNone(resolveSource)} "
            + $"applyCount={applyCount} "
            + $"trigger={FormatNone(trigger)} "
            + $"lastReason={FormatNone(lastReason)} "
            + $"writeAttemptCount={writeAttempts} "
            + $"writeStatus={FormatNone(writeStatus)} "
            + $"writeConfirmed={writeConfirmed} "
            + $"promotionEligible={_automaticCheck.PlacementPromotionEligible} "
            + $"promotionPersisted={_automaticCheck.PlacementPromotionPersisted} "
            + $"promotionStatus={FormatNone(promotionStatus)} "
            + $"promotionReason={FormatNone(promotionReason)}";
    }

    // report の QuickCheck セクション先頭へ差し込む固定行（selector 非経由）。
    internal IReadOnlyList<string> BuildAutomaticCheckRuntimeProofLines()
    {
        return
        [
            BuildCharaSelectPlacementRuntimeProofLine(),
        ];
    }

    // legacy ownership が新エンジン active 中に一切作動していないか。
    // 自動 proof の report では累積値ではなく今回 run の差分を使い、過去 run の legacy write を
    // current proof の失敗理由へ持ち込まない。通常診断では従来どおり累積値を保持する。
    internal bool ComputeCharaSelectPlacementLegacyOwnershipInactive()
    {
        if (!IsCharaSelectPlacementActive || _savedViewPoseMaintain.Active)
        {
            return false;
        }

        var runScoped = IsRunScopedQuickCheckActive();
        var phase2GApplied = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedCount(
            runScoped,
            _phaseRecording.Phase2GGenerationOverrideSetMidAppliedCount
                + _phaseRecording.Phase2GGenerationOverrideLowHighAppliedCount,
            _quickCheckState.Phase2GApplyCountStart);
        var characterPlacementApplied = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedPlacementCount(
            runScoped,
            _characterPlacement.CharaSelectCharacterPlacementCount,
            _quickCheckState.CharacterPlacementCountStart);

        return phase2GApplied == 0
            && characterPlacementApplied == 0;
    }

    // 1 クリックレポート用の placement/source 診断行。既存 placement proof と
    // candidate-specific source-backed layout snapshot を同じ auto-copy 経路へ載せる。
    // 早期 return 経路・詳細経路の両方から呼ぶ。
    // completed-run proof snapshot があればそこから出す（live runtime が reset/restore で消えても正しい）。
    internal IEnumerable<string> BuildCharaSelectPlacementDiagnosticLines()
    {
        var completed = _automaticCheck.CompletedRunProof;
        var reportFromSnapshot = completed.HasValue;
        var completedProof = completed.GetValueOrDefault();
        var candidateId = reportFromSnapshot && !string.IsNullOrWhiteSpace(completedProof.CandidateId)
            ? completedProof.CandidateId
            : _configuration.TitleBackgroundCharaSelectPlacementCandidateId;
        var candidateMatches = reportFromSnapshot
            ? completedProof.CandidateMatches
            : !string.IsNullOrEmpty(candidateId)
                && string.Equals(
                    TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(candidateId),
                    ResolveCurrentOverrideCandidate().Id,
                    StringComparison.Ordinal);
        var positionCaptured = reportFromSnapshot
            ? completedProof.PositionCaptured
            : _configuration.TitleBackgroundCharaSelectPlacementPositionCaptured;
        var targetPositionValue = reportFromSnapshot && completedProof.PositionCaptured
            ? completedProof.TargetPosition
            : new System.Numerics.Vector3(
                _configuration.TitleBackgroundCharaSelectPlacementPositionX,
                _configuration.TitleBackgroundCharaSelectPlacementPositionY,
                _configuration.TitleBackgroundCharaSelectPlacementPositionZ);
        var targetPosition = positionCaptured ? FormatVector(targetPositionValue) : "none";
        var targetRotationValue = reportFromSnapshot && completedProof.PositionCaptured
            ? completedProof.TargetRotation
            : _configuration.TitleBackgroundCharaSelectPlacementRotation;
        var targetRotation = positionCaptured ? targetRotationValue.ToString("0.###") : "none";
        var targetScene = reportFromSnapshot && !string.IsNullOrWhiteSpace(completedProof.TargetScene)
            ? completedProof.TargetScene
            : _validatedTerritoryPath;
        var proof = completed ?? _charaSelectPlacement.CaptureProofSnapshot(
            TitleBackgroundCharaSelectEngineOwnerLogic.Describe(CharaSelectEngineOwner),
            _automaticCheck.PlacementProofArmed,
            positionCaptured,
            ComputeCharaSelectPlacementLegacyOwnershipInactive(),
            candidateId,
            candidateMatches,
            targetScene,
            targetPositionValue,
            targetRotationValue);

        var placementSourceMode = TitleBackgroundCharaSelectStaticAnchorLogic.ResolvePlacementSourceMode(
            ResolveCurrentOverrideCandidate());

        return TitleBackgroundCharaSelectPlacementDiagnostics.Build(
            proof,
            reportFromSnapshot,
            placementConfigEnabled: _configuration.TitleBackgroundCharaSelectPlacementEnabled,
            candidateId: candidateId,
            candidateMatches: candidateMatches,
            targetScene: targetScene,
            targetPosition: targetPosition,
            targetRotation: targetRotation,
            disposed: _hookLifecycle.Disposed)
            .Concat(_charaSelectSourceLayout.BuildDiagnosticLines(
                _lastOverrideApplied && _lastOverrideLobbyType == GameLobbyType.CharaSelect,
                _lastOverrideTerritoryId,
                _lastOverrideLayerFilterKey))
            .Concat(_charaSelectStaticAnchor.BuildDiagnosticLines(placementSourceMode))
            .Concat(_charaSelectSceneObjectSuppression.BuildDiagnosticLines(
                ResolveCurrentOverrideCandidate().Id,
                string.Equals(
                    ResolveCurrentOverrideCandidate().Id,
                    TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                    StringComparison.Ordinal)));
    }

    // config の保存値から source-backed な LocationModel-lite を組み立てる。
    private TitleBackgroundCharaSelectLocationModel BuildCurrentCharaSelectLocationModel()
    {
        // proof run は必ず今回の read-only capture を先に完了させる。既存の persistent 値を
        // proof 中の write target に流すと、古い座標で先に actor を動かし、その動かした値を
        // 今回の source capture として再取得する循環になるため、config は fallback にしない。
        var proofRun = _automaticCheck.PlacementProofArmed;
        var activeCandidate = ResolveCurrentOverrideCandidate();

        // FRU など user-approved static anchor 候補は、proof run / 永続運用のどちらでも「今回の
        // pre-login scene/layout authorization を通った anchor」だけを position に使う。
        // 永続化された Position へは fallback しない（override/layout が壊れている状態で別 scene へ
        // (100,0,100) を書く経路を残さないため）。rotation は既存 canonical stable capture 由来
        // （proof run で採取、成功時に config へ永続化された値）。anchor 候補は「キャラの現在 lobby
        // 位置」を絶対に source にしない。
        if (activeCandidate.ApprovedStaticAnchor.HasValue)
        {
            var anchorAuthorized = _charaSelectStaticAnchor.TryGetAuthorizedAnchor(
                activeCandidate.Id,
                out var approvedAnchor);
            var anchorReady = anchorAuthorized
                && (!proofRun || _charaSelectPlacement.CaptureCompleted);
            var anchorRotation = proofRun
                ? (_charaSelectPlacement.CaptureCompleted ? _charaSelectPlacement.CapturedRotation : 0f)
                : _configuration.TitleBackgroundCharaSelectPlacementRotation;
            return TitleBackgroundCharaSelectPlacementLogic.BuildLocationModel(
                _validatedTerritoryPath,
                _configuration.TitleBackgroundLayoutTerritoryTypeId,
                _configuration.TitleBackgroundLayoutLayerFilterKey,
                anchorReady,
                approvedAnchor.X,
                approvedAnchor.Y,
                approvedAnchor.Z,
                anchorRotation);
        }

        var sourcePosition = default(System.Numerics.Vector3);
        var useSourceBackedRunPosition = proofRun
            && activeCandidate.RequiresSourceBackedLayout
            && _charaSelectSourceLayout.TryGetPosition(activeCandidate.Id, out sourcePosition);
        var useRunScopedCapture = proofRun && _charaSelectPlacement.CaptureCompleted;
        var positionCaptured = proofRun
            ? useSourceBackedRunPosition || useRunScopedCapture
            : _configuration.TitleBackgroundCharaSelectPlacementPositionCaptured;
        var position = useSourceBackedRunPosition
            ? sourcePosition
            : useRunScopedCapture
            ? _charaSelectPlacement.CapturedPosition
            : proofRun
                ? default
            : new System.Numerics.Vector3(
                _configuration.TitleBackgroundCharaSelectPlacementPositionX,
                _configuration.TitleBackgroundCharaSelectPlacementPositionY,
                _configuration.TitleBackgroundCharaSelectPlacementPositionZ);
        var rotation = useRunScopedCapture
            ? _charaSelectPlacement.CapturedRotation
            : proofRun
                ? 0f
            : _configuration.TitleBackgroundCharaSelectPlacementRotation;

        return TitleBackgroundCharaSelectPlacementLogic.BuildLocationModel(
            _validatedTerritoryPath,
            _configuration.TitleBackgroundLayoutTerritoryTypeId,
            _configuration.TitleBackgroundLayoutLayerFilterKey,
            positionCaptured,
            position.X,
            position.Y,
            position.Z,
            rotation);
    }

    // 同じ Framework frame で canonical resolve 済みの actor だけから位置/回転を read-only 採取する。
    // 安定サンプル（連続 N フレーム）を満たすまで write は許可しない。pointer は state へ保存しない。
    // proof run では capture 値を target に使い、persistent path では config target を書く前の
    // resolver-authorized actor proof として使う。(0,0,0) は正常値として許可する。
    private void CaptureCharaSelectPlacementProof(in TitleBackgroundResolvedActorContext context)
    {
        try
        {
            if (!IsCharaSelectPlacementActive
                || _clientState.IsLoggedIn
                || _charaSelectPlacement.CaptureTimedOut)
            {
                return;
            }

            if (_charaSelectPlacement.CaptureCompleted)
            {
                if (_charaSelectPlacement.CaptureProofMatches(context))
                {
                    return;
                }

                // latest attempt が一時的に unresolved でも既存 proof を破壊しない。
                // valid な別 identity / generation / candidate が現れた場合だけ新しい streak を開始する。
                if (!context.Valid)
                {
                    return;
                }

                _charaSelectPlacement.ResetCaptureProof();
            }

            if (!context.Valid)
            {
                _charaSelectPlacement.ResetCaptureSampling();
                var reason = !context.Actor.Valid
                    ? "capture-invalid:character-unresolved"
                    : !context.CandidateMatches
                        ? "capture-invalid:candidate-mismatch"
                        : !context.SceneGenerationMatches
                            ? "capture-invalid:scene-generation-mismatch"
                            : "capture-invalid:context";
                _charaSelectPlacement.RecordSkip(reason);
                return;
            }

            // actor recreation / identity / generation / candidate / source 変化では streak を取り直す。
            // resolver 成立後の bounded capture clock は維持する。
            if (!_charaSelectPlacement.CaptureIdentityMatches(
                    context.Actor.IdentityKey,
                    context.RuntimeSceneGeneration,
                    context.CandidateId,
                    context.Actor.Source.ToString()))
            {
                _charaSelectPlacement.ResetCaptureStreak();
            }

            var framesElapsed = _charaSelectPlacement.RecordCaptureSamplingAttempt();
            if (!TitleBackgroundCharacterSourceProbe.TryReadCharaSelectCharacterTransform(
                    context.Actor, out var capturedPosition, out var capturedRotation))
            {
                _charaSelectPlacement.ResetCaptureStreak();
                if (TitleBackgroundCharaSelectPlacementLogic.IsCaptureBudgetExceeded(framesElapsed))
                {
                    _charaSelectPlacement.MarkCaptureTimedOut();
                    _charaSelectPlacement.RecordSkip("capture-timeout");
                    RecordTransitionEvent("charaselect placement capture timeout", $"frames={framesElapsed}");
                }
                else
                {
                    _charaSelectPlacement.RecordSkip("capture-invalid:transform-read-failed");
                }
                return;
            }

            var validity = TitleBackgroundCharaSelectPlacementLogic.EvaluateCaptureValidity(
                context.Actor.MappingHit,
                context.Actor.ObjectResolved,
                context.Actor.DrawReady,
                context.ActiveSceneGeneration,
                context.RuntimeSceneGeneration,
                capturedPosition,
                capturedRotation);
            if (validity != "ok")
            {
                _charaSelectPlacement.ResetCaptureStreak();
                if (TitleBackgroundCharaSelectPlacementLogic.IsCaptureBudgetExceeded(framesElapsed))
                {
                    _charaSelectPlacement.MarkCaptureTimedOut();
                    _charaSelectPlacement.RecordSkip("capture-timeout");
                    RecordTransitionEvent("charaselect placement capture timeout", $"frames={framesElapsed}");
                }
                else
                {
                    _charaSelectPlacement.RecordSkip($"capture-invalid:{validity}");
                }
                return;
            }

            var streak = TitleBackgroundCharaSelectPlacementLogic.EvaluateCaptureSampleStreak(
                _charaSelectPlacement.CaptureHasPreviousSample,
                _charaSelectPlacement.CaptureLastSamplePosition,
                _charaSelectPlacement.CaptureLastSampleRotation,
                capturedPosition,
                capturedRotation,
                _charaSelectPlacement.CaptureSampleStreak);
            _charaSelectPlacement.RecordCaptureSample(streak, capturedPosition, capturedRotation, framesElapsed);
            _charaSelectPlacement.RecordCaptureSampleIdentity(
                context.Actor.IdentityKey,
                context.RuntimeSceneGeneration,
                context.CandidateId,
                context.Actor.Source.ToString());

            if (!TitleBackgroundCharaSelectPlacementLogic.IsCaptureStreakSatisfied(streak))
            {
                // bounded timeout: 安定 5 連続へ到達しなければ fail-closed（既存 capture を書かない）。
                if (TitleBackgroundCharaSelectPlacementLogic.IsCaptureBudgetExceeded(framesElapsed))
                {
                    _charaSelectPlacement.MarkCaptureTimedOut();
                    _charaSelectPlacement.RecordSkip("capture-timeout");
                    RecordTransitionEvent("charaselect placement capture timeout", $"frames={framesElapsed}");
                }

                return;
            }

            // 安定 5 連続到達 + 候補一致 + finite。ここでは run-scoped に確定するだけで、
            // 既存 config を上書きしない。promotion の全条件が揃った後に restore 後保存する。
            var sanitizedX = TitleBackgroundPreset.SanitizeCoordinate(capturedPosition.X);
            var sanitizedY = TitleBackgroundPreset.SanitizeCoordinate(capturedPosition.Y);
            var sanitizedZ = TitleBackgroundPreset.SanitizeCoordinate(capturedPosition.Z);
            var sanitizedRotation = TitleBackgroundPreset.SanitizeCoordinate(capturedRotation);

            var zeroAccepted = TitleBackgroundCharacterSourceEvaluation.IsZeroPosition(capturedPosition);
            _charaSelectPlacement.RecordCapturePersisted(
                streak,
                zeroAccepted,
                new ClientVector3(sanitizedX, sanitizedY, sanitizedZ),
                sanitizedRotation,
                context);
            RecordTransitionEvent(
                "charaselect placement evidence captured",
                $"stableSamples={streak}; zeroAccepted={zeroAccepted}");
            _log.Information(
                "[XMU BG] CharaSelect placement evidence captured. candidate={Candidate}, stableSamples={Samples}, zeroAccepted={Zero}",
                context.CandidateId,
                streak,
                zeroAccepted);
        }
        catch (Exception ex)
        {
            MarkRuntimeError(ex, nameof(CaptureCharaSelectPlacementProof));
        }
    }
}
