// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.VfxInventory.cs
// Description: FRU クリア後ステージ candidate 固有の、read-only な InstanceType.Vfx インベントリ収集。
//              pre-login CharaSelect で、既存 FRU static-anchor authorization / current scene generation /
//              loaded ActiveLayout（InitState 7 / territory 1238 / explicit layer 0）の厳密確認を
//              通ったときだけ、loaded VFX instance を 1 パス走査して compact な要約を run-scoped 状態へ記録する。
// Reason: FRU 本来の ambient VFX が clear-stage で欠けている可能性の切り分けに、現行の型付き API で
//         安全に読める最小限（stable identity / IsActive / primary path / IsPrimaryLoaded /
//         GraphicsObject 有無 / path hash）だけを OneClick final report へ自動統合する。
//         Checkpoint 1: VFX write は 1 件も行わない（SetActive / vfunc54 / trigger-index / raw offset write なし）。
//         native pointer は各パス内でだけ使い、field / runtime state へ保持しない。
//         login / session 終了 / scene generation 変化で以降のパスを停止する。
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    private readonly TitleBackgroundCharaSelectVfxInventoryRuntimeState _charaSelectVfxInventory = new();

    // OnFrameworkUpdate から毎フレーム呼ぶ。gate は MaintainFruSceneObjectSuppression と同等で、
    // 成立時に read-only の 1 パス走査を行う。window（stable / pass 予算）を使い切ったら以降走査しない。
    private void MaintainFruVfxInventory()
    {
        var candidate = ResolveCurrentOverrideCandidate();
        var candidateIsFru = string.Equals(
            candidate.Id,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
            StringComparison.Ordinal);

        var generation = _activeCharaSelectSceneGeneration;
        var charaSelectMap = TryReadCurrentLobbyMap(out var lobbyMap)
            && TitleBackgroundCharaSelectPlacementLogic.IsCharaSelectMap(lobbyMap);
        var anchorSnapshot = _charaSelectStaticAnchor.Snapshot;
        var anchorAuthorized = _charaSelectStaticAnchor.TryGetAuthorizedAnchor(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
            out _);

        // 先に native を読まない cheap gate を評価する（suppression と同じく native ActiveLayout の
        // 読取は全ての手前 gate を通ったときだけ行う）。ここでは loaded-layout の値を「まだ未確認」
        // として渡す（activeLayoutAvailable=false）。
        var preGate = TitleBackgroundCharaSelectVfxInventoryLogic.Evaluate(
            candidateIsFru,
            _clientState.IsLoggedIn,
            _charaSelectTitleBackgroundSessionActive,
            _hookLifecycle.State == TitleBackgroundServiceState.Ready,
            charaSelectMap,
            generation,
            anchorAuthorized,
            anchorSnapshot.SceneGeneration,
            activeLayoutAvailable: false,
            activeLayoutInitState: -1,
            loadedLayoutTerritoryTypeId: 0,
            loadedLayoutLayerFilterKey: uint.MaxValue,
            candidate.TerritoryId,
            candidate.LayerFilterKey);
        if (preGate.Gate != TitleBackgroundVfxInventoryGate.ActiveLayoutNotReady
            && !preGate.ShouldCollect)
        {
            _charaSelectVfxInventory.RecordGateStatus(preGate.Reason);
            return;
        }

        // ここから native ActiveLayout を読む（厳密確認値。pointer は保持しない）。
        var activeLayoutAvailable = false;
        var activeLayoutInitState = -1;
        uint loadedTerritory = 0;
        uint loadedLayer = uint.MaxValue;
        LayoutManager* activeLayout = null;
        try
        {
            var layoutWorld = LayoutWorld.Instance();
            activeLayout = layoutWorld == null ? null : layoutWorld->ActiveLayout;
            if (activeLayout != null)
            {
                activeLayoutAvailable = true;
                activeLayoutInitState = activeLayout->InitState;
                loadedTerritory = activeLayout->TerritoryTypeId;
                loadedLayer = activeLayout->LayerFilterKey;
            }
        }
        catch (Exception ex)
        {
            activeLayout = null;
            _charaSelectVfxInventory.RecordFailure($"active-layout:{ex.GetType().Name}");
        }

        var gate = TitleBackgroundCharaSelectVfxInventoryLogic.Evaluate(
            candidateIsFru,
            _clientState.IsLoggedIn,
            _charaSelectTitleBackgroundSessionActive,
            _hookLifecycle.State == TitleBackgroundServiceState.Ready,
            charaSelectMap,
            generation,
            anchorAuthorized,
            anchorSnapshot.SceneGeneration,
            activeLayoutAvailable,
            activeLayoutInitState,
            loadedTerritory,
            loadedLayer,
            candidate.TerritoryId,
            candidate.LayerFilterKey);

        _charaSelectVfxInventory.RecordGateStatus(gate.Reason);
        if (!gate.ShouldCollect)
        {
            return;
        }

        _charaSelectVfxInventory.ArmForGeneration(generation);
        if (!_charaSelectVfxInventory.ShouldRunPass())
        {
            _charaSelectVfxInventory.RecordGateStatus($"window-closed:{_charaSelectVfxInventory.StopReason}");
            return;
        }

        try
        {
            _charaSelectVfxInventory.BeginPass();
            ScanVfxInventory(activeLayout);
            _charaSelectVfxInventory.EndPass();
        }
        catch (Exception ex)
        {
            _charaSelectVfxInventory.RecordFailure($"scan:{ex.GetType().Name}");
            _log.Warning(ex, "[XMU BG] FRU VFX inventory pass failed.");
        }
    }

    // loaded ActiveLayout の InstanceType.Vfx を read-only に走査する。write は一切しない。
    // pointer はこの呼び出し内でだけ使う。
    private void ScanVfxInventory(LayoutManager* activeLayout)
    {
        if (activeLayout == null
            || !activeLayout->InstancesByType.TryGetValuePointer(InstanceType.Vfx, out var innerMapPtr)
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
            if (!_charaSelectVfxInventory.CanScanMore())
            {
                break;
            }

            var instance = entry.Item2.Value;
            if (instance == null)
            {
                continue;
            }

            var key = entry.Item1;
            try
            {
                var subId = instance->SubId;
                // 型付きフィールド（API15 build surface で利用可能）。map key からの推測ではなく
                // これを TitleEdit UUID 導出の第一ソースにする。
                var instanceKey = instance->Id.InstanceKey;
                var isActive = instance->IsActive;

                string primaryPath;
                var cptr = instance->GetPrimaryPath();
                primaryPath = cptr.HasValue ? cptr.ToString() : string.Empty;
                var hasPrimaryPath = primaryPath.Length > 0;

                var isPrimaryLoaded = instance->IsPrimaryLoaded();
                var hasGraphicsObject = instance->GetGraphics() != null;
                var pathHash = TitleBackgroundCharaSelectVfxInventoryLogic.HashPath(primaryPath);

                _charaSelectVfxInventory.RecordInstance(
                    isActive,
                    hasPrimaryPath,
                    isPrimaryLoaded,
                    hasGraphicsObject);

                _charaSelectVfxInventory.RecordDetail(new TitleBackgroundVfxDetailEntry(
                    key,
                    instanceKey,
                    subId,
                    TitleBackgroundCharaSelectVfxInventoryLogic.DeriveTitleEditUuid(instanceKey, subId),
                    isActive,
                    isPrimaryLoaded,
                    hasGraphicsObject,
                    pathHash,
                    primaryPath));

                _charaSelectVfxInventory.OfferRepresentative(
                    TitleBackgroundCharaSelectVfxInventoryLogic.FormatRepresentative(
                        key,
                        subId,
                        isActive,
                        isPrimaryLoaded,
                        hasGraphicsObject,
                        pathHash,
                        primaryPath,
                        TitleBackgroundCharaSelectVfxInventoryRuntimeState.RepresentativePathMaxLength),
                    hasPrimaryPath,
                    isActive);
            }
            catch (Exception ex)
            {
                _charaSelectVfxInventory.RecordReadFailure($"instance:{ex.GetType().Name}");
            }
        }
    }

    // OneClick 完了処理（成功/失敗どちらも）で、診断レポート行を組み立てる直前に 1 回呼ぶ。
    // 走査で既に安全に読めている最新スナップショット（最大 ~248 件）を、既存の QuickCheck detail /
    // bulk diag と同じ保存パターンで詳細診断ファイルへ書き出す。clipboard レポートは compact のまま。
    // VFX write は行わない。arm されていない run（hook 未準備など）ではファイルへ触れない。
    private void SaveFruVfxInventoryDetailFile()
    {
        if (_charaSelectVfxInventory.DetailStatus is "not-run")
        {
            return;
        }

        try
        {
            var candidate = ResolveCurrentOverrideCandidate();
            var lines = _charaSelectVfxInventory.BuildDetailFileLines(candidate.Id);
            Directory.CreateDirectory(_configDirectory);
            var path = Path.Combine(
                _configDirectory,
                TitleBackgroundCharaSelectVfxInventoryRuntimeState.DetailFileName);
            File.WriteAllLines(path, lines);
            _charaSelectVfxInventory.MarkDetailWritten(true);
        }
        catch (Exception ex)
        {
            _charaSelectVfxInventory.MarkDetailWritten(false);
            _log.Warning(ex, "[XMU BG] Failed to write FRU VFX inventory detail file.");
        }
    }
}
