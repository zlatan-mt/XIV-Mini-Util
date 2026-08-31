// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.StaticAnchor.cs
// Description: FRU クリア後ステージ candidate の user-approved static anchor authorization。
//              pre-login の CharaSelect フレームで、n4gw scene override と loaded ActiveLayout の
//              identity が期待どおりのときだけ anchor を認可する（read-only）。
// Reason: n4gw に character 配置用の layout marker が無く、FRU territory へも入れないため、
//         Elpis の same-terrain capture が使えない。座標 (100,0,100) は task で明示承認された
//         static anchor（source-backed marker ではない）。native pointer は保持しない。
//         login 後は no-op にして最後の pre-login 評価を凍結する（post-login read/write leak を作らない）。
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    private readonly TitleBackgroundCharaSelectStaticAnchorRuntimeState _charaSelectStaticAnchor = new();

    // MaintainTitleEditInformedCharaSelectPlacement から毎フレーム呼ぶ。anchor を持たない候補、
    // login 済み、非 CharaSelect のときは何もしない（Snapshot は Empty か直前の pre-login 値のまま）。
    private void EvaluateFruStaticAnchorAuthorization(int runtimeSceneGeneration, bool isCharaSelectMap)
    {
        var candidate = ResolveCurrentOverrideCandidate();
        if (!candidate.ApprovedStaticAnchor.HasValue)
        {
            return;
        }

        if (_clientState.IsLoggedIn || !isCharaSelectMap)
        {
            return;
        }

        var activeLayoutAvailable = false;
        var layoutInitState = -1;
        var loadedLayoutTerritoryTypeId = 0u;
        var loadedLayerFilterKey = 0u;

        try
        {
            var layoutWorld = LayoutWorld.Instance();
            var activeLayout = layoutWorld == null ? null : layoutWorld->ActiveLayout;
            if (activeLayout != null)
            {
                activeLayoutAvailable = true;
                layoutInitState = (int)activeLayout->InitState;
                loadedLayoutTerritoryTypeId = activeLayout->TerritoryTypeId;
                loadedLayerFilterKey = activeLayout->LayerFilterKey;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] FRU static anchor: active layout read failed.");
        }

        var sceneOverrideApplied = _lastOverrideApplied
            && _lastOverrideLobbyType == GameLobbyType.CharaSelect;

        var snapshot = TitleBackgroundCharaSelectStaticAnchorLogic.Evaluate(
            candidate,
            preLogin: true,
            charaSelectMap: isCharaSelectMap,
            sceneOverrideApplied: sceneOverrideApplied,
            // 実際に last-applied された値だけを渡す（desired 状態の _validatedTerritoryPath ではない）。
            appliedScenePath: _lastOverrideApplied ? _lastOverrideNewPath : null,
            appliedTerritoryTypeId: _lastOverrideTerritoryId,
            appliedLayerFilterKey: _lastOverrideLayerFilterKey,
            activeLayoutAvailable: activeLayoutAvailable,
            layoutInitState: layoutInitState,
            loadedLayoutTerritoryTypeId: loadedLayoutTerritoryTypeId,
            loadedLayerFilterKey: loadedLayerFilterKey,
            sceneGeneration: runtimeSceneGeneration);
        _charaSelectStaticAnchor.Capture(snapshot);
    }
}
