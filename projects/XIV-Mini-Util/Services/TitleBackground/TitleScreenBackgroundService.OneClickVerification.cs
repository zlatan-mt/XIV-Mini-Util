// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.OneClickVerification.cs
// Description: Title Background 実機確認の「1クリック」入口。preflight・自動復旧・状態遷移・失敗レポート自動コピーを集約する。
// Reason: ユーザー操作を「1クリック→ログアウト→ログイン→貼る」だけに限定する恒久契約（AGENTS.md）を、
//         UI から低レベル手順を呼ばせず単一サービスメソッドで満たすため。
namespace XivMiniUtil.Services.TitleBackground;

// 通常画面に表示してよい最小状態（内部 enum / Phase / candidate / gate は出さない）。
internal readonly record struct TitleBackgroundOneClickStatus(
    string StateLine,
    string NextActionLine,
    string ResultLine,
    bool Busy);

public sealed unsafe partial class TitleScreenBackgroundService
{
    // 1クリック実機確認の単一入口。UI はこのメソッドだけを呼ぶ。
    // 順に: 前回 run の安全復元 → 推奨設定適用(candidate 確定) → 現在地を非永続 probe 取得・検証・有効化
    // → hook 再初期化(未準備なら自動復旧1回) → run-scoped QuickCheck 開始 → 「ログアウトしてください」。
    public IReadOnlyList<string> StartOneClickTitleBackgroundVerification()
    {
        var previousRestore = RestoreAutomaticCheckSettingsOnce(
            "restart-before-new-run",
            reloadNativeIntegration: true);
        if (!previousRestore.SettingsRestored
            || !previousRestore.RuntimeReloaded
            || File.Exists(AutomaticCheckRecoveryPath))
        {
            return FailOneClickWithoutTransaction(
                "previous-restore-failed",
                "前回の状態を復元できませんでした。",
                "失敗：レポートをコピーしました");
        }

        if (!TryBeginAutomaticCheckSettingsTransaction(out var transactionError))
        {
            return FailOneClickWithoutTransaction(
                $"transaction-failed:{transactionError}",
                "確認を開始できませんでした。",
                "失敗：レポートをコピーしました");
        }

        ResetAutomaticCheckReportForNewRun();

        try
        {
            // candidate 設定は必ず probe 取得より先に行う（candidate-mismatch を構造的に防ぐ）。
            ApplySimpleAutoSetup();
            var candidate = ResolveCurrentOverrideCandidate();

            // 現在地を非永続 probe へ。CaptureWorldProbeAnchorInMemory が territory/finite を検証し、
            // 成功時に probe を有効化する（config は書かない）。
            if (!CaptureWorldProbeAnchorInMemory(out var probeStatus))
            {
                return FailOneClickWithReport(
                    "probe-capture-failed",
                    probeStatus,
                    "失敗：レポートをコピーしました");
            }

            var worldResolution = ResolveExperimentalWorldPlacement(candidate);
            if (!worldResolution.Eligible)
            {
                return FailOneClickWithReport(
                    "probe-not-applicable",
                    TitleBackgroundExperimentalWorldPlacementLogic.DescribeReason(worldResolution.Gate),
                    "失敗：レポートをコピーしました");
            }

            // hook readiness を再初期化。未準備なら安全な再初期化を1回試して再評価。
            ReloadNativeIntegrationForOneClick();
            if (_state != TitleBackgroundServiceState.Ready)
            {
                ReloadNativeIntegrationForOneClick();
            }

            if (_state != TitleBackgroundServiceState.Ready)
            {
                return FailOneClickWithReport(
                    "hook-not-ready",
                    _stateReason,
                    "失敗：レポートをコピーしました");
            }

            // run-scoped QuickCheck を自動確認機構で開始（完了時に統合レポートを自動コピー）。
            PrepareAutomaticQuickCheckDiagnostics();
            _automaticCheck.Requested = true;
            _automaticCheck.CompletionDueAt = null;
            _automaticCheck.LoginObservedAt = null;
            _automaticCheck.PendingClipboardText = string.Empty;

            if (_clientState.IsLoggedIn)
            {
                _quickCheckState = TitleBackgroundQuickCheckState.Idle;
                _automaticCheck.State = TitleBackgroundAutomaticCheckState.WaitingForCharacterSelect;
                _automaticCheck.Status = "ログアウトしてください";
            }
            else
            {
                ArmAutomaticQuickCheck();
                _automaticCheck.Status = "キャラ選択画面を確認中";
            }

            return ["[XMU OneClick] START", _automaticCheck.Status];
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] One-click verification failed to prepare.");
            var failed = FailOneClickWithReport(
                "prepare-exception",
                ex.GetType().Name,
                "失敗：レポートをコピーしました");
            return failed;
        }
    }

    // 通常画面に出す限定された状態文言へ写像する（内部名は出さない）。
    internal TitleBackgroundOneClickStatus GetOneClickStatus()
    {
        EnsureAutomaticCheckReportAvailability();
        switch (_automaticCheck.State)
        {
            case TitleBackgroundAutomaticCheckState.WaitingForCharacterSelect:
                return new TitleBackgroundOneClickStatus("ログアウトしてください", string.Empty, string.Empty, true);
            case TitleBackgroundAutomaticCheckState.Collecting:
                return _clientState.IsLoggedIn
                    ? new TitleBackgroundOneClickStatus("完了処理中", string.Empty, string.Empty, true)
                    : new TitleBackgroundOneClickStatus("キャラ選択画面を確認中", "ログインしてください", string.Empty, true);
            case TitleBackgroundAutomaticCheckState.Completed:
                return new TitleBackgroundOneClickStatus("準備完了", string.Empty, "完了：レポートをコピーしました", false);
            case TitleBackgroundAutomaticCheckState.Failed:
                return new TitleBackgroundOneClickStatus("準備完了", string.Empty, "失敗：レポートをコピーしました", false);
            default:
                return new TitleBackgroundOneClickStatus("準備完了", string.Empty, string.Empty, false);
        }
    }

    internal bool IsOneClickBusy =>
        _automaticCheck.State is TitleBackgroundAutomaticCheckState.WaitingForCharacterSelect
            or TitleBackgroundAutomaticCheckState.Collecting;

    // transaction 開始前の失敗（復元失敗/transaction失敗）。snapshot が無くても安全に失敗完了する。
    private IReadOnlyList<string> FailOneClickWithoutTransaction(string reason, string userMessage, string resultLine)
    {
        _automaticCheck.Requested = false;
        _automaticCheck.State = TitleBackgroundAutomaticCheckState.Failed;
        _automaticCheck.Status = resultLine;
        EmitOneClickFailureReport(reason, userMessage);
        return ["[XMU OneClick] FAILED", userMessage];
    }

    // transaction 開始後の失敗。失敗レポートを自動コピーし、設定を元に戻す（追加操作は要求しない）。
    private IReadOnlyList<string> FailOneClickWithReport(string reason, string detail, string resultLine)
    {
        _automaticCheck.Requested = false;
        _automaticCheck.State = TitleBackgroundAutomaticCheckState.Failed;
        _automaticCheck.Status = resultLine;
        EmitOneClickFailureReport(reason, detail);
        var restoreResult =
            RestoreAutomaticCheckSettingsOnce("one-click-failed", reloadNativeIntegration: true);
        FinalizeAutomaticCheckReport(restoreResult);
        return ["[XMU OneClick] FAILED", resultLine];
    }

    // 失敗レポートを生成し、自動確認レポートファイルへ保存＆clipboard キューへ積む（成功時と同じ自動コピー経路）。
    // 原因・hook 状態・candidate・territory・再初期化結果を含める。
    private void EmitOneClickFailureReport(string reason, string detail)
    {
        try
        {
            var candidate = ResolveCurrentOverrideCandidate();
            var header = new List<string>
            {
                "result=FAILED",
                $"reason={reason}",
                $"detail={(string.IsNullOrWhiteSpace(detail) ? "none" : detail)}",
                $"serviceState={_state}",
                $"serviceStateReason={FormatNone(_stateReason)}",
                $"hookReady={_state == TitleBackgroundServiceState.Ready}",
                $"candidate={candidate.Id}",
                $"activeCandidateTerritory={candidate.TerritoryId}",
                $"savedTerritory={_worldProbeState.TerritoryTypeId}",
                $"currentTerritory={(_clientState.IsLoggedIn ? _clientState.TerritoryType.ToString() : "not-logged-in")}",
                $"reinitResult={(_state == TitleBackgroundServiceState.Ready ? "recovered" : "still-not-ready")}",
            };

            var diagnosticLines = TitleBackgroundAutomaticCheckDiagnosticSelector.Select(
                GetDiagnosticLines(automaticInvocation: true));
            var report = TitleBackgroundAutomaticCheckReportBuilder.Build(
                DateTimeOffset.Now,
                header,
                diagnosticLines,
                partial: true,
                _automaticCheck.RunId);

            PublishAutomaticCheckReport(report, "one-click-failure");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] Failed to emit one-click failure report.");
            PublishAutomaticCheckReport(
                BuildAutomaticCheckFailureFallback(reason, $"report-exception:{ex.GetType().Name}"),
                "one-click-failure-fallback");
        }
    }
}
