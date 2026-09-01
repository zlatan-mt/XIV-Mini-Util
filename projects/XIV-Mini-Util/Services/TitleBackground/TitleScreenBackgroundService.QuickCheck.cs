// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.QuickCheck.cs
// Description: TitleBackground の QuickCheck 実行状態と評価入力の組み立てを提供する
// Reason: QuickCheck 統合処理を TitleScreenBackgroundService の本体状態管理から分離するため
using System.Text;
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    internal IReadOnlyList<string> StartAutomaticQuickCheck()
    {
        var previousRestore = RestoreAutomaticCheckSettingsOnce(
            "restart-before-new-run",
            reloadNativeIntegration: true);
        if (!previousRestore.SettingsRestored
            || !previousRestore.RuntimeReloaded
            || File.Exists(AutomaticCheckRecoveryPath))
        {
            _automaticCheck.State = TitleBackgroundAutomaticCheckState.Failed;
            _automaticCheck.Status = "前回の設定を復元できないため、新しい自動確認を開始しませんでした。";
            return ["[XMU AutoCheck] FAILED", _automaticCheck.Status];
        }

        if (!TryBeginAutomaticCheckSettingsTransaction(out var transactionError))
        {
            _automaticCheck.State = TitleBackgroundAutomaticCheckState.Failed;
            _automaticCheck.Status = $"自動確認を開始できませんでした: {transactionError}";
            return ["[XMU AutoCheck] FAILED", _automaticCheck.Status];
        }

        ResetAutomaticCheckReportForNewRun();

        try
        {
            if (!TitleBackgroundQuickCheckUiPresenter.IsSimpleAutoSetupConfigured(_configuration))
            {
                ApplySimpleAutoSetup();
            }

            PrepareAutomaticQuickCheckDiagnostics();
            // baseline setup / reload がすべて終わった後に placement proof を arm する（config は書かない）。
            ArmAutomaticPlacementProof();
            _automaticCheck.Requested = true;
            _automaticCheck.CompletionDueAt = null;
            _automaticCheck.LoginObservedAt = null;
            _automaticCheck.PendingClipboardText = string.Empty;

            if (_clientState.IsLoggedIn)
            {
                _quickCheckState = TitleBackgroundQuickCheckState.Idle;
                _automaticCheck.State = TitleBackgroundAutomaticCheckState.WaitingForCharacterSelect;
                _automaticCheck.Status = "待機中: ログアウトしてキャラ選択画面を開いてください。";
            }
            else
            {
                ArmAutomaticQuickCheck();
            }

            return
            [
                "[XMU AutoCheck] START",
                _automaticCheck.Status,
                "ログイン後に診断ログを自動保存し、クリップボードへコピーします。",
            ];
        }
        catch (Exception ex)
        {
            _automaticCheck.State = TitleBackgroundAutomaticCheckState.Failed;
            _automaticCheck.Status = "自動確認の準備に失敗しました。設定を元に戻しました。";
            _log.Warning(ex, "[XMU BG] Failed to prepare automatic check.");
            DisarmAutomaticPlacementProof();
            PublishAutomaticCheckReport(
                BuildAutomaticCheckFailureFallback("prepare-exception", ex.GetType().Name),
                "automatic-check-prepare-failed");
            RestoreAutomaticCheckSettingsOnce("automatic-check-prepare-failed", reloadNativeIntegration: true);
            return ["[XMU AutoCheck] FAILED", _automaticCheck.Status];
        }
    }

    internal TitleBackgroundAutomaticCheckStatus GetAutomaticQuickCheckStatus()
    {
        EnsureAutomaticCheckReportAvailability();
        var nextAction = _automaticCheck.State switch
        {
            TitleBackgroundAutomaticCheckState.WaitingForCharacterSelect => "ログアウトし、Character Select からログインしてください。",
            TitleBackgroundAutomaticCheckState.Collecting => "そのままログインしてください。操作やコマンド入力は不要です。",
            TitleBackgroundAutomaticCheckState.Completed => "結果はコピー済みです。このまま貼り付けられます。",
            TitleBackgroundAutomaticCheckState.Failed => "もう一度「自動確認を開始」を押してください。",
            _ => "「自動確認を開始」を押した後、Character Select からログインしてください。",
        };
        return new TitleBackgroundAutomaticCheckStatus(
            _automaticCheck.State,
            _automaticCheck.Status,
            nextAction,
            _automaticCheck.ReportAvailable);
    }

    internal bool QueueLastAutomaticCheckReportForClipboard()
    {
        try
        {
            EnsureAutomaticCheckReportAvailability();
            if (string.IsNullOrWhiteSpace(_automaticCheck.LastReport) && _automaticCheck.ReportAvailable)
            {
                var path = Path.Combine(_configDirectory, TitleBackgroundAutomaticCheckReportBuilder.FileName);
                _automaticCheck.LastReport = File.ReadAllText(path);
            }
        }
        catch (Exception ex)
        {
            _automaticCheck.Status = "前回の確認ログを読み込めませんでした。";
            _log.Warning(ex, "[XMU BG] Failed to read previous automatic check report.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_automaticCheck.LastReport))
        {
            return false;
        }

        _automaticCheck.PendingClipboardText = _automaticCheck.LastReport;
        _automaticCheck.Status = "前回の確認ログをクリップボードへコピーします。";
        return true;
    }

    private void EnsureAutomaticCheckReportAvailability()
    {
        if (_automaticCheck.ReportAvailabilityInitialized)
        {
            return;
        }

        _automaticCheck.ReportAvailable = File.Exists(
            Path.Combine(_configDirectory, TitleBackgroundAutomaticCheckReportBuilder.FileName));
        _automaticCheck.ReportAvailabilityInitialized = true;
    }

    internal bool TryConsumeAutomaticCheckClipboardText(out string text)
    {
        text = _automaticCheck.PendingClipboardText;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        _automaticCheck.PendingClipboardText = string.Empty;
        return true;
    }

    private void ArmAutomaticQuickCheck()
    {
        StartQuickCheck();
        _automaticCheck.State = TitleBackgroundAutomaticCheckState.Collecting;
        _automaticCheck.Status = "収集中: Character Select からログインしてください。";
    }

    private void PrepareAutomaticQuickCheckDiagnostics()
    {
        _characterPlacement.FacingCalibrationCapturedDuringRun = false;
        _characterPlacement.FacingCalibrationCandidateId = string.Empty;
        _characterPlacement.FacingCalibrationDerivedOffset = null;
        _characterPlacement.FacingCalibrationFirstOffset = null;
        _characterPlacement.FacingCalibrationMinRelativeOffset = null;
        _characterPlacement.FacingCalibrationMaxRelativeOffset = null;
        _characterPlacement.FacingCalibrationMaxOffsetDelta = null;
        _characterPlacement.FacingCalibrationPreviousNaturalRotation = null;
        _characterPlacement.FacingCalibrationPreviousExpectedFromGeometry = null;
        _characterPlacement.FacingCalibrationNaturalRotation = null;
        _characterPlacement.FacingCalibrationExpectedFromGeometry = null;
        _characterPlacement.FacingCalibrationSampleCount = 0;
        _characterPlacement.FacingCalibrationRejectedTransientCount = 0;

        // 焦点 anchor override が ON なら、その実挙動を確認したいので passive は立てない。
        // passive は最優先 passthrough のため、立てると override が一度も走らず確認にならない。
        // 保存 view は方針転換（2026-07-04）により run 中は常に抑止される（FixOn detour / runtime restore の
        // 両経路が _automaticCheck.Requested で抑止）ため、view ON はもはや passive を立てない理由にならない。
        // run 中はカメラを自然 FixOn に任せて確認場所を写し、view による座標サンプル汚染も防ぐ。
        if (_configuration.TitleBackgroundFixOnPassiveObservationEnabled
            || _configuration.TitleBackgroundFixOnFocusAnchorOverrideEnabled)
        {
            return;
        }

        _configuration.TitleBackgroundFixOnPassiveObservationEnabled = true;
        _configuration.Save();
        RecordTransitionEvent("automatic check passive observation prepared", "existing override setting preserved");
        ReloadNativeIntegration();
    }

    private void UpdateAutomaticQuickCheck()
    {
        if (!_automaticCheck.Requested)
        {
            return;
        }

        if (_automaticCheck.State == TitleBackgroundAutomaticCheckState.WaitingForCharacterSelect
            && !_clientState.IsLoggedIn)
        {
            ArmAutomaticQuickCheck();
            return;
        }

        if (_automaticCheck.State != TitleBackgroundAutomaticCheckState.Collecting)
        {
            return;
        }

        if (!_clientState.IsLoggedIn)
        {
            _automaticCheck.LoginObservedAt = null;
            return;
        }

        var now = DateTimeOffset.Now;
        _automaticCheck.LoginObservedAt ??= now;
        var transitionObserved = _quickCheckState.RunState == TitleBackgroundQuickCheckRunState.LoggedInObserved;
        var forcePartial = !transitionObserved
            && TitleBackgroundAutomaticCheckLogic.ShouldForcePartialCompletion(
                _automaticCheck.State,
                _clientState.IsLoggedIn,
                _automaticCheck.LoginObservedAt,
                now);
        if (!transitionObserved && !forcePartial)
        {
            _automaticCheck.Status = "ログインを検出しました。診断の完了を待っています。";
            return;
        }

        _automaticCheck.CompletionDueAt ??= forcePartial ? now : now.AddSeconds(1);
        if (now < _automaticCheck.CompletionDueAt.Value)
        {
            _automaticCheck.Status = "ログイン完了を確認中です。";
            return;
        }

        CompleteAutomaticQuickCheck(forcePartial);
    }

    private void CompleteAutomaticQuickCheck(bool partial)
    {
        var restoreResult = AutomaticCheckRestoreResult.NotRequired;
        // run 完了時点（設定復元より前）でキャプチャする、自動永続化の判定材料。
        // _configuration は finally の RestoreAutomaticCheckSettingsOnce で run 開始前の値へ
        // 巻き戻されるため、run 中に実際に使われた candidate/probe の値はここで確定させておく。
        TitleBackgroundRunAnchorPersistenceCandidate? persistenceCandidate = null;
        TitleBackgroundRunFacingCalibrationPersistenceCandidate? facingCalibrationCandidate = null;
        TitleBackgroundRunCharaSelectPlacementPersistenceCandidate? placementPersistenceCandidate = null;
        try
        {
            // 0) login freeze 経路が走っていなければ（forcePartial 等）ここで completed-run proof を確定する。
            //    以降の report は live ではなくこの snapshot から出す（REPORT SEMANTICS）。
            CaptureCompletedRunProofSnapshot("complete");
            // 1) QuickCheck 評価
            var result = EvaluateQuickCheck();
            SaveQuickCheckResult(result);
            _quickCheckState = _quickCheckState with { RunState = result.RunState };
            // Character Select placement は proof が成功したときだけ通常設定への昇格候補にする。
            // config へはまだ書かず、設定復元成功後の callback でのみ persist する。
            placementPersistenceCandidate = ResolveRunCharaSelectPlacementPersistenceCandidate(result, partial);
            // 2) Phase 0C: 完了時点の run-scoped 値から有効な probe run だけを 1 サンプル蓄積する
            //    （config 非保存・採用可否は純粋ゲートに委譲）。設定復元(finally)で runId が消える前に実行。
            //    レポート統合より前に追加し、今回 run を集約結果へ反映する。
            TryAddWorldCoordinateSampleFromRun(
                _automaticCheck.RunId,
                result.CompletedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            // 2.5) 通常セッションでの陸上配置解禁: 今回 run で実際に world-experimental(probe) 配置が
            //      適用されていれば、その probe anchor を永続化候補としてキャプチャする（config 書込みは
            //      finally の設定復元より後・reload より前で行う）。
            persistenceCandidate = ResolveRunAnchorPersistenceCandidate();
            facingCalibrationCandidate = ResolveRunFacingCalibrationPersistenceCandidate();
            // 3) QuickCheck・主要診断・座標対応分析を 1 つのレポートへ統合（別ボタン操作を不要にする）。
            // 先頭に selector 非経由の runtime proof 行（placement owner / proof-arm / resolve 状態）を差し込む。
            var quickCheckLines = new List<string>(BuildAutomaticCheckRuntimeProofLines());
            quickCheckLines.AddRange(TitleBackgroundQuickCheckEvaluator.BuildChatLines(result));
            var diagnosticLines = new List<string>(
                TitleBackgroundAutomaticCheckDiagnosticSelector.Select(
                    GetDiagnosticLines(automaticInvocation: true)));
            diagnosticLines.Add("--- World/Lobby Coordinate Correspondence ---");
            diagnosticLines.AddRange(
                TitleBackgroundWorldCoordinateCorrespondenceLogic.BuildReport(_worldProbeState.Samples));
            var report = TitleBackgroundAutomaticCheckReportBuilder.Build(
                result.CompletedAt,
                quickCheckLines,
                diagnosticLines,
                partial,
                _automaticCheck.RunId);
            PublishAutomaticCheckReport(report, "complete");
            _automaticCheck.State = TitleBackgroundAutomaticCheckState.Completed;
            _automaticCheck.Status = partial
                ? $"部分完了: {result.Level}。遷移検出が完了しなかったため、取得済みログを自動コピーしました。"
                : $"完了: {result.Level}。確認ログを自動コピーしました。";
        }
        catch (Exception ex)
        {
            // completion/report failure is not a successful proof; never promote a candidate
            // that was resolved before the exception.
            placementPersistenceCandidate = null;
            _automaticCheck.PlacementPromotionEligible = false;
            _automaticCheck.PlacementPromotionPersisted = false;
            _automaticCheck.PlacementPromotionStatus = "rejected";
            _automaticCheck.PlacementPromotionReason = "completion-failed";
            _automaticCheck.State = TitleBackgroundAutomaticCheckState.Failed;
            _automaticCheck.Status = "自動確認ログの作成に失敗しました。";
            _log.Warning(ex, "[XMU BG] Automatic QuickCheck failed.");
            PublishAutomaticCheckReport(
                BuildAutomaticCheckFailureFallback("completion-exception", ex.GetType().Name),
                "automatic-check-completion-failed");
        }
        finally
        {
            _automaticCheck.Requested = false;
            _automaticCheck.CompletionDueAt = null;
            _automaticCheck.LoginObservedAt = null;
            // report は既に completed-run proof snapshot から生成済み。ここで proof arm を解除し、
            // live runtime を reset する（reset 後の live state で completed report を作らない）。
            DisarmAutomaticPlacementProof();
            _charaSelectPlacement.Reset();
            // 復元(ApplyTo+Save)の後・reload の前に自動永続化を書き込む。復元より前に書くと
            // run 開始前スナップショットへの復元で上書き・消失し、reload より後だと新 config が
            // native 側へ反映されない。afterRestoreBeforeReload はこの一箇所専用の差し込み点。
            var persistedThisRun = false;
            var persistedFacingCalibrationThisRun = false;
            var persistedPlacementThisRun = false;
            restoreResult = RestoreAutomaticCheckSettingsOnce(
                "automatic-check-complete",
                reloadNativeIntegration: true,
                afterRestoreBeforeReload: () =>
                {
                    persistedPlacementThisRun = TryPersistRunCharaSelectPlacementFromCandidate(placementPersistenceCandidate);
                    persistedThisRun = TryPersistRunAnchorFromCandidate(persistenceCandidate);
                    persistedFacingCalibrationThisRun =
                        TryPersistRunFacingCalibrationFromCandidate(facingCalibrationCandidate);
                });
            FinalizeAutomaticCheckReport(
                restoreResult,
                persistedThisRun,
                persistenceCandidate,
                persistedFacingCalibrationThisRun,
                facingCalibrationCandidate,
                persistedPlacementThisRun,
                placementPersistenceCandidate);
            _charaSelectSourceLayout.Reset();
            _charaSelectStaticAnchor.Reset();
            _charaSelectSceneObjectSuppression.Reset();
        }
    }

    // run 完了時点（設定復元より前）で、今回 run が world-experimental(probe) 配置を実際に適用したかを
    // 判定し、適用済みなら永続化候補をキャプチャする。config は一切書かない（純粋な値の確定のみ）。
    // ここで使う candidate/territory は _worldProbeState（config 非依存のセッション限定状態）由来なので、
    // finally での _configuration 復元より前に呼んでも run 中止 candidate と一致する。
    private TitleBackgroundRunAnchorPersistenceCandidate? ResolveRunAnchorPersistenceCandidate()
    {
        var runActive = IsRunScopedQuickCheckActive();
        var runAppliedFrameCount = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedPlacementCount(
            runActive,
            _characterPlacement.CharaSelectCharacterPlacementCount,
            _quickCheckState.CharacterPlacementCountStart);
        var runPlacementApplied = runAppliedFrameCount > 0;

        var activeCandidate = ResolveCurrentOverrideCandidate();
        var worldResolution = ResolveExperimentalWorldPlacement(activeCandidate);

        var shouldPersist = TitleBackgroundAutomaticCheckLogic.ShouldPersistRunAnchor(
            runPlacementApplied,
            _characterPlacement.LastCharaSelectCharacterPlacementSource,
            worldResolution.Eligible,
            worldResolution.Source,
            WorldExperimentalSourceProbe);
        if (!shouldPersist)
        {
            return null;
        }

        // gate（Evaluate）は既に候補一致・territory 一致・有限値・frame=world を通っているが、
        // 保存内容の取り違えを避けるため probe anchor 自体の基本条件も二重に確認する（fail-closed）。
        if (!_worldProbeState.Enabled
            || !_worldProbeState.HasValue
            || string.IsNullOrEmpty(_worldProbeState.CandidateId)
            || _worldProbeState.TerritoryTypeId == 0
            || !TitleBackgroundCameraMath.IsFiniteVector(_worldProbeState.Position))
        {
            return null;
        }

        return new TitleBackgroundRunAnchorPersistenceCandidate(
            _worldProbeState.CandidateId,
            _worldProbeState.Position,
            _worldProbeState.TerritoryTypeId);
    }

    // 設定復元(ApplyTo+Save)の直後・reload の前に呼ばれる。キャプチャ済みの永続化候補があれば
    // 通常セッションの陸上配置を有効化する Configuration フィールドへ書き込み Save する。
    // 候補が null（保存条件を満たさなかった run）なら何もしない。
    private bool TryPersistRunAnchorFromCandidate(TitleBackgroundRunAnchorPersistenceCandidate? candidate)
    {
        if (!candidate.HasValue)
        {
            return false;
        }

        var value = candidate.Value;
        _configuration.TitleBackgroundCharaSelectAnchorEnabled = true;
        _configuration.TitleBackgroundCharaSelectAnchorCandidateId = value.CandidateId;
        _configuration.TitleBackgroundCharaSelectAnchorX = value.Position.X;
        _configuration.TitleBackgroundCharaSelectAnchorY = value.Position.Y;
        _configuration.TitleBackgroundCharaSelectAnchorZ = value.Position.Z;
        _configuration.TitleBackgroundCharaSelectAnchorFrame = TitleBackgroundCharaSelectAnchorFrame.World;
        _configuration.TitleBackgroundCharaSelectAnchorTerritoryTypeId = value.TerritoryTypeId;
        _configuration.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = true;
        _configuration.Save();
        RecordTransitionEvent("run anchor persisted from successful run", "automatic-check-complete");
        return true;
    }

    // run 完了時点の placement proof から、通常セッションへ昇格できる値だけを候補化する。
    // 判定中は config を変更しない。partial / failed / candidate mismatch / 未確認 write は null。
    private TitleBackgroundRunCharaSelectPlacementPersistenceCandidate? ResolveRunCharaSelectPlacementPersistenceCandidate(
        TitleBackgroundQuickCheckResult result,
        bool partial)
    {
        if (!_automaticCheck.CompletedRunProof.HasValue)
        {
            _automaticCheck.PlacementPromotionStatus = "rejected";
            _automaticCheck.PlacementPromotionReason = "proof-snapshot-missing";
            return null;
        }

        var proof = _automaticCheck.CompletedRunProof.Value;
        var candidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(proof.CandidateId);
        var activeCandidate = ResolveCurrentOverrideCandidate();
        var candidateMatches = proof.CandidateMatches
            && string.Equals(candidateId, activeCandidate.Id, StringComparison.Ordinal);
        var targetFinite = !string.IsNullOrWhiteSpace(proof.TargetScene)
            && TitleBackgroundCameraMath.IsFiniteVector(proof.TargetPosition)
            && float.IsFinite(proof.TargetRotation);
        var decision = TitleBackgroundCharaSelectPlacementLogic.EvaluatePromotion(
            proof,
            partial,
            result.Level,
            candidateMatches,
            targetFinite,
            result.PostLoginLeakStatus);
        if (!decision.Eligible)
        {
            _automaticCheck.PlacementPromotionEligible = false;
            _automaticCheck.PlacementPromotionPersisted = false;
            _automaticCheck.PlacementPromotionStatus = "rejected";
            _automaticCheck.PlacementPromotionReason = decision.Reason;
            return null;
        }

        if (!TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(candidateId, out var candidate)
            || !candidate.BackgroundUsable
            || candidate.TerritoryId == 0)
        {
            _automaticCheck.PlacementPromotionEligible = false;
            _automaticCheck.PlacementPromotionPersisted = false;
            _automaticCheck.PlacementPromotionStatus = "rejected";
            _automaticCheck.PlacementPromotionReason = "candidate-not-persistable";
            return null;
        }

        var layoutTerritoryTypeId = candidate.TerritoryId;
        var layoutLayerFilterKey = candidate.LayerFilterKey;
        if (candidate.RequiresSourceBackedLayout
            && !_charaSelectSourceLayout.TryGetPersistenceMetadata(
                candidate.Id,
                out layoutTerritoryTypeId,
                out layoutLayerFilterKey))
        {
            _automaticCheck.PlacementPromotionEligible = false;
            _automaticCheck.PlacementPromotionPersisted = false;
            _automaticCheck.PlacementPromotionStatus = "rejected";
            _automaticCheck.PlacementPromotionReason = "source-layout-not-captured";
            return null;
        }

        if (!TitleBackgroundCharaSelectSourceLayoutLogic.IsConfiguredForCandidate(
                candidate,
                layoutTerritoryTypeId,
                layoutLayerFilterKey))
        {
            _automaticCheck.PlacementPromotionEligible = false;
            _automaticCheck.PlacementPromotionPersisted = false;
            _automaticCheck.PlacementPromotionStatus = "rejected";
            _automaticCheck.PlacementPromotionReason = "candidate-layout-not-persistable";
            return null;
        }

        var targetScene = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(proof.TargetScene);
        if (!string.Equals(targetScene, candidate.TerritoryPath, StringComparison.OrdinalIgnoreCase))
        {
            _automaticCheck.PlacementPromotionEligible = false;
            _automaticCheck.PlacementPromotionPersisted = false;
            _automaticCheck.PlacementPromotionStatus = "rejected";
            _automaticCheck.PlacementPromotionReason = "target-scene-candidate-mismatch";
            return null;
        }

        _automaticCheck.PlacementPromotionEligible = true;
        _automaticCheck.PlacementPromotionPersisted = false;
        _automaticCheck.PlacementPromotionStatus = "eligible";
        _automaticCheck.PlacementPromotionReason = decision.Reason;
        return new TitleBackgroundRunCharaSelectPlacementPersistenceCandidate(
            candidate.Id,
            candidate.TerritoryPath,
            layoutTerritoryTypeId,
            layoutLayerFilterKey,
            proof.TargetPosition,
            proof.TargetRotation);
    }

    // 設定復元に成功した直後、native reload の前にだけ呼ぶ。成功 proof 以外では何もしない。
    // candidate の scene metadata も再検証してから route + source-backed Position/Rotation を保存する。
    private bool TryPersistRunCharaSelectPlacementFromCandidate(
        TitleBackgroundRunCharaSelectPlacementPersistenceCandidate? candidate)
    {
        if (!candidate.HasValue)
        {
            return false;
        }

        try
        {
            var value = candidate.Value;
            if (!TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(value.CandidateId, out var definition)
                || !definition.BackgroundUsable
                || !string.Equals(
                    TitleBackgroundPathHelper.NormalizeTerritoryPathInput(value.TerritoryPath),
                    definition.TerritoryPath,
                    StringComparison.OrdinalIgnoreCase)
                || !TitleBackgroundCharaSelectSourceLayoutLogic.IsConfiguredForCandidate(
                    definition,
                    value.LayoutTerritoryTypeId,
                    value.LayoutLayerFilterKey)
                || !TitleBackgroundCameraMath.IsFiniteVector(value.Position)
                || !float.IsFinite(value.Rotation))
            {
                _automaticCheck.PlacementPromotionStatus = "persist-failed";
                _automaticCheck.PlacementPromotionReason = "candidate-revalidation-failed";
                return false;
            }

            // successful proof の candidate-bound route を通常利用へ昇格する。V2 はこの owner と排他。
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(_configuration, definition);
            _configuration.TitleBackgroundLayoutTerritoryTypeId = value.LayoutTerritoryTypeId;
            _configuration.TitleBackgroundLayoutLayerFilterKey = value.LayoutLayerFilterKey;
            _configuration.TitleBackgroundOverrideEnabled = true;
            _configuration.TitleBackgroundCameraOverrideEnabled = true;
            _configuration.TitleBackgroundIntegratedCompositionEnabled = true;
            _configuration.TitleBackgroundV2Enabled = false;
            _configuration.TitleBackgroundCharaSelectPlacementEnabled = true;
            _configuration.TitleBackgroundCharaSelectPlacementCandidateId = definition.Id;
            _configuration.TitleBackgroundCharaSelectPlacementPositionCaptured = true;
            _configuration.TitleBackgroundCharaSelectPlacementPositionX = value.Position.X;
            _configuration.TitleBackgroundCharaSelectPlacementPositionY = value.Position.Y;
            _configuration.TitleBackgroundCharaSelectPlacementPositionZ = value.Position.Z;
            _configuration.TitleBackgroundCharaSelectPlacementRotation = value.Rotation;
            _configuration.TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.CharaSelectOnly;
            _configuration.TitleBackgroundCharacterSelectBackgroundMode =
                TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly;
            _configuration.TitleBackgroundCharaSelectCameraFramingMode =
                TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended;
            _configuration.Save();

            _automaticCheck.PlacementPromotionPersisted = true;
            _automaticCheck.PlacementPromotionStatus = "persisted";
            _automaticCheck.PlacementPromotionReason = "persisted";
            RecordTransitionEvent("run placement persisted from successful proof", definition.Id);
            return true;
        }
        catch (Exception ex)
        {
            _automaticCheck.PlacementPromotionPersisted = false;
            _automaticCheck.PlacementPromotionStatus = "persist-failed";
            _automaticCheck.PlacementPromotionReason = $"persist-failed:{ex.GetType().Name}";
            _log.Warning(ex, "[XMU BG] Failed to persist Character Select placement proof.");
            return false;
        }
    }

    private TitleBackgroundRunFacingCalibrationPersistenceCandidate? ResolveRunFacingCalibrationPersistenceCandidate()
    {
        if (!_characterPlacement.FacingCalibrationCapturedDuringRun
            || string.IsNullOrWhiteSpace(_characterPlacement.FacingCalibrationCandidateId)
            || !_characterPlacement.FacingCalibrationDerivedOffset.HasValue
            || !float.IsFinite(_characterPlacement.FacingCalibrationDerivedOffset.Value)
            || !TitleBackgroundCharaSelectCharacterFacing.HasStableCalibrationWindow(
                _characterPlacement.FacingCalibrationSampleCount,
                _characterPlacement.FacingCalibrationMaxOffsetDelta))
        {
            return null;
        }

        return new TitleBackgroundRunFacingCalibrationPersistenceCandidate(
            _characterPlacement.FacingCalibrationCandidateId,
            _characterPlacement.FacingCalibrationDerivedOffset.Value);
    }

    private bool TryPersistRunFacingCalibrationFromCandidate(
        TitleBackgroundRunFacingCalibrationPersistenceCandidate? candidate)
    {
        if (!candidate.HasValue)
        {
            return false;
        }

        var value = candidate.Value;
        _configuration.TitleBackgroundCharaSelectFacingCalibrationCaptured = true;
        _configuration.TitleBackgroundCharaSelectFacingCalibrationCandidateId = value.CandidateId;
        _configuration.TitleBackgroundCharaSelectFacingCalibrationOffset =
            TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(value.Offset);
        _configuration.Save();
        RecordTransitionEvent("facing calibration persisted from automatic run", value.CandidateId);
        return true;
    }

    // OneClick 実機確認 run の placement proof を arm する。config は一切書かない（runtime bool のみ）。
    // baseline setup / hook / diagnostics 準備がすべて終わった後に呼ぶこと。V2 の保存設定は True のままでよい
    // （実 owner は CharaSelectEngineOwner=PlacementProof になり V2 writer は動かない）。
    private void ArmAutomaticPlacementProof()
    {
        _automaticCheck.CompletedRunProof = null;
        _automaticCheck.ResetPlacementPromotion();
        _automaticCheck.PlacementProofArmed = true;
        _charaSelectPlacement.Reset();
        _charaSelectSelectionChangePending = false;
        _charaSelectPlacementResolveRetries = 0;
        RecordTransitionEvent("automatic placement proof armed", "runtime-scoped");
    }

    // proof arm を解除する。config は触らない（arm 中も触っていないため差分ゼロ）。
    private void DisarmAutomaticPlacementProof()
    {
        if (_automaticCheck.PlacementProofArmed)
        {
            RecordTransitionEvent("automatic placement proof disarmed", "run-end");
        }

        _automaticCheck.PlacementProofArmed = false;
        _charaSelectSelectionChangePending = false;
        _charaSelectPlacementResolveRetries = 0;
    }

    // login freeze / 完了時に completed-run proof snapshot を 1 回だけ確定する。
    // 以後 report はこの snapshot から出す（live runtime が reset/restore で消えても正しい）。
    private void CaptureCompletedRunProofSnapshot(string reason)
    {
        if (!_automaticCheck.PlacementProofArmed || _automaticCheck.CompletedRunProof != null)
        {
            return;
        }

        var activeCandidate = ResolveCurrentOverrideCandidate();
        var placementCandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(
            _configuration.TitleBackgroundCharaSelectPlacementCandidateId);
        // proof snapshot は run-scoped capture だけを表す。既存の persistent 値を混ぜると、
        // capture 未成立の失敗 run が「captured」と誤診断され、promotion の根拠にもなり得る。
        // FRU など approved-static-anchor 候補は、実際に write した値と一致させるため
        // TargetPosition=認可済み anchor / TargetRotation=canonical stable capture に統一する
        // （書く前の lobby 位置を promotion / report の evidence にしない）。
        var approvedAnchor = default(System.Numerics.Vector3);
        var anchorAuthorized = activeCandidate.ApprovedStaticAnchor.HasValue
            && _charaSelectStaticAnchor.TryGetAuthorizedAnchor(activeCandidate.Id, out approvedAnchor);
        var sourcePosition = default(System.Numerics.Vector3);
        var sourcePositionCaptured = !activeCandidate.ApprovedStaticAnchor.HasValue
            && activeCandidate.RequiresSourceBackedLayout
            && _charaSelectSourceLayout.TryGetPosition(activeCandidate.Id, out sourcePosition);
        var positionCaptured = activeCandidate.ApprovedStaticAnchor.HasValue
            ? anchorAuthorized && _charaSelectPlacement.CaptureCompleted
            : _charaSelectPlacement.CaptureCompleted;
        var targetPosition = activeCandidate.ApprovedStaticAnchor.HasValue
            ? (anchorAuthorized ? approvedAnchor : default)
            : sourcePositionCaptured
            ? sourcePosition
            : _charaSelectPlacement.CaptureCompleted
            ? _charaSelectPlacement.CapturedPosition
            : default;
        var targetRotation = _charaSelectPlacement.CaptureCompleted
            ? _charaSelectPlacement.CapturedRotation
            : 0f;
        var targetScene = string.IsNullOrWhiteSpace(_validatedTerritoryPath)
            ? TitleBackgroundPathHelper.NormalizeTerritoryPathInput(_configuration.TitleBackgroundTerritoryPath)
            : _validatedTerritoryPath;
        var proof = _charaSelectPlacement.CaptureProofSnapshot(
            TitleBackgroundCharaSelectEngineOwnerLogic.Describe(CharaSelectEngineOwner),
            _automaticCheck.PlacementProofArmed,
            positionCaptured,
            ComputeCharaSelectPlacementLegacyOwnershipInactive(),
            placementCandidateId,
            !string.IsNullOrEmpty(placementCandidateId)
                && string.Equals(placementCandidateId, activeCandidate.Id, StringComparison.Ordinal),
            targetScene,
            targetPosition,
            targetRotation);
        if (sourcePositionCaptured)
        {
            // Source-backed world target is the proof target; stable actor capture remains the
            // canonical rotation/readiness proof and is not replaced by world rotation.
            proof = proof with { TargetPosition = sourcePosition };
        }

        _automaticCheck.CompletedRunProof = proof;
        RecordTransitionEvent("automatic placement proof snapshot captured", reason);
    }

    internal void CancelAutomaticQuickCheck()
    {
        _automaticCheck.Requested = false;
        _automaticCheck.CompletionDueAt = null;
        _automaticCheck.LoginObservedAt = null;
        _automaticCheck.State = TitleBackgroundAutomaticCheckState.Idle;
        _automaticCheck.Status = "自動確認を中止し、設定を元に戻しました。";
        DisarmAutomaticPlacementProof();
        _automaticCheck.CompletedRunProof = null;
        _automaticCheck.ResetPlacementPromotion();
        _charaSelectPlacement.Reset();
        _charaSelectSourceLayout.Reset();
        _charaSelectStaticAnchor.Reset();
        _charaSelectSceneObjectSuppression.Reset();
        RestoreAutomaticCheckSettingsOnce("automatic-check-cancelled", reloadNativeIntegration: true);
    }

    internal bool ResetSimpleTitleBackgroundSettings()
    {
        _automaticCheck.Requested = false;
        _automaticCheck.CompletionDueAt = null;
        _automaticCheck.LoginObservedAt = null;
        DisarmAutomaticPlacementProof();
        _automaticCheck.CompletedRunProof = null;
        _automaticCheck.ResetPlacementPromotion();
        var restoreResult = RestoreAutomaticCheckSettingsOnce(
            "simple-settings-reset",
            reloadNativeIntegration: false);
        if (!restoreResult.SettingsRestored)
        {
            return false;
        }

        _configuration.TitleBackgroundOverrideEnabled = false;
        _configuration.TitleBackgroundCameraOverrideEnabled = false;
        _configuration.TitleBackgroundIntegratedCompositionEnabled = false;
        _configuration.TitleBackgroundV2Enabled = false;
        _configuration.TitleBackgroundCharaSelectPlacementEnabled = false;
        _configuration.TitleBackgroundCharaSelectPlacementCandidateId = string.Empty;
        _configuration.TitleBackgroundCharaSelectPlacementPositionCaptured = false;
        _configuration.TitleBackgroundCharaSelectPlacementPositionX = 0f;
        _configuration.TitleBackgroundCharaSelectPlacementPositionY = 0f;
        _configuration.TitleBackgroundCharaSelectPlacementPositionZ = 0f;
        _configuration.TitleBackgroundCharaSelectPlacementRotation = 0f;
        _charaSelectPlacement.Reset();
        _charaSelectSourceLayout.Reset();
        _charaSelectStaticAnchor.Reset();
        _charaSelectSceneObjectSuppression.Reset();
        _configuration.TitleBackgroundSelectedPresetId = string.Empty;
        _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
        _configuration.TitleBackgroundTerritoryPath = string.Empty;
        _configuration.TitleBackgroundTerritoryTypeId = 0;
        _configuration.TitleBackgroundLayoutTerritoryTypeId = 0;
        _configuration.TitleBackgroundLayoutLayerFilterKey = 0;
        _configuration.TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.ResolveOnly;
        _configuration.TitleBackgroundCharacterSelectBackgroundMode =
            TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly;
        _configuration.TitleBackgroundCharacterSelectLightingMode =
            TitleBackgroundCharacterSelectLightingMode.Default;
        _configuration.TitleBackgroundCharaSelectCameraFramingMode =
            TitleBackgroundCharaSelectCameraFramingMode.Default;
        _configuration.TitleBackgroundFixOnPassiveObservationEnabled = false;
        _configuration.TitleBackgroundFixOnFocusAnchorOverrideEnabled = false;
        _configuration.TitleBackgroundEnvironmentNoonEnabled = true;
        _configuration.TitleBackgroundEnvironmentClearSkyEnabled = true;
        _configuration.TitleBackgroundCharaSelectAnchorEnabled = false;
        _configuration.TitleBackgroundCharaSelectAnchorCandidateId = string.Empty;
        _configuration.TitleBackgroundCharaSelectAnchorX = 0f;
        _configuration.TitleBackgroundCharaSelectAnchorY = 0f;
        _configuration.TitleBackgroundCharaSelectAnchorZ = 0f;
        _configuration.TitleBackgroundCharaSelectAnchorRotation = 0f;
        _configuration.TitleBackgroundCharaSelectAnchorFrame = string.Empty;
        _configuration.TitleBackgroundCharaSelectAnchorTerritoryTypeId = 0;
        _configuration.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = false;
        // セッション限定 probe（config 外）も解除しないと、リセット後も実験配置が継続し得る。
        ClearWorldProbeAnchor();
        // Phase 0C: セッション内の対応サンプルも解除する。
        ClearWorldCoordinateSamples();
        _configuration.TitleBackgroundCharaSelectViewEnabled = false;
        _configuration.TitleBackgroundCharaSelectViewCandidateId = string.Empty;
        _configuration.TitleBackgroundCharaSelectViewCameraX = 0f;
        _configuration.TitleBackgroundCharaSelectViewCameraY = 0f;
        _configuration.TitleBackgroundCharaSelectViewCameraZ = 0f;
        _configuration.TitleBackgroundCharaSelectViewFocusX = 0f;
        _configuration.TitleBackgroundCharaSelectViewFocusY = 0f;
        _configuration.TitleBackgroundCharaSelectViewFocusZ = 0f;
        _configuration.TitleBackgroundCharaSelectViewFovY = TitleBackgroundPreset.DefaultFovY;
        _configuration.TitleBackgroundCharaSelectViewPoseCaptured = false;
        _configuration.TitleBackgroundCharaSelectViewDirH = 0f;
        _configuration.TitleBackgroundCharaSelectViewDirV = 0f;
        _configuration.TitleBackgroundCharaSelectViewDistance = 0f;
        _configuration.TitleBackgroundCharaSelectFacingCalibrationCaptured = false;
        _configuration.TitleBackgroundCharaSelectFacingCalibrationCandidateId = string.Empty;
        _configuration.TitleBackgroundCharaSelectFacingCalibrationOffset =
            TitleBackgroundCharaSelectCharacterFacing.DefaultCalibrationOffset;
        _configuration.Save();

        _automaticCheck.State = TitleBackgroundAutomaticCheckState.Idle;
        try
        {
            _charaSelectService?.ReapplyCompositionRuntimeStateFromConfiguration();
            ReloadNativeIntegration();
            _automaticCheck.Status = "背景と位置の設定を初期状態に戻しました。";
            return true;
        }
        catch (Exception ex)
        {
            _automaticCheck.Status = "設定は初期化しましたが、実行状態の再読み込みに失敗しました。";
            _log.Warning(ex, "[XMU BG] Failed to reload runtime after simple settings reset.");
            return false;
        }
    }

    private string AutomaticCheckRecoveryPath =>
        Path.Combine(_configDirectory, TitleBackgroundAutomaticCheckRecoveryJournal.FileName);

    private bool TryBeginAutomaticCheckSettingsTransaction(out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            Directory.CreateDirectory(_configDirectory);
            var runId = Guid.NewGuid().ToString("N");
            var journal = TitleBackgroundAutomaticCheckRecoveryJournal.Create(
                runId,
                DateTimeOffset.Now,
                _configuration);
            var path = AutomaticCheckRecoveryPath;
            var tempPath = path + ".tmp";
            File.WriteAllText(
                tempPath,
                TitleBackgroundAutomaticCheckRecoveryJournal.Serialize(journal),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, path, overwrite: true);

            _automaticCheck.SettingsSnapshot = journal.OriginalSettings;
            _automaticCheck.RunId = runId;
            _automaticCheck.SettingsRestored = false;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.GetType().Name;
            _log.Warning(ex, "[XMU BG] Failed to create automatic-check recovery journal.");
            return false;
        }
    }

    private void TryRestoreInterruptedAutomaticCheck()
    {
        var path = AutomaticCheckRecoveryPath;
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var journal = TitleBackgroundAutomaticCheckRecoveryJournal.Deserialize(File.ReadAllText(path));
            if (journal == null)
            {
                _automaticCheck.Status = "中断された自動確認の復元情報を読み取れませんでした。";
                return;
            }

            journal.OriginalSettings.ApplyTo(_configuration);
            _configuration.Save();
            _charaSelectService?.ReapplyCompositionRuntimeStateFromConfiguration();
            File.Delete(path);
            _automaticCheck.Status = "中断された自動確認の設定を元に戻しました。";
            _automaticCheck.SettingsRestored = true;
        }
        catch (Exception ex)
        {
            _automaticCheck.Status = "中断された自動確認の設定復元に失敗しました。";
            _log.Warning(ex, "[XMU BG] Failed to restore interrupted automatic check.");
        }
    }

    private AutomaticCheckRestoreResult RestoreAutomaticCheckSettingsOnce(
        string reason,
        bool reloadNativeIntegration,
        Action? afterRestoreBeforeReload = null)
    {
        if (_automaticCheck.SettingsRestored || _automaticCheck.SettingsSnapshot == null)
        {
            return AutomaticCheckRestoreResult.NotRequired;
        }

        try
        {
            _automaticCheck.SettingsSnapshot.ApplyTo(_configuration);
            _configuration.Save();
            _charaSelectService?.ReapplyCompositionRuntimeStateFromConfiguration();
            _automaticCheck.SettingsRestored = true;
            if (File.Exists(AutomaticCheckRecoveryPath))
            {
                File.Delete(AutomaticCheckRecoveryPath);
            }

            RecordTransitionEvent("automatic check settings restored", reason);
        }
        catch (Exception ex)
        {
            _automaticCheck.Status = "自動確認の設定復元に失敗しました。次回起動時に再試行します。";
            _log.Warning(ex, "[XMU BG] Failed to restore automatic check settings. reason={Reason}", reason);
            return new AutomaticCheckRestoreResult(false, false);
        }
        finally
        {
            if (_automaticCheck.SettingsRestored)
            {
                _automaticCheck.SettingsSnapshot = null;
                _automaticCheck.RunId = string.Empty;
            }
        }

        // 復元(ApplyTo+Save)が成功した直後・reload(hook 再初期化)より前に呼ぶ差し込み点。
        // 現状は run 完了時の自動永続化（世界座標アンカー / Character Select placement / facing calibration）専用。
        // 呼び出し元を指定しない限り
        // 既定 null で従来どおり何もしない（他の呼び出し箇所は無変更）。
        if (afterRestoreBeforeReload != null)
        {
            try
            {
                afterRestoreBeforeReload();
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[XMU BG] afterRestoreBeforeReload callback failed. reason={Reason}", reason);
            }
        }

        if (reloadNativeIntegration && !_hookLifecycle.Disposed)
        {
            try
            {
                ReloadNativeIntegration();
            }
            catch (Exception ex)
            {
                _automaticCheck.Status = "設定は復元しましたが、実行状態の再読み込みに失敗しました。";
                _log.Warning(
                    ex,
                    "[XMU BG] Settings were restored, but native integration reload failed. reason={Reason}",
                    reason);
                return new AutomaticCheckRestoreResult(true, false);
            }
        }

        return new AutomaticCheckRestoreResult(true, true);
    }

    private void FinalizeAutomaticCheckReport(
        AutomaticCheckRestoreResult restoreResult,
        bool persistedAnchorFromRun = false,
        TitleBackgroundRunAnchorPersistenceCandidate? persistedCandidate = null,
        bool persistedFacingCalibrationFromRun = false,
        TitleBackgroundRunFacingCalibrationPersistenceCandidate? facingCalibrationCandidate = null,
        bool persistedPlacementFromRun = false,
        TitleBackgroundRunCharaSelectPlacementPersistenceCandidate? placementPersistenceCandidate = null)
    {
        if (string.IsNullOrWhiteSpace(_automaticCheck.LastReport))
        {
            return;
        }

        var report = $"{_automaticCheck.LastReport.TrimEnd()}{Environment.NewLine}"
            + $"[XIV Mini Util] settingsRestored={restoreResult.SettingsRestored}{Environment.NewLine}"
            + $"[XIV Mini Util] runtimeReloaded={restoreResult.RuntimeReloaded}{Environment.NewLine}";
        if (_automaticCheck.PlacementPromotionEligible && !restoreResult.SettingsRestored)
        {
            _automaticCheck.PlacementPromotionStatus = "restore-failed";
            _automaticCheck.PlacementPromotionReason = "settings-restore-failed";
        }
        else if (_automaticCheck.PlacementPromotionPersisted && !restoreResult.RuntimeReloaded)
        {
            _automaticCheck.PlacementPromotionStatus = "persisted-reload-failed";
            _automaticCheck.PlacementPromotionReason = "native-reload-failed";
        }

        var placementProofVerdict = _automaticCheck.PlacementPromotionEligible
            && persistedPlacementFromRun
            && restoreResult.SettingsRestored
            && restoreResult.RuntimeReloaded
            ? "PASS"
            : "FAIL";
        report += $"[XIV Mini Util] automaticRun.placementProofVerdict={placementProofVerdict}{Environment.NewLine}"
            + $"[XIV Mini Util] automaticRun.placementPromotionEligible={_automaticCheck.PlacementPromotionEligible}{Environment.NewLine}"
            + $"[XIV Mini Util] automaticRun.placementPromotionPersisted={persistedPlacementFromRun}{Environment.NewLine}"
            + $"[XIV Mini Util] automaticRun.placementPromotionStatus={_automaticCheck.PlacementPromotionStatus}{Environment.NewLine}"
            + $"[XIV Mini Util] automaticRun.placementPromotionReason={FormatNone(_automaticCheck.PlacementPromotionReason)}{Environment.NewLine}"
            + $"[XIV Mini Util] automaticRun.ownerAfterRestore={TitleBackgroundCharaSelectEngineOwnerLogic.Describe(CharaSelectEngineOwner)}{Environment.NewLine}";
        if (persistedPlacementFromRun && placementPersistenceCandidate.HasValue)
        {
            var placement = placementPersistenceCandidate.Value;
            report += $"[XIV Mini Util] automaticRun.placementPromotionCandidate={placement.CandidateId}{Environment.NewLine}"
                + $"[XIV Mini Util] automaticRun.placementPromotionTarget={FormatVector(placement.Position)} / rotation={placement.Rotation:0.###}{Environment.NewLine}";
        }

        // 既存診断キーは変更せず、run 成功時の自動永続化結果だけを新規キーとして追記する。
        // 保存しなかった場合はキー自体を出さない（既存レポートを汚さない）。
        if (persistedAnchorFromRun && persistedCandidate.HasValue)
        {
            var candidate = persistedCandidate.Value;
            report += $"[XIV Mini Util] characterPlace.persistedAnchorFromRun={persistedAnchorFromRun}{Environment.NewLine}"
                + $"[XIV Mini Util] characterPlace.persistedAnchorPosition={FormatVector(candidate.Position)}{Environment.NewLine}"
                + $"[XIV Mini Util] characterPlace.persistedAnchorCandidateId={candidate.CandidateId}{Environment.NewLine}"
                + $"[XIV Mini Util] characterPlace.persistedAnchorTerritoryTypeId={candidate.TerritoryTypeId}{Environment.NewLine}";
        }

        if (persistedFacingCalibrationFromRun && facingCalibrationCandidate.HasValue)
        {
            var calibration = facingCalibrationCandidate.Value;
            report += $"[XIV Mini Util] character.facing.calibration.persisted=True{Environment.NewLine}"
                + $"[XIV Mini Util] character.facing.calibration.persistedCandidateId={calibration.CandidateId}{Environment.NewLine}"
                + $"[XIV Mini Util] character.facing.calibration.persistedOffset={calibration.Offset:0.###}{Environment.NewLine}";
        }

        PublishAutomaticCheckReport(report, "finalize");
    }

    // Clipboard handoff is the primary contract. Persisting the same report is best-effort and
    // must never prevent the in-memory report from being copied.
    private void PublishAutomaticCheckReport(string report, string context)
    {
        _automaticCheck.LastReport = report;
        _automaticCheck.PendingClipboardText = report;
        _automaticCheck.ReportAvailable = true;
        _automaticCheck.ReportAvailabilityInitialized = true;

        try
        {
            Directory.CreateDirectory(_configDirectory);
            File.WriteAllText(
                Path.Combine(_configDirectory, TitleBackgroundAutomaticCheckReportBuilder.FileName),
                report);
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "[XMU BG] Failed to persist automatic check report. context={Context}",
                context);
        }
    }

    private void ResetAutomaticCheckReportForNewRun()
    {
        _automaticCheck.LastReport = string.Empty;
        _automaticCheck.PendingClipboardText = string.Empty;
        _automaticCheck.ReportAvailable = false;
        _automaticCheck.ReportAvailabilityInitialized = true;
        _automaticCheck.CompletedRunProof = null;
        _automaticCheck.ResetPlacementPromotion();
        _charaSelectSourceLayout.Reset();
        _charaSelectStaticAnchor.Reset();
        _charaSelectSceneObjectSuppression.Reset();
        // pre-login environment snapshot（weather / time）を次 run へ持ち越さない。
        _environmentNoon.ResetRunScopedSnapshot();
        _environmentClearSky.ResetRunScopedSnapshot();
    }

    private string BuildAutomaticCheckFailureFallback(string reason, string detail)
    {
        var safeReason = reason.Replace('\r', ' ').Replace('\n', ' ');
        var safeDetail = detail.Replace('\r', ' ').Replace('\n', ' ');
        return string.Join(
            Environment.NewLine,
            "[XIV Mini Util] Title Background automatic check",
            $"[XIV Mini Util] completedAt={DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            $"[XIV Mini Util] runId={_automaticCheck.RunId}",
            "[XIV Mini Util] completion=failed",
            "[XIV Mini Util] result=FAILED",
            $"[XIV Mini Util] {BuildCharaSelectPlacementRuntimeProofLine()}",
            $"[XIV Mini Util] reason={safeReason}",
            $"[XIV Mini Util] detail={safeDetail}");
    }

    private readonly record struct AutomaticCheckRestoreResult(
        bool SettingsRestored,
        bool RuntimeReloaded)
    {
        public static AutomaticCheckRestoreResult NotRequired { get; } = new(true, true);
    }

    public IReadOnlyList<string> StartQuickCheck()
    {
        // A completed OneClick proof belongs only to the completed automatic run. A later
        // manual QuickCheck must evaluate the persistent owner from its own run, not reuse
        // the frozen proof snapshot as a stale input.
        _automaticCheck.CompletedRunProof = null;
        _automaticCheck.ResetPlacementPromotion();
        var currentMap = TryReadCurrentLobbyMap(out var map) ? map.ToString() : "unknown";
        var candidate = ResolveCurrentOverrideCandidate();
        // Reset integrated composition route tracking for this run before recording the baseline.
        // Route invocation below may trigger a scene reload; the override counter change will be
        // captured relative to the baseline saved in _quickCheckState.
        _integratedCompositionRouteInvoked = false;
        _integratedCompositionRouteLastReason = string.Empty;
        _charaSelectService?.ResetTitleBackgroundCharacterCompositionBridgeSnapshot();
        if (_configuration.TitleBackgroundIntegratedCompositionEnabled)
        {
            // Invoke before saving the baseline so CreateScene fires before the user logs in.
            TryInvokeIntegratedCompositionRoute();
            _charaSelectService?.ApplyTitleBackgroundCharacterCompositionBridgeRuntimeState();
        }

        _quickCheckState = new TitleBackgroundQuickCheckState(
            TitleBackgroundQuickCheckRunState.Armed,
            DateTimeOffset.Now,
            IsNewCharaSelectEngineActive
                ? _charaSelectPlacement.SceneGeneration
                : _charaSelectCameraAdapter.RuntimeState.SceneGeneration,
            _cameraRestoreCurve.SceneReadySignalAcceptedCount,
            _quickCheckOverrideAppliedCount,
            GetPhase2GApplyCount(),
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
            candidate.Id,
            _configuration.TitleBackgroundCharacterSelectBackgroundMode,
            _configuration.TitleBackgroundCharacterSelectLightingMode,
            currentMap,
            _clientState.IsLoggedIn,
            _characterPlacement.CharaSelectCharacterPlacementCount,
            _transitionDiagnostics.EventSequenceWatermark,
            CharaSelectPlacementApplyCountStart: _charaSelectPlacement.PlacementApplyCount);
        _configuration.TitleBackgroundLastQuickCheckResult = TitleBackgroundQuickCheckLevel.NotRun;
        _configuration.TitleBackgroundLastQuickCheckCandidateId = candidate.Id;
        var startReason = _clientState.IsLoggedIn
            ? "Start QuickCheck from title/character select for a clean run. Current run started while already logged in."
            : "Armed: enter Character Select, log in, then run check";
        _configuration.TitleBackgroundLastQuickCheckReason = startReason;
        _configuration.TitleBackgroundLastQuickCheckNextAction = _clientState.IsLoggedIn
            ? "return to title/character select, start QuickCheck, then log in and run check"
            : "enter Character Select, log in, then run check";
        _configuration.TitleBackgroundLastQuickCheckTime = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        _configuration.TitleBackgroundLastQuickCheckDetailFileName = TitleBackgroundQuickCheckEvaluator.DetailFileName;
        _configuration.Save();

        return
        [
            "[XMU QuickCheck] START",
            $"Candidate: {candidate.Id} / {candidate.DisplayName}",
            $"Next: {_configuration.TitleBackgroundLastQuickCheckNextAction}",
        ];
    }

    public IReadOnlyList<string> GetQuickCheckStatusLines()
    {
        var last = TitleBackgroundQuickCheckUiPresenter.BuildSummary(_configuration);
        return
        [
            $"[XMU QuickCheck] state={_quickCheckState.RunState}",
            $"Started: {(_quickCheckState.StartedAt.HasValue ? _quickCheckState.StartedAt.Value.ToString("yyyy-MM-dd HH:mm:ss zzz") : "none")}",
            last.LastResultLine,
            last.LastReasonLine,
            last.NextActionLine,
            last.DetailLine,
        ];
    }

    public IReadOnlyList<string> ResetQuickCheck()
    {
        _quickCheckState = TitleBackgroundQuickCheckState.Idle;
        _automaticCheck.Requested = false;
        _automaticCheck.CompletionDueAt = null;
        _automaticCheck.LoginObservedAt = null;
        _automaticCheck.State = TitleBackgroundAutomaticCheckState.Idle;
        _automaticCheck.Status = "自動確認は未開始です。";
        DisarmAutomaticPlacementProof();
        _automaticCheck.CompletedRunProof = null;
        _automaticCheck.ResetPlacementPromotion();
        _charaSelectPlacement.Reset();
        _charaSelectSourceLayout.Reset();
        _charaSelectStaticAnchor.Reset();
        _charaSelectSceneObjectSuppression.Reset();
        RestoreAutomaticCheckSettingsOnce("quick-check-reset", reloadNativeIntegration: true);
        _configuration.TitleBackgroundLastQuickCheckResult = TitleBackgroundQuickCheckLevel.NotRun;
        _configuration.TitleBackgroundLastQuickCheckCandidateId = string.Empty;
        _configuration.TitleBackgroundLastQuickCheckReason = string.Empty;
        _configuration.TitleBackgroundLastQuickCheckNextAction = string.Empty;
        _configuration.TitleBackgroundLastQuickCheckTime = string.Empty;
        _configuration.TitleBackgroundLastQuickCheckDetailFileName = string.Empty;
        _configuration.Save();

        return
        [
            "[XMU QuickCheck] RESET",
            "Next: start another one-click verification from the Login Background settings.",
        ];
    }

    public IReadOnlyList<string> RunQuickCheck()
    {
        // A completed OneClick proof is authoritative only for its own automatic report.
        // Preserve it while the automatic run is collecting, but never let a later manual
        // QuickCheck evaluate the persistent route from a stale frozen snapshot.
        if (_automaticCheck.State != TitleBackgroundAutomaticCheckState.Collecting)
        {
            _automaticCheck.CompletedRunProof = null;
            _automaticCheck.ResetPlacementPromotion();
        }

        var result = EvaluateQuickCheck();
        SaveQuickCheckResult(result);
        _quickCheckState = _quickCheckState with { RunState = result.RunState };
        return TitleBackgroundQuickCheckEvaluator.BuildChatLines(result);
    }

    private TitleBackgroundQuickCheckResult EvaluateQuickCheck()
    {
        var input = BuildQuickCheckInput();
        return TitleBackgroundQuickCheckEvaluator.Evaluate(input);
    }

    // QuickCheck の run-scoped 計測が有効か（Start 済みかつ Idle でない）。
    private bool IsRunScopedQuickCheckActive()
    {
        return _quickCheckState.StartedAt.HasValue
            && _quickCheckState.RunState != TitleBackgroundQuickCheckRunState.Idle;
    }

    // Delivery / Transition の判定に使う sceneReady accepted 回数。
    // 自動確認時は current run の差分（run-scoped）、通常診断時は累積値を返す。
    private int GetVerdictSceneReadyAcceptedCount(bool automaticInvocation)
    {
        return TitleBackgroundAutomaticCheckLogic.ResolveVerdictSceneReadyAcceptedCount(
            automaticInvocation,
            IsRunScopedQuickCheckActive(),
            _cameraRestoreCurve.SceneReadySignalAcceptedCount,
            _quickCheckState.SceneReadyAcceptedCountStart);
    }

    private TitleBackgroundQuickCheckInput BuildQuickCheckInput()
    {
        var candidate = ResolveCurrentOverrideCandidate();
        var runScoped = _quickCheckState.StartedAt.HasValue
            && _quickCheckState.RunState != TitleBackgroundQuickCheckRunState.Idle;
        var sceneReadyAcceptedCount = runScoped
            ? Math.Max(0, _cameraRestoreCurve.SceneReadySignalAcceptedCount - _quickCheckState.SceneReadyAcceptedCountStart)
            : _cameraRestoreCurve.SceneReadySignalAcceptedCount;
        var overrideAppliedCount = runScoped
            ? Math.Max(0, _quickCheckOverrideAppliedCount - _quickCheckState.OverrideAppliedCountStart)
            : _quickCheckOverrideAppliedCount;
        var phase2GApplyCount = runScoped
            ? Math.Max(0, GetPhase2GApplyCount() - _quickCheckState.Phase2GApplyCountStart)
            : GetPhase2GApplyCount();
        var currentLobbyMapAvailable = TryReadCurrentLobbyMap(out var currentLobbyMap);
        var currentLobbyMapName = currentLobbyMapAvailable ? currentLobbyMap.ToString() : "unknown";
        var currentLobbyMapRemainedAfterLogin = _clientState.IsLoggedIn
            && currentLobbyMapAvailable
            && currentLobbyMap != GameLobbyType.None;
        var phase2MSummary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(_phaseRecording.Phase2MPlacementFrames.Values);
        var characterKnownLimitation = !candidate.CharacterExpectedVisible
            || string.Equals(phase2MSummary.ActorVisible, "not-observed", StringComparison.OrdinalIgnoreCase);
        var actorSourceAmbiguous = string.Equals(GetLatestCharacterPlacementActorCandidateStatus(), "ambiguous", StringComparison.OrdinalIgnoreCase)
            || string.Equals(phase2MSummary.Resolution, "ambiguous", StringComparison.OrdinalIgnoreCase);
        var zeroTransformStubs = phase2MSummary.ZeroPositionCandidateCount > 0
            && phase2MSummary.NonZeroPositionCandidateCount == 0;
        var backgroundApplied = runScoped
            ? overrideAppliedCount > 0
            : overrideAppliedCount > 0 || (_lastOverrideApplied && _lastOverrideLobbyType == GameLobbyType.CharaSelect);
        var adapterState = _charaSelectCameraAdapter.State.ToString();
        // post-login 異常は run-scoped 時に「現時点の状態」だけで判定し、過去 run の sticky 履歴を持ち込まない。
        // 通常診断（run-scoped でない）時は従来どおり累積履歴も含める。
        var staleCharaSelectStateAfterLogin = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedStateAnomaly(
            runScoped,
            _transitionDiagnostics.StaleAdapterStateAfterLogin,
            _clientState.IsLoggedIn && TitleBackgroundQuickCheckEvaluator.IsUnsafeAfterLoginAdapterState(adapterState));
        var activeAfterLoginDetected = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedStateAnomaly(
            runScoped,
            _transitionDiagnostics.StaleSceneOverrideStateAfterLogin,
            _clientState.IsLoggedIn && _activeSceneOverride);
        var phase2GAppliedAfterLogin = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedEventAnomaly(
            runScoped,
            _transitionDiagnostics.Phase2GAppliedAfterLogin,
            _transitionDiagnostics.LastPhase2GAppliedAfterLoginEventSeq,
            _quickCheckState.TransitionEventSeqStart);
        var pluginOrHookError = _hookLifecycle.State is TitleBackgroundServiceState.InvalidConfiguration
            or TitleBackgroundServiceState.AddressResolveFailed
            or TitleBackgroundServiceState.HookCreateFailed
            or TitleBackgroundServiceState.HookEnableFailed
            or TitleBackgroundServiceState.RuntimeError;
        var candidateFieldsValid = TitleBackgroundCharaSelectSourceLayoutLogic.IsCandidateFieldsValid(
            candidate,
            _configuration.TitleBackgroundTerritoryPath,
            _configuration.TitleBackgroundTerritoryTypeId,
            _configuration.TitleBackgroundLayoutTerritoryTypeId,
            _configuration.TitleBackgroundLayoutLayerFilterKey);

        var serviceReady = _hookLifecycle.State == TitleBackgroundServiceState.Ready;
        // Derive shouldArmAdapter from reason so both fields are always consistent.
        // ShouldArmAdapter(3 params) is kept for actual adapter arming in ConfigureCharaSelectCameraAdapter;
        // the QuickCheck diagnostic must reflect ALL conditions including integrated composition.
        var shouldArmAdapterReason = TitleBackgroundCharaSelectCameraLogic.BuildShouldArmAdapterReason(
            _configuration.TitleBackgroundOverrideEnabled,
            _configuration.TitleBackgroundCameraOverrideEnabled,
            _configuration.TitleBackgroundRuntimeMode,
            _configuration.TitleBackgroundIntegratedCompositionEnabled,
            candidateFieldsValid,
            serviceReady,
            serviceReady);
        var shouldArmAdapter = shouldArmAdapterReason == "none";
        var sceneOverrideApplyObserved = overrideAppliedCount > 0;
        var cameraFramingApplied = phase2GApplyCount > 0;
        var bridgeSnapshotBase = _charaSelectService?.GetTitleBackgroundCharacterCompositionBridgeSnapshot()
            ?? TitleBackgroundCharacterCompositionBridgeSnapshot.Empty;
        var bridgeSnapshot = bridgeSnapshotBase with
        {
            AppliedCamera = cameraFramingApplied,
            CharacterVisualKnownByBridge = bridgeSnapshotBase.CharacterVisualKnownByBridge && cameraFramingApplied,
        };
        var cameraProfile = ResolveCurrentTitleBackgroundCameraProfile(candidate.Id);
        var hasLatestTimelineSample = TryGetLatestPhase2CTimelineSnapshot(out var latestTimelineSample);
        var finalYawPitchDistanceMatchesProfile = cameraProfile.HasProfile && hasLatestTimelineSample
            ? BuildPhase2GFinalCameraStateMatchesPresetVerdict(latestTimelineSample)
            : "unknown";
        var currentDirH = hasLatestTimelineSample ? latestTimelineSample.LobbyDirH ?? latestTimelineSample.DirH : null;
        var currentDirV = hasLatestTimelineSample ? latestTimelineSample.LobbyDirV ?? latestTimelineSample.DirV : null;
        var currentDistance = hasLatestTimelineSample ? latestTimelineSample.LobbyDistance ?? latestTimelineSample.Distance : null;
        var currentPosition = hasLatestTimelineSample ? latestTimelineSample.SceneCameraPosition : _cameraObservation.LastPostFixOnSceneCameraPosition;
        var currentLookAt = hasLatestTimelineSample ? latestTimelineSample.SceneCameraLookAtVector ?? latestTimelineSample.LobbyLastLookAtVector : _cameraObservation.LastPostFixOnLookAtVector;
        var runtimeHasProfilePose = _charaSelectCameraAdapter.RuntimeState.HasCameraPose;
        var visibleProfileAppliedState = BuildVisibleProfileAppliedState(cameraProfile, runtimeHasProfilePose, cameraFramingApplied);
        // 配置結果は run-scoped で判定する。前回 run の成功回数・source・frame を今回へ流用しない。
        var runScopedPlacementCount = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedPlacementCount(
            runScoped,
            _characterPlacement.CharaSelectCharacterPlacementCount,
            _quickCheckState.CharacterPlacementCountStart);
        var runScopedCharaSelectPlacementApplyCount = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedPlacementCount(
            runScoped,
            _charaSelectPlacement.PlacementApplyCount,
            _quickCheckState.CharaSelectPlacementApplyCountStart);
        var sceneGenerationEnd = IsNewCharaSelectEngineActive
            ? _charaSelectPlacement.SceneGeneration
            : _charaSelectCameraAdapter.RuntimeState.SceneGeneration;
        var charaSelectObserved = IsQuickCheckCharaSelectObserved();
        if (_automaticCheck.CompletedRunProof is { } completedProof)
        {
            charaSelectObserved |= completedProof.CharaSelectSessionObserved;
        }
        var persistentCharaSelectPlacementActive =
            CharaSelectEngineOwner == TitleBackgroundCharaSelectEngineOwner.Placement;
        var persistentCharaSelectPlacementApplied = persistentCharaSelectPlacementActive
            && runScopedCharaSelectPlacementApplyCount > 0;
        var persistentCharaSelectPlacementReadbackConfirmed = persistentCharaSelectPlacementActive
            && _charaSelectPlacement.LastWriteReadbackConfirmed;
        var characterCompositedApplied = runScopedPlacementCount > 0
            || runScopedCharaSelectPlacementApplyCount > 0;
        // camera-focus は画面内に置いただけ（地面位置は未確認）。
        var characterPlacedViaCameraFocusFallback = TitleBackgroundAutomaticCheckLogic.ResolveCameraFocusFallbackPlacement(
            characterCompositedApplied,
            _characterPlacement.LastCharaSelectCharacterPlacementSource);
        // 地面検証済みは anchor 由来かつ frame が明確な地面 provenance（LobbyNative）を持つ場合のみ。
        // CharaSelectFallback（水上座標の再保存の可能性）/ Unknown / World は ground verified にしない。
        var characterGroundPlacementVerified = TitleBackgroundAutomaticCheckLogic.ResolveGroundPlacementVerified(
            characterCompositedApplied,
            _characterPlacement.LastCharaSelectCharacterPlacementSource,
            _characterPlacement.LastCharaSelectCharacterPlacementAnchorFrame);
        var passiveCameraObservationActive = _configuration.TitleBackgroundFixOnPassiveObservationEnabled;
        var verification = BuildTitleBackgroundVerificationSummary();
        var cameraFramesCharacter = verification.Framing.Status switch
        {
            TitleBackgroundVerificationStatus.PASS => "True",
            TitleBackgroundVerificationStatus.FAIL => "False",
            _ => characterGroundPlacementVerified
                ? "True"
                : CharacterPlacementStatusToQuickCheckTriState(phase2MSummary.CameraFramesActor),
        };

        return new TitleBackgroundQuickCheckInput(
            runScoped,
            _quickCheckState.RunState,
            _quickCheckState.StartedAt,
            DateTimeOffset.Now,
            runScoped && _quickCheckState.StartedLoggedIn,
            charaSelectObserved,
            runScoped ? _quickCheckState.SceneGenerationStart : 0,
            sceneGenerationEnd,
            sceneReadyAcceptedCount,
            overrideAppliedCount,
            phase2GApplyCount,
            pluginOrHookError,
            _hookLifecycle.StateReason,
            _clientState.IsLoggedIn,
            currentLobbyMapName,
            currentLobbyMapRemainedAfterLogin,
            _configuration.TitleBackgroundCharacterSelectBackgroundMode,
            _configuration.TitleBackgroundCharacterSelectLightingMode,
            candidate.Id,
            candidate.DisplayName,
            candidate.VerifiedInGame,
            candidate.Source,
            candidate.ExpectedCompatibility,
            candidate.ExpectedBrightness,
            candidate.TerritoryPath,
            candidate.TerritoryId,
            _configuration.TitleBackgroundLayoutLayerFilterKey,
            candidateFieldsValid,
            backgroundApplied,
            backgroundApplied,
            !candidate.VerifiedInGame
                && verification.Framing.Status != TitleBackgroundVerificationStatus.PASS,
            candidate.CharacterExpectedVisible,
            phase2MSummary.ActorVisible,
            characterKnownLimitation,
            _clientState.IsLoggedIn && _activeSceneOverride,
            activeAfterLoginDetected,
            staleCharaSelectStateAfterLogin,
            phase2GAppliedAfterLogin,
            true,
            actorSourceAmbiguous,
            zeroTransformStubs,
            _configuration.TitleBackgroundCharacterVisualStatus,
            _configuration.TitleBackgroundCharaSelectCameraFramingMode,
            candidate.RecommendedCameraFraming,
            candidate.RecommendedAction,
            _configuration.TitleBackgroundOverrideEnabled,
            _configuration.TitleBackgroundCameraOverrideEnabled,
            _configuration.CharaSelectSceneCompositionEnabled,
            _configuration.TitleBackgroundIntegratedCompositionEnabled,
            shouldArmAdapter,
            shouldArmAdapterReason,
            _integratedCompositionRouteInvoked,
            _integratedCompositionRouteLastReason,
            cameraFramingApplied,
            sceneOverrideApplyObserved,
            _integratedCompositionAutoEnabled,
            bridgeSnapshot,
            cameraProfile.ProfileId,
            cameraProfile.ProfileSource,
            FormatFloat(cameraProfile.Yaw ?? _charaSelectCameraAdapter.GetRestoredYaw() ?? _charaSelectCameraAdapter.RuntimeState.Yaw),
            FormatFloat(cameraProfile.Pitch ?? _charaSelectCameraAdapter.RuntimeState.Pitch),
            FormatFloat(cameraProfile.Distance ?? _charaSelectCameraAdapter.RuntimeState.Distance),
            FormatVector(cameraProfile.LookAtOffset),
            FormatVector(cameraProfile.PositionOffset),
            cameraFramesCharacter,
            VerdictToQuickCheckTriState(finalYawPitchDistanceMatchesProfile),
            cameraProfile.HasProfile,
            visibleProfileAppliedState == "True",
            visibleProfileAppliedState,
            BuildCameraProfileApplyRoute(cameraProfile, runtimeHasProfilePose, cameraFramingApplied),
            _configuration.TitleBackgroundCapturedCameraProfileEnabled,
            FormatFloat(_configuration.TitleBackgroundCapturedDirH),
            FormatFloat(_configuration.TitleBackgroundCapturedDirV),
            FormatFloat(_configuration.TitleBackgroundCapturedDistance),
            FormatVector(BuildCapturedProfilePosition()),
            FormatVector(BuildCapturedProfileLookAt()),
            FormatFloat(currentDirH),
            FormatFloat(currentDirV),
            FormatFloat(currentDistance),
            FormatVector(currentPosition),
            FormatVector(currentLookAt),
            bridgeSnapshot.AppliedStage && bridgeSnapshot.AppliedCharacter,
            cameraProfile.HasProfile && runtimeHasProfilePose,
            characterCompositedApplied,
            passiveCameraObservationActive,
            characterPlacedViaCameraFocusFallback,
            characterGroundPlacementVerified,
            verification.PoseHold.ReportValue,
            verification.CameraJitter.ReportValue,
            verification.RotationJitter.ReportValue,
            verification.Facing.ReportValue,
            verification.Framing.ReportValue,
            verification.Suppression.ReportValue,
            verification.LoginStop.ReportValue,
            verification.Environment.ReportValue,
            _automaticCheck.CompletedRunProof,
            persistentCharaSelectPlacementActive,
            persistentCharaSelectPlacementApplied,
            persistentCharaSelectPlacementReadbackConfirmed);
    }
}
