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

    // Phase A UX gap の最小修正: 通常のキャラ/ワールド選択変更 1 回で完結する selection-change レポートの
    // publish / auto-copy。bounded diagnostic が分類状態 / window 終了 / bounded timeout / session 終了に
    // 到達した時点で 1 度だけ、専用ファイル（title-background-selection-change-diag.txt）へ保存し
    // clipboard キューへ積む。OneClick / AutomaticQuickCheck は開始しない。config も一切触らない。
    // 「どの選択変更ぶんを publish 済みか」を SelectionChangeReArmCount で追跡する（次の switch で
    // カウントが進み再度 publish 可能に。Reset で 0 へ戻れば publishedForReArmCount も 0 に戻す）。
    private int _fruSelectionChangeReportPublishedForReArmCount;
    private string _fruSelectionChangePendingClipboardText = string.Empty;

    // Phase A 最終診断（review 5521774559）: 1 回の通常の選択変更で sharedgroup-delta / vfx-delta /
    // no-safe-layout-delta / incomplete のいずれかへ確定させる、bounded・READ-ONLY・managed な delta 証拠。
    // WRITE window / deny list / SetActive / native hook は一切変更しない。VFX write なし。
    private readonly TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState _charaSelectSelectionChangeDelta = new();

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

            // session 終了は hard stop: READ-ONLY follow-up / 最終 delta 観測窓を安全停止し、
            // 未 publish の selection-change レポートがあれば今出す。
            _charaSelectSceneObjectSuppression.StopCoverageFollowUp("session-end");
            _charaSelectSelectionChangeDelta.MarkSessionEnd();
            MaybePublishFruSelectionChangeReport(candidate.Id, sessionEnding: true);
            return;
        }

        // pre-login: READ-ONLY coverage follow-up の時計を進め（native 読取なし・timeout は延長しない）、
        // 分類確定 / follow-up terminal / bounded timeout に到達していれば selection-change レポートを
        // 1 度だけ publish する（後続の gate 失敗フレームでも出せるよう、ここで判定する）。
        AdvanceFruCoverageFollowUpClock();
        MaybePublishFruSelectionChangeReport(candidate.Id, sessionEnding: false);

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

            // 最終診断: managed baseline を snapshot する（native scan は増やさない）。
            // - SharedGroup baseline = 直近の authorized な通常 suppression pass の path->activeCount。
            // - VFX baseline = 既存 VFX inventory の最新 managed スナップショット（frozen で可）。
            _charaSelectSelectionChangeDelta.ArmFromReArm(_charaSelectVfxInventory.DetailSnapshot);
        }

        _charaSelectSceneObjectSuppression.ArmForGeneration(generation, forceReArm);
        if (!_charaSelectSceneObjectSuppression.ShouldRunPass())
        {
            _charaSelectSceneObjectSuppression.RecordGateStatus("window-closed");
            _charaSelectSceneObjectSuppression.RecordCleanupState(
                $"window-closed:{_charaSelectSceneObjectSuppression.StopReason}");
            // WRITE window は終了済み（stable / no-match / budget）。以降は再開しない。
            // 代わりに、識別子ゲートを満たすフレームで READ-ONLY coverage follow-up の 1 パスを走らせる。
            ScanFruCoverageFollowUpPass(candidate);
            return;
        }

        try
        {
            if (!TryResolveAuthorizedFruActiveLayout(candidate, out var activeLayout, out var writeGateStatus))
            {
                _charaSelectSceneObjectSuppression.RecordGateStatus(writeGateStatus);
                return;
            }

            _charaSelectSceneObjectSuppression.RecordGateStatus(writeGateStatus);

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
            // 最終診断: 同じ WRITE scan の中で eligible SharedGroup の active-instance 数も数える
            // （新しい native scan は増やさない）。再 arm 前は baseline source、再 arm 後は delta として畳み込む。
            _charaSelectSelectionChangeDelta.BeginSharedGroupPass();
            var writeSgScanOk = SuppressSharedGroups(activeLayout);
            _charaSelectSceneObjectSuppression.EndPass();
            _charaSelectSelectionChangeDelta.FinishSharedGroupPass(writeSgScanOk, ComputeSelectionChangeElapsedMs());
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

    // loaded ActiveLayout の identity を厳密確認する（fail-closed: TerritoryTypeId==0 も不一致扱い）。
    // WRITE パスと READ-ONLY coverage follow-up パスで共有する。native 読取のみ。呼び出し側で
    // gate status を記録し、認可されないフレームでは以降の native instance 読取を行わないこと。
    // 認可に成功したときの gateStatus は "authorized"。
    private bool TryResolveAuthorizedFruActiveLayout(
        in TitleBackgroundCharacterSelectOverrideCandidate candidate,
        out LayoutManager* activeLayout,
        out string gateStatus)
    {
        activeLayout = null;
        var layoutWorld = LayoutWorld.Instance();
        var layout = layoutWorld == null ? null : layoutWorld->ActiveLayout;
        if (layout == null)
        {
            gateStatus = "active-layout-null";
            return false;
        }

        if (layout->InitState != 7)
        {
            gateStatus = "active-layout-not-ready";
            return false;
        }

        if (layout->TerritoryTypeId != candidate.TerritoryId)
        {
            gateStatus = "loaded-layout-territory-mismatch";
            return false;
        }

        if (layout->LayerFilterKey != candidate.LayerFilterKey)
        {
            gateStatus = "loaded-layout-layer-mismatch";
            return false;
        }

        activeLayout = layout;
        gateStatus = "authorized";
        return true;
    }

    // selection-change イベントからの monotonic 経過 ms（未観測なら -1）。native 読取なし。
    private long ComputeSelectionChangeElapsedMs()
    {
        var eventTick = _charaSelectSceneObjectSuppression.SelectionChangeEventTickMs;
        return eventTick > 0 ? Math.Max(0L, Environment.TickCount64 - eventTick) : -1L;
    }

    // 最終診断の bounded 観測窓（= 2500ms READ-ONLY window）の時計だけを進める。
    // 毎 pre-login フレームで呼ぶ。native 読取なし。1 回の選択変更 + WRITE window 終了で常に arm し、
    // 2500ms 完走でのみ stop（旧 classifier が positive になっても早期停止しない）。
    // identity gate 待ちでも timeout は延長しない。
    private void AdvanceFruCoverageFollowUpClock()
    {
        var st = _charaSelectSceneObjectSuppression;

        if (!st.CoverageFollowUpArmed
            && _charaSelectSelectionChangeDelta.Armed
            && TitleBackgroundCharaSelectSceneObjectSuppressionLogic.ShouldArmCoverageFollowUp(
                st.SelectionChangeReArmCount > 0,
                st.Completed))
        {
            st.ArmCoverageFollowUp(Environment.TickCount64);
        }

        if (!st.CoverageFollowUpActive)
        {
            return;
        }

        var elapsedMs = Math.Max(0L, Environment.TickCount64 - st.CoverageFollowUpStartTickMs);
        st.RecordCoverageFollowUpElapsed(elapsedMs);

        if (elapsedMs >= TitleBackgroundCharaSelectSceneObjectSuppressionLogic.CoverageFollowUpDurationMs)
        {
            st.StopCoverageFollowUp("followup-timeout");
            _charaSelectSelectionChangeDelta.MarkWindowComplete();
        }
    }

    // READ-ONLY coverage follow-up の 1 パス。identity gate を満たすフレームでのみ native を読む。
    // 満たさないフレームでは何も読まず待つ（timeout は AdvanceFruCoverageFollowUpClock 側で進む）。
    // MUST FIX (review 5098578049): identity resolve（native 読取を含む）も follow-up scan と
    // 同じ exception boundary の中で行う。例外時は fail closed で戻る（native write は行わない）。
    private void ScanFruCoverageFollowUpPass(in TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        var st = _charaSelectSceneObjectSuppression;
        if (!st.CoverageFollowUpActive)
        {
            return;
        }

        try
        {
            // gate 未成立（例外ではない）は従来どおり gate status を記録して待つ。
            // TryResolveAuthorizedFruActiveLayout の 4 チェック・順序は変更しない。
            if (!TryResolveAuthorizedFruActiveLayout(candidate, out var activeLayout, out var gate))
            {
                st.RecordGateStatus($"coverage-followup:{gate}");
                _charaSelectSelectionChangeDelta.RecordGateBlockedPass();
                return;
            }

            var elapsedMs = ComputeSelectionChangeElapsedMs();

            st.BeginCoverageFollowUpPass();
            ScanCoverageFollowUpSampledPaths(activeLayout);
            st.EndCoverageFollowUpPass();

            // 最終診断: 同じ authorized フレームで eligible SharedGroup の count-delta と typed VFX delta を
            // READ-ONLY 観測する（active 切替なし・deny 変更なし・VFX write なし・pointer 保持なし）。
            _charaSelectSelectionChangeDelta.BeginSharedGroupPass();
            var sgOk = ScanSelectionChangeDeltaSharedGroups(activeLayout);
            _charaSelectSelectionChangeDelta.FinishSharedGroupPass(sgOk, elapsedMs);

            _charaSelectSelectionChangeDelta.BeginVfxPass();
            var vfxOk = ScanSelectionChangeDeltaVfx(activeLayout);
            _charaSelectSelectionChangeDelta.FinishVfxPass(vfxOk);
        }
        catch (Exception ex)
        {
            // fail closed: 例外時は何も観測せず戻る。native write は元々一切行わない。
            st.RecordFailure($"coverage-followup-scan:{ex.GetType().Name}");
            _charaSelectSelectionChangeDelta.RecordReadFailure();
            return;
        }
    }

    // 最終診断: eligible SharedGroup path（SharedGroup + non-deny + 非 KeepPathToken）の
    // active-instance 数をこの 1 パスで数える。READ-ONLY（GetPrimaryPath / IsActive のみ）。
    // 戻り値 = SharedGroup map を最後まで走査できたか（partial/failed なら false で 0 を合成させない）。
    private bool ScanSelectionChangeDeltaSharedGroups(LayoutManager* activeLayout)
    {
        if (!activeLayout->InstancesByType.TryGetValuePointer(InstanceType.SharedGroup, out var innerMapPtr)
            || innerMapPtr == null)
        {
            return false;
        }

        var innerMap = innerMapPtr->Value;
        if (innerMap == null)
        {
            return false;
        }

        foreach (var entry in *innerMap)
        {
            var instance = entry.Item2.Value;
            if (instance == null)
            {
                continue;
            }

            string primaryPath;
            try
            {
                var cptr = instance->GetPrimaryPath();
                primaryPath = cptr.HasValue ? cptr.ToString() : string.Empty;
            }
            catch (Exception ex)
            {
                _charaSelectSelectionChangeDelta.RecordReadFailure();
                _charaSelectSceneObjectSuppression.RecordFailure($"delta-sg-path:{ex.GetType().Name}");
                continue;
            }

            if (!TitleBackgroundCharaSelectSceneObjectSuppressionLogic.IsEligibleDeltaSharedGroupPath(primaryPath))
            {
                continue;
            }

            bool isActive;
            try
            {
                isActive = instance->IsActive;
            }
            catch (Exception ex)
            {
                _charaSelectSelectionChangeDelta.RecordReadFailure();
                _charaSelectSceneObjectSuppression.RecordFailure($"delta-sg-active:{ex.GetType().Name}");
                continue;
            }

            _charaSelectSelectionChangeDelta.RecordSharedGroupInstance(
                TitleBackgroundCharaSelectSceneObjectSuppressionLogic.SanitizeGameAssetPathForDiagnostics(primaryPath),
                isActive);
        }

        return true;
    }

    // 最終診断: InstanceType.Vfx を READ-ONLY 観測する。既存 ScanVfxInventory と同じ typed read だけを使う
    // （Id.InstanceKey / SubId / IsActive / GetPrimaryPath / IsPrimaryLoaded / GetGraphics()!=null）。
    // VFX write は 1 件も行わない。戻り値 = Vfx map を最後まで走査できたか。
    private bool ScanSelectionChangeDeltaVfx(LayoutManager* activeLayout)
    {
        if (!activeLayout->InstancesByType.TryGetValuePointer(InstanceType.Vfx, out var innerMapPtr)
            || innerMapPtr == null)
        {
            return false;
        }

        var innerMap = innerMapPtr->Value;
        if (innerMap == null)
        {
            return false;
        }

        var scanned = 0;
        foreach (var entry in *innerMap)
        {
            if (scanned >= TitleBackgroundSelectionChangeDeltaLogic.MaxVfxScanEntries)
            {
                break;
            }

            var instance = entry.Item2.Value;
            if (instance == null)
            {
                continue;
            }

            scanned++;
            try
            {
                var subId = instance->SubId;
                var instanceKey = instance->Id.InstanceKey;
                var isActive = instance->IsActive;
                var cptr = instance->GetPrimaryPath();
                var primaryPath = cptr.HasValue ? cptr.ToString() : string.Empty;
                var isPrimaryLoaded = instance->IsPrimaryLoaded();
                var hasGraphicsObject = instance->GetGraphics() != null;

                _charaSelectSelectionChangeDelta.RecordVfxInstance(
                    TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(instanceKey, subId),
                    isActive,
                    isPrimaryLoaded,
                    hasGraphicsObject,
                    TitleBackgroundCharaSelectVfxInventoryLogic.HashPath(primaryPath),
                    primaryPath);
            }
            catch (Exception ex)
            {
                _charaSelectSelectionChangeDelta.RecordReadFailure();
                _charaSelectSceneObjectSuppression.RecordFailure($"delta-vfx:{ex.GetType().Name}");
            }
        }

        return true;
    }

    // sampled non-deny path の instance だけを READ-ONLY 観測する。
    // 許可 native 操作: GetPrimaryPath / IsActive のみ。SetActive / deny 変更 / pointer 保持なし。
    private void ScanCoverageFollowUpSampledPaths(LayoutManager* activeLayout)
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

            string primaryPath;
            try
            {
                var cptr = instance->GetPrimaryPath();
                primaryPath = cptr.HasValue ? cptr.ToString() : string.Empty;
            }
            catch (Exception ex)
            {
                _charaSelectSceneObjectSuppression.RecordFailure($"coverage-followup-path:{ex.GetType().Name}");
                continue;
            }

            // sampled path 以外は触らない（follow-up は sampled path のみ観測）。
            if (!_charaSelectSceneObjectSuppression.IsSampledNonDenyKeepPath(primaryPath))
            {
                continue;
            }

            bool isActive;
            try
            {
                isActive = instance->IsActive;
            }
            catch (Exception ex)
            {
                _charaSelectSceneObjectSuppression.RecordFailure($"coverage-followup-active:{ex.GetType().Name}");
                continue;
            }

            _charaSelectSceneObjectSuppression.RecordNonDenyKeepPathFollowUp(primaryPath, isActive);
        }
    }

    // SharedGroup instance を走査し、deny 判定のものを bounded に SetActive(false) する。
    // pointer はこの呼び出し内だけで使う。既に inactive なものは write しない（idempotent）。
    // 戻り値 = SharedGroup map を最後まで走査できたか（最終診断の count-delta が partial pass で
    // 0 を合成しないための valid フラグ。suppression の write / stable 判定挙動は変更なし）。
    private bool SuppressSharedGroups(LayoutManager* activeLayout)
    {
        if (!activeLayout->InstancesByType.TryGetValuePointer(InstanceType.SharedGroup, out var innerMapPtr)
            || innerMapPtr == null)
        {
            return false;
        }

        var innerMap = innerMapPtr->Value;
        if (innerMap == null)
        {
            return false;
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
                // instance を評価できなかった -> このパスは stable にしない（suppression semantics 不変）。
                // MUST FIX (review 5098946239 #2): verdict 不明 = 最終診断側は fail-closed でこの
                // WRITE pass を invalidate する（eligible path の取りこぼしを delta 化しない）。
                _charaSelectSceneObjectSuppression.MarkPassDirty();
                _charaSelectSceneObjectSuppression.RecordFailure($"primary-path:{ex.GetType().Name}");
                _charaSelectSelectionChangeDelta.RecordReadFailure();
                continue;
            }

            var decision = TitleBackgroundCharaSelectSceneObjectSuppressionLogic.Evaluate(primaryPath, true);
            if (decision.Verdict != TitleBackgroundSceneObjectSuppressionVerdict.Suppress)
            {
                // deny token 非該当（"no-deny-token"）の SharedGroup のみ。ここでは write を一切しない。
                // deny list / Evaluate / KeepPathTokens は変更しない。
                if (decision.Verdict == TitleBackgroundSceneObjectSuppressionVerdict.Keep
                    && string.Equals(decision.Reason, "no-deny-token", StringComparison.Ordinal))
                {
                    try
                    {
                        var nonDenyActive = instance->IsActive;
                        var sanitizedDiag = TitleBackgroundCharaSelectSceneObjectSuppressionLogic
                            .SanitizeGameAssetPathForDiagnostics(primaryPath);

                        if (_charaSelectSceneObjectSuppression.CapturingFirstReArmedPass)
                        {
                            // 最初の re-arm パス: active な非-deny 構造 SharedGroup の path を bounded に採取。
                            if (nonDenyActive)
                            {
                                _charaSelectSceneObjectSuppression.RecordActiveNonDenyKeepPath(primaryPath);
                            }
                        }
                        else if (_charaSelectSceneObjectSuppression.ShouldFollowUpNonDenyKeepPaths)
                        {
                            // 後続パス（同一 bounded window 内）: 採取済み path が active -> inactive したかだけを
                            // read-only で確認する。
                            _charaSelectSceneObjectSuppression.RecordNonDenyKeepPathFollowUp(primaryPath, nonDenyActive);
                        }

                        // 最終診断 (review 5521774559 A): eligible SharedGroup（非 keep-token）の
                        // active-instance 数をこの WRITE scan で数える（新しい native scan は増やさない）。
                        if (sanitizedDiag.Length != 0
                            && !TitleBackgroundSelectionChangeDeltaLogic.MatchesKnownKeepToken(sanitizedDiag))
                        {
                            _charaSelectSelectionChangeDelta.RecordSharedGroupInstance(sanitizedDiag, nonDenyActive);
                        }
                    }
                    catch (Exception ex)
                    {
                        // suppression 側の dirty/stable semantics は変えない（この catch は非-deny 診断計数のみ）。
                        // MUST FIX (review 5098946239 #2): 最終診断側はこの WRITE pass を invalidate し、
                        // baseline->0 を合成させない。
                        _charaSelectSceneObjectSuppression.RecordFailure($"coverage-probe:{ex.GetType().Name}");
                        _charaSelectSelectionChangeDelta.RecordReadFailure();
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

        return true;
    }

    // Phase A UX gap の最小修正。選択変更が 1 回観測され、その分類が確定した / bounded window が終了した /
    // bounded timeout を超えた / session が終了した時点で、selection-change レポートを 1 度だけ
    // 専用ファイルへ保存し、clipboard キューへ積む。OneClick / AutomaticQuickCheck は開始しない。
    // config mutation なし。deny list / suppression timing / fade semantics / native hook 変更なし。
    private void MaybePublishFruSelectionChangeReport(string candidateId, bool sessionEnding)
    {
        var suppression = _charaSelectSceneObjectSuppression;
        var reArmCount = suppression.SelectionChangeReArmCount;

        // Reset / new scene generation / candidate 変更で証拠がクリアされ再カウントが始まったら、
        // publish 済みマーカーも戻す（カウントが後退した = 別セッション扱い）。
        if (reArmCount < _fruSelectionChangeReportPublishedForReArmCount)
        {
            _fruSelectionChangeReportPublishedForReArmCount = 0;
        }

        if (reArmCount == 0 || reArmCount <= _fruSelectionChangeReportPublishedForReArmCount)
        {
            // まだ選択変更が無い、またはこの（もしくは後続の）選択変更ぶんは publish 済み。
            return;
        }

        var selectionChangeClass = suppression.SelectionChangeClass;
        var eventTick = suppression.SelectionChangeEventTickMs;
        var elapsedMs = eventTick > 0 ? Math.Max(0L, Environment.TickCount64 - eventTick) : -1L;

        // 最終診断が armed なら、旧 classifier が positive になっても早期 publish せず、
        // bounded 観測窓（2500ms）が terminal / session end になるまで待つ。
        if (!TitleBackgroundCharaSelectSceneObjectSuppressionLogic.SelectionChangeReportReady(
                selectionChangeClass,
                suppression.Completed,
                elapsedMs,
                sessionEnding,
                suppression.ActiveNonDenyKeepPathSampleCount,
                suppression.CoverageFollowUpTerminal,
                _charaSelectSelectionChangeDelta.Armed,
                _charaSelectSelectionChangeDelta.Complete))
        {
            return;
        }

        var trigger = sessionEnding
            ? "session-end"
            : _charaSelectSelectionChangeDelta.Armed
                ? $"final:{_charaSelectSelectionChangeDelta.Outcome}"
                : selectionChangeClass != TitleBackgroundSceneObjectSelectionChangeClass.InsufficientEvidence
                    ? "classified"
                    : suppression.CoverageFollowUpTerminal
                        ? $"coverage-followup:{suppression.CoverageFollowUpStopReason}"
                        : suppression.Completed
                            ? "window-closed"
                            : "timeout";

        string report;
        try
        {
            var reportLines = suppression.BuildDiagnosticLines(candidateId, true)
                .Concat(_charaSelectSelectionChangeDelta.BuildFinalDiagnosticLines())
                .ToArray();
            report = TitleBackgroundSelectionChangeReportBuilder.Build(
                DateTimeOffset.Now,
                candidateId,
                trigger,
                reportLines);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] Failed to build FRU selection-change diagnostic.");
            return;
        }

        // clipboard 引き渡しを primary contract にする（file 保存は best-effort）。
        _fruSelectionChangeReportPublishedForReArmCount = reArmCount;
        _fruSelectionChangePendingClipboardText = report;

        try
        {
            Directory.CreateDirectory(_configDirectory);
            File.WriteAllText(
                Path.Combine(_configDirectory, TitleBackgroundSelectionChangeReportBuilder.FileName),
                report);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] Failed to persist FRU selection-change diagnostic.");
        }

        _log.Information(
            "[XMU BG] FRU selection-change diagnostic published. trigger={Trigger} class={Class} chars={Chars}",
            trigger,
            selectionChangeClass,
            report.Length);
    }

    // Plugin.UiEvents の Draw ハンドラが 1 フレームに 1 回消費する（既存 auto-check clipboard と同じパターン）。
    internal bool TryConsumeFruSelectionChangeClipboardText(out string text)
    {
        text = _fruSelectionChangePendingClipboardText;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        _fruSelectionChangePendingClipboardText = string.Empty;
        return true;
    }
}
