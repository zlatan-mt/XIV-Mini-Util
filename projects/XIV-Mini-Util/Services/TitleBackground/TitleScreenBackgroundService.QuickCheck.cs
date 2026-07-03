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
        // 既に override 系（焦点 anchor / view）が ON なら、その実挙動を確認したいので passive は立てない。
        // passive は最優先 passthrough のため、立てると override が一度も走らず確認にならない。
        if (_configuration.TitleBackgroundFixOnPassiveObservationEnabled
            || _configuration.TitleBackgroundFixOnFocusAnchorOverrideEnabled
            || _configuration.TitleBackgroundCharaSelectViewEnabled)
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
        try
        {
            // 1) QuickCheck 評価
            var result = EvaluateQuickCheck();
            SaveQuickCheckResult(result);
            _quickCheckState = _quickCheckState with { RunState = result.RunState };
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
            // 3) QuickCheck・主要診断・座標対応分析を 1 つのレポートへ統合（別ボタン操作を不要にする）。
            var quickCheckLines = TitleBackgroundQuickCheckEvaluator.BuildChatLines(result);
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
            // 復元(ApplyTo+Save)の後・reload の前に自動永続化を書き込む。復元より前に書くと
            // run 開始前スナップショットへの復元で上書き・消失し、reload より後だと新 config が
            // native 側へ反映されない。afterRestoreBeforeReload はこの一箇所専用の差し込み点。
            var persistedThisRun = false;
            restoreResult = RestoreAutomaticCheckSettingsOnce(
                "automatic-check-complete",
                reloadNativeIntegration: true,
                afterRestoreBeforeReload: () =>
                {
                    persistedThisRun = TryPersistRunAnchorFromCandidate(persistenceCandidate);
                });
            FinalizeAutomaticCheckReport(restoreResult, persistedThisRun, persistenceCandidate);
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

    internal void CancelAutomaticQuickCheck()
    {
        _automaticCheck.Requested = false;
        _automaticCheck.CompletionDueAt = null;
        _automaticCheck.LoginObservedAt = null;
        _automaticCheck.State = TitleBackgroundAutomaticCheckState.Idle;
        _automaticCheck.Status = "自動確認を中止し、設定を元に戻しました。";
        RestoreAutomaticCheckSettingsOnce("automatic-check-cancelled", reloadNativeIntegration: true);
    }

    internal bool ResetSimpleTitleBackgroundSettings()
    {
        _automaticCheck.Requested = false;
        _automaticCheck.CompletionDueAt = null;
        _automaticCheck.LoginObservedAt = null;
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
        // 現状は run 完了時の自動永続化（世界座標アンカーの保存）専用。呼び出し元を指定しない限り
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
        TitleBackgroundRunAnchorPersistenceCandidate? persistedCandidate = null)
    {
        if (string.IsNullOrWhiteSpace(_automaticCheck.LastReport))
        {
            return;
        }

        var report = $"{_automaticCheck.LastReport.TrimEnd()}{Environment.NewLine}"
            + $"[XIV Mini Util] settingsRestored={restoreResult.SettingsRestored}{Environment.NewLine}"
            + $"[XIV Mini Util] runtimeReloaded={restoreResult.RuntimeReloaded}{Environment.NewLine}";
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
            _charaSelectCameraAdapter.RuntimeState.SceneGeneration,
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
            _transitionDiagnostics.EventSequenceWatermark);
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
            "Next: run /xmutbgcheck start before the next Character Select login check.",
        ];
    }

    public IReadOnlyList<string> RunQuickCheck()
    {
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
        var candidateFieldsValid = !string.IsNullOrWhiteSpace(candidate.TerritoryPath)
            && candidate.TerritoryPath != "none"
            && TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath(candidate.TerritoryPath)
            && candidate.TerritoryId != 0
            && candidate.LayerFilterKey != 0;

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
        var characterCompositedApplied = runScopedPlacementCount > 0;
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

        return new TitleBackgroundQuickCheckInput(
            runScoped,
            _quickCheckState.RunState,
            _quickCheckState.StartedAt,
            DateTimeOffset.Now,
            runScoped && _quickCheckState.StartedLoggedIn,
            IsQuickCheckCharaSelectObserved(),
            runScoped ? _quickCheckState.SceneGenerationStart : 0,
            _charaSelectCameraAdapter.RuntimeState.SceneGeneration,
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
            candidate.LayerFilterKey,
            candidateFieldsValid,
            backgroundApplied,
            backgroundApplied,
            !candidate.VerifiedInGame,
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
            characterGroundPlacementVerified
                ? "True"
                : CharacterPlacementStatusToQuickCheckTriState(phase2MSummary.CameraFramesActor),
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
            characterGroundPlacementVerified);
    }
}
