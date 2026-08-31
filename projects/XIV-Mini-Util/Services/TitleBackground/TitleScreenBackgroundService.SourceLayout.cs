// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.SourceLayout.cs
// Description: Elpis OneClick の live active-layout / same-terrain world source capture。
// Reason: candidate-specific source acquisition を既存の placement engine へ接続し、
//         推測した layout 値や world->scene-local 変換を production path へ流さないため。
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    // OneClick transaction が開始済みであることを呼び出し側が保証する。
    // active layout の pointer はこの呼び出し内だけで読み、managed state には値だけを保存する。
    private bool TryCaptureElpisSourceLayout(out string status)
    {
        var candidate = ResolveCurrentOverrideCandidate();
        if (!candidate.RequiresSourceBackedLayout)
        {
            status = "not-required";
            return true;
        }

        _charaSelectSourceLayout.RecordSourceCaptureAttempt();
        var currentTerritoryTypeId = _clientState.TerritoryType;
        var currentTerritoryPath = ResolveCurrentTerritoryPath(currentTerritoryTypeId);
        var layoutReady = false;
        var layoutInitState = -1;
        var layoutTerritoryTypeId = 0u;
        var layoutLayerFilterKey = 0u;
        var position = new Vector3(float.NaN, float.NaN, float.NaN);
        var captureExceptionStatus = string.Empty;

        try
        {
            if (_clientState.IsLoggedIn)
            {
                var layoutWorld = LayoutWorld.Instance();
                var activeLayout = layoutWorld == null ? null : layoutWorld->ActiveLayout;
                if (activeLayout != null)
                {
                    layoutInitState = (int)activeLayout->InitState;
                    layoutTerritoryTypeId = activeLayout->TerritoryTypeId;
                    layoutLayerFilterKey = activeLayout->LayerFilterKey;
                    layoutReady = activeLayout->InitState == 7;
                }

                var localPlayer = _objectTable.LocalPlayer;
                if (localPlayer != null)
                {
                    position = localPlayer.Position;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] Failed to read Elpis source-backed layout.");
            captureExceptionStatus = $"source-capture-exception:{ex.GetType().Name}";
        }

        var snapshot = TitleBackgroundCharaSelectSourceLayoutLogic.Evaluate(
            candidate,
            currentTerritoryTypeId,
            currentTerritoryPath,
            layoutReady,
            layoutTerritoryTypeId,
            layoutLayerFilterKey,
            position,
            selectedCandidateIsElpis: string.Equals(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(
                    _configuration.TitleBackgroundCharacterSelectOverrideCandidateId),
                    TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId,
                StringComparison.Ordinal),
            layoutInitState: layoutInitState);
        _charaSelectSourceLayout.Capture(snapshot);

        if (!snapshot.Eligible)
        {
            status = string.IsNullOrEmpty(captureExceptionStatus)
                ? snapshot.FailureReason
                : captureExceptionStatus;
            RecordTransitionEvent("Elpis source-backed layout rejected", snapshot.FailureReason);
            return false;
        }

        // この時点では recovery journal の内側。native reload 用の一時設定であり、
        // successful proof 前に known-good persistent state を置き換えるものではない。
        _configuration.TitleBackgroundLayoutTerritoryTypeId = snapshot.LayoutTerritoryTypeId;
        _configuration.TitleBackgroundLayoutLayerFilterKey = snapshot.LayoutLayerFilterKey;
        _configuration.Save();
        status = "source-backed layout captured";
        RecordTransitionEvent(
            "Elpis source-backed layout captured",
            $"territory={snapshot.LayoutTerritoryTypeId}; layer={snapshot.LayoutLayerFilterKey}");
        return true;
    }

    private string ResolveCurrentTerritoryPath(uint territoryTypeId)
    {
        if (territoryTypeId == 0)
        {
            return string.Empty;
        }

        try
        {
            var territory = _dataManager.GetExcelSheet<TerritoryType>().GetRow(territoryTypeId);
            return TitleBackgroundPathHelper.TryNormalizeAndValidateTerritoryPath(
                territory.Bg.ToString(),
                out var normalizedPath,
                out _)
                ? normalizedPath
                : string.Empty;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] Failed to resolve current territory path for Elpis source capture.");
            return string.Empty;
        }
    }
}
