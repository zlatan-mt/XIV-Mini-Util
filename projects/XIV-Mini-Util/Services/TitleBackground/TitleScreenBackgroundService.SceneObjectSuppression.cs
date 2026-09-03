// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.SceneObjectSuppression.cs
// Description: FRU クリア後ステージ candidate 固有の、戦闘用 gimmick / telegraph SharedGroup の抑止。
//              pre-login の CharaSelect で、static-anchor と同じ scene identity 認可 + loaded ActiveLayout の
//              厳密確認を通ったときだけ、FRU deny/keep token に一致する SharedGroup instance を
//              ILayoutInstance.SetActive(false) する。scene-generation 単位の有限 window で bounded。
// Reason: n4gw 直接ロードで FRU 戦闘中の gimmick / 魔法陣 / 氷 / telegraph SharedGroup が多数表示される
//         （実機確認済み）。placement / camera / actor resolver / static anchor engine は一切変更しない。
//         VFX 型 instance は現行 API で active 切替の semantic が未確認のため対象外。
//         native pointer は各 bounded pass で再取得し保持しない。login / session 終了 / window close /
//         scene generation 変化で書込を停止する（post-login leak なし。login 時にゲームが layout 再構築）。
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    private readonly TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState _charaSelectSceneObjectSuppression = new();

    // CharaSelectService.SelectedCharacterChanged（detour スレッド）で立て、Framework スレッドの
    // suppression pass で 1 回だけ消費する。read→clear を分離すると event を取りこぼしうるため、
    // set は Interlocked.Exchange(_,1)、consume は Interlocked.Exchange(_,0)!=0 で atomic に handoff する。
    // 0 = pending なし / 1 = pending あり。
    private int _fruSuppressionSelectionChangePending;

    // Phase A 診断（非永続）: 直近の選択変更イベントの monotonic tick（ms）と、その観測時 scene generation。
    // detour スレッド（OnCharaSelectSelectionChanged）で publish し、Framework スレッドの re-arm で読む。
    // int / long の単純 racy read で足りる（診断値のみ。write ゲートには使わない）。
    private long _fruSuppressionSelectionChangeEventTickMs;
    private int _fruSuppressionSelectionChangeGenerationAtEvent = -1;

    // OnFrameworkUpdate から毎フレーム呼ぶ。FRU candidate かつ pre-login + CharaSelect session + hook Ready +
    // static-anchor 認可済み + current lobby map == CharaSelect + loaded ActiveLayout（InitState 7 /
    // territory 1238 / layer 0）のときだけ 1 パス走査する。window が閉じたら以降書かない。
    private void MaintainFruSceneObjectSuppression()
    {
        var candidate = ResolveCurrentOverrideCandidate();
        var candidateIsFru = string.Equals(
            candidate.Id,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
            StringComparison.Ordinal);
        if (!candidateIsFru)
        {
            return;
        }

        if (_clientState.IsLoggedIn)
        {
            // login 済み: これ以降は一切書き込まない。直前の window 状態を凍結する。
            System.Threading.Interlocked.Exchange(ref _fruSuppressionSelectionChangePending, 0);
            if (_charaSelectSceneObjectSuppression.Attempted)
            {
                _charaSelectSceneObjectSuppression.RecordCleanupState("stopped-on-login");
            }

            return;
        }

        if (!_charaSelectTitleBackgroundSessionActive
            || _hookLifecycle.State != TitleBackgroundServiceState.Ready)
        {
            _charaSelectSceneObjectSuppression.RecordGateStatus("session-or-hook-not-ready");
            return;
        }

        // current lobby map が CharaSelect であることを確認する。
        if (!TryReadCurrentLobbyMap(out var lobbyMap)
            || !TitleBackgroundCharaSelectPlacementLogic.IsCharaSelectMap(lobbyMap))
        {
            _charaSelectSceneObjectSuppression.RecordGateStatus("not-chara-select-map");
            return;
        }

        var generation = _activeCharaSelectSceneGeneration;
        if (generation <= 0)
        {
            _charaSelectSceneObjectSuppression.RecordGateStatus("scene-generation-not-observed");
            return;
        }

        // scene identity は static-anchor authorization を再利用する。ただし snapshot 自体に
        // frame identity が無いため、当該 snapshot が「今の scene generation のもの」であることを
        // 明示的に要求する（この frame で MaintainTitleEditInformedCharaSelectPlacement が同一
        // generation に対し評価した結果であることの保証）。authorized のとき applied path /
        // applied territory 1238 / explicit layer 0 / loaded ActiveLayout InitState==7 /
        // loaded layout territory 1238 が全て確認済み。
        var anchorSnapshot = _charaSelectStaticAnchor.Snapshot;
        if (!_charaSelectStaticAnchor.TryGetAuthorizedAnchor(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                out _)
            || anchorSnapshot.SceneGeneration != generation)
        {
            _charaSelectSceneObjectSuppression.RecordGateStatus(
                $"scene-not-authorized:{anchorSnapshot.AuthorizationReason}:gen({anchorSnapshot.SceneGeneration}/{generation})");
            return;
        }

        // キャラ切替でゲームが SharedGroup を再 active 化しうるため、同一 scene generation でも
        // fresh window を re-arm する（既存 SelectedCharacterChanged イベントを利用。新 hook なし）。
        // atomic handoff: pending を read しつつ 0 へ戻す（取りこぼし防止）。
        var forceReArm = System.Threading.Interlocked.Exchange(ref _fruSuppressionSelectionChangePending, 0) != 0;
        if (forceReArm)
        {
            // Phase A: この選択変更イベントの証拠（event->re-arm の monotonic 経過・世代）を記録する。
            var eventTick = System.Threading.Volatile.Read(ref _fruSuppressionSelectionChangeEventTickMs);
            var eventToReArmMs = eventTick > 0 ? Math.Max(0L, Environment.TickCount64 - eventTick) : -1L;
            _charaSelectSceneObjectSuppression.NoteSelectionChangeReArm(
                System.Threading.Volatile.Read(ref _fruSuppressionSelectionChangeGenerationAtEvent),
                generation,
                eventTick,
                eventToReArmMs);
        }

        _charaSelectSceneObjectSuppression.ArmForGeneration(generation, forceReArm);
        if (!_charaSelectSceneObjectSuppression.ShouldRunPass())
        {
            _charaSelectSceneObjectSuppression.RecordGateStatus("window-closed");
            _charaSelectSceneObjectSuppression.RecordCleanupState(
                $"window-closed:{_charaSelectSceneObjectSuppression.StopReason}");
            return;
        }

        try
        {
            var layoutWorld = LayoutWorld.Instance();
            var activeLayout = layoutWorld == null ? null : layoutWorld->ActiveLayout;
            if (activeLayout == null)
            {
                _charaSelectSceneObjectSuppression.RecordGateStatus("active-layout-null");
                return;
            }

            // 厳密確認（fail-closed: TerritoryTypeId==0 も不一致扱い）。初期化途中の不一致は
            // failure ではなく gate status（次フレーム再試行）。
            if (activeLayout->InitState != 7)
            {
                _charaSelectSceneObjectSuppression.RecordGateStatus("active-layout-not-ready");
                return;
            }

            if (activeLayout->TerritoryTypeId != candidate.TerritoryId)
            {
                _charaSelectSceneObjectSuppression.RecordGateStatus("loaded-layout-territory-mismatch");
                return;
            }

            if (activeLayout->LayerFilterKey != candidate.LayerFilterKey)
            {
                _charaSelectSceneObjectSuppression.RecordGateStatus("loaded-layout-layer-mismatch");
                return;
            }

            _charaSelectSceneObjectSuppression.RecordGateStatus("authorized");

            // Phase A: 選択変更後の最初の実パスなら、event->first-pass の monotonic 経過を確定する。
            if (_charaSelectSceneObjectSuppression.AwaitingFirstReArmedPass)
            {
                var eventTick = _charaSelectSceneObjectSuppression.SelectionChangeEventTickMs;
                var eventToFirstPassMs = eventTick > 0
                    ? Math.Max(0L, Environment.TickCount64 - eventTick)
                    : -1L;
                _charaSelectSceneObjectSuppression.MarkFirstReArmedPassStarting(eventToFirstPassMs);
            }

            _charaSelectSceneObjectSuppression.BeginPass();
            SuppressSharedGroups(activeLayout);
            _charaSelectSceneObjectSuppression.EndPass();
            _charaSelectSceneObjectSuppression.RecordCleanupState(
                _charaSelectSceneObjectSuppression.Completed
                    ? $"window-closed:{_charaSelectSceneObjectSuppression.StopReason}"
                    : "running-pre-login");
        }
        catch (Exception ex)
        {
            _charaSelectSceneObjectSuppression.RecordFailure($"exception:{ex.GetType().Name}");
            _log.Warning(ex, "[XMU BG] FRU scene-object suppression pass failed.");
        }
    }

    // SharedGroup instance を走査し、deny 判定のものを bounded に SetActive(false) する。
    // pointer はこの呼び出し内だけで使う。既に inactive なものは write しない（idempotent）。
    private void SuppressSharedGroups(LayoutManager* activeLayout)
    {
        if (!activeLayout->InstancesByType.TryGetValuePointer(InstanceType.SharedGroup, out var innerMapPtr)
            || innerMapPtr == null)
        {
            return;
        }

        var innerMap = innerMapPtr->Value;
        if (innerMap == null)
        {
            return;
        }

        foreach (var entry in *innerMap)
        {
            var instance = entry.Item2.Value;
            if (instance == null)
            {
                continue;
            }

            _charaSelectSceneObjectSuppression.RecordScanned();

            string primaryPath;
            try
            {
                var cptr = instance->GetPrimaryPath();
                primaryPath = cptr.HasValue ? cptr.ToString() : string.Empty;
            }
            catch (Exception ex)
            {
                // instance を評価できなかった -> このパスは stable にしない。
                _charaSelectSceneObjectSuppression.MarkPassDirty();
                _charaSelectSceneObjectSuppression.RecordFailure($"primary-path:{ex.GetType().Name}");
                continue;
            }

            var decision = TitleBackgroundCharaSelectSceneObjectSuppressionLogic.Evaluate(primaryPath, true);
            if (decision.Verdict != TitleBackgroundSceneObjectSuppressionVerdict.Suppress)
            {
                // Phase A coverage-gap 証拠: 最初の re-arm パスでだけ、deny token 非該当（"no-deny-token"）で
                // active な SharedGroup の game-asset primary path を bounded に採取する。keep-token 一致
                // （花畑 / 床 / 遠景 / 照明 = 意図的存置）は対象外。ここでは write を一切しない。
                if (decision.Verdict == TitleBackgroundSceneObjectSuppressionVerdict.Keep
                    && string.Equals(decision.Reason, "no-deny-token", StringComparison.Ordinal)
                    && _charaSelectSceneObjectSuppression.CapturingFirstReArmedPass)
                {
                    try
                    {
                        if (instance->IsActive)
                        {
                            _charaSelectSceneObjectSuppression.RecordActiveNonDenyKeepPath(primaryPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _charaSelectSceneObjectSuppression.RecordFailure($"coverage-probe:{ex.GetType().Name}");
                    }
                }

                continue;
            }

            var key = entry.Item1;
            _charaSelectSceneObjectSuppression.RecordMatched(key);

            bool isActive;
            try
            {
                isActive = instance->IsActive;
            }
            catch (Exception ex)
            {
                _charaSelectSceneObjectSuppression.MarkPassDirty();
                _charaSelectSceneObjectSuppression.RecordFailure($"is-active:{ex.GetType().Name}");
                continue;
            }

            if (!isActive)
            {
                _charaSelectSceneObjectSuppression.RecordAlreadyInactive(key);
                continue;
            }

            if (!_charaSelectSceneObjectSuppression.TryConsumeWriteBudget(key))
            {
                // per-instance budget 枯渇。まだ active なのでこのパスは stable にしない。
                _charaSelectSceneObjectSuppression.RecordBudgetExhausted(key);
                continue;
            }

            try
            {
                instance->SetActive(false);
                _charaSelectSceneObjectSuppression.RecordWriteAttempted(key, "SharedGroup");

                // readback（realized state は 1 フレーム遅れることがある。未確定なら次パスで再確認）。
                if (!instance->IsActive)
                {
                    _charaSelectSceneObjectSuppression.RecordConfirmedInactive(key);
                }
                else
                {
                    _charaSelectSceneObjectSuppression.RecordStillActive(key);
                }
            }
            catch (Exception ex)
            {
                _charaSelectSceneObjectSuppression.MarkPassDirty();
                _charaSelectSceneObjectSuppression.RecordFailure($"set-active:{ex.GetType().Name}");
            }
        }
    }
}
