// Path: projects/XIV-Mini-Util/Services/Common/InventoryCacheService.cs
// Description: ホームタブ用のインベントリ集計キャッシュ
// Reason: UI描画時の毎フレーム再計算を避けるため
using Dalamud.Plugin.Services;
using XivMiniUtil;
using XivMiniUtil.Models.Common;
using XivMiniUtil.Models.Desynth;

namespace XivMiniUtil.Services.Common;

public sealed class InventoryCacheService
{
    private readonly InventoryService _inventoryService;
    private readonly IPluginLog _pluginLog;

    private InventoryPreviewSnapshot _snapshot = InventoryPreviewSnapshot.Empty;
    private DesynthPreviewRequest? _lastRequest;
    private bool _dirty = true;

    public InventoryCacheService(InventoryService inventoryService, IPluginLog pluginLog)
    {
        _inventoryService = inventoryService;
        _pluginLog = pluginLog;
    }

    public InventoryPreviewSnapshot GetSnapshot(DesynthPreviewRequest request, bool forceRefresh = false)
    {
        if (forceRefresh || _dirty || _lastRequest == null || !_lastRequest.Equals(request))
        {
            Refresh(request);
        }

        return _snapshot;
    }

    public void MarkDirty()
    {
        _dirty = true;
    }

    private void Refresh(DesynthPreviewRequest request)
    {
        _lastRequest = request;

        if (!_inventoryService.IsPlayerLoggedIn)
        {
            _snapshot = InventoryPreviewSnapshot.Empty;
            _dirty = false;
            return;
        }

        try
        {
            var extractableItems = _inventoryService.GetExtractableItems().ToList();
            var extractableQuantity = extractableItems.Sum(item => item.Quantity);

            var desynthableItems = _inventoryService.GetDesynthableItems(request.MinLevel, request.MaxLevel).ToList();
            var desynthableQuantity = desynthableItems.Sum(item => item.Quantity);

            var targetCount = request.TargetMode == DesynthTargetMode.Count
                ? Math.Clamp(request.NormalizedTargetCount, 1, 999)
                : desynthableQuantity;
            var effectiveCount = request.TargetMode == DesynthTargetMode.Count
                ? Math.Min(desynthableQuantity, targetCount)
                : desynthableQuantity;

            _snapshot = new InventoryPreviewSnapshot(
                DateTime.UtcNow,
                true,
                extractableItems,
                extractableItems.Count,
                extractableQuantity,
                desynthableItems,
                desynthableItems.Count,
                desynthableQuantity,
                effectiveCount,
                _inventoryService.GetMaxItemLevel(),
                request);
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "インベントリ集計の更新に失敗しました。");
            _snapshot = InventoryPreviewSnapshot.Empty;
        }

        _dirty = false;
    }
}
