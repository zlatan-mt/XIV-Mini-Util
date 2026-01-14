// Path: projects/XIV-Mini-Util/Windows/Components/HomeTab.cs
// Description: ホームタブ（マテリア精製/分解）のUIを描画する
// Reason: MainWindowの責務を分割し可読性を高めるため
using Dalamud.Bindings.ImGui;
using System.Numerics;
using System.Threading.Tasks;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiTableFlags = Dalamud.Bindings.ImGui.ImGuiTableFlags;
using ImGuiTreeNodeFlags = Dalamud.Bindings.ImGui.ImGuiTreeNodeFlags;
using ImGuiWindowFlags = Dalamud.Bindings.ImGui.ImGuiWindowFlags;
using XivMiniUtil.Models.Common;
using XivMiniUtil.Models.Desynth;
using XivMiniUtil.Services.Common;
using XivMiniUtil.Services.Desynth;
using XivMiniUtil.Services.Materia;

namespace XivMiniUtil.Windows.Components;

public sealed class HomeTab : ITabComponent
{
    private const int PreviewLimit = 10;
    private static readonly Vector4 WarningColor = new(1f, 0.65f, 0.2f, 1f);

    private readonly Configuration _configuration;
    private readonly MateriaExtractService _materiaService;
    private readonly DesynthService _desynthService;
    private readonly InventoryCacheService _inventoryCacheService;
    private readonly bool _materiaFeatureEnabled;
    private readonly bool _desynthFeatureEnabled;

    private string? _lastResultMessage;
    private bool _isVisible;
    private bool _refreshRequested = true;
    private InventoryPreviewSnapshot _snapshot = InventoryPreviewSnapshot.Empty;

    private bool _openDesynthConfirm;
    private DesynthConfirmData? _desynthConfirmData;

    public HomeTab(
        Configuration configuration,
        MateriaExtractService materiaService,
        DesynthService desynthService,
        InventoryCacheService inventoryCacheService,
        bool materiaFeatureEnabled,
        bool desynthFeatureEnabled)
    {
        _configuration = configuration;
        _materiaService = materiaService;
        _desynthService = desynthService;
        _inventoryCacheService = inventoryCacheService;
        _materiaFeatureEnabled = materiaFeatureEnabled;
        _desynthFeatureEnabled = desynthFeatureEnabled;
    }

    public string? LastResultMessage => _lastResultMessage;

    public void SetVisible(bool isVisible)
    {
        if (isVisible && !_isVisible)
        {
            _refreshRequested = true;
        }

        _isVisible = isVisible;
    }

    public void Draw()
    {
        RefreshSnapshotIfNeeded();

        DrawMateriaSection();
        ImGui.Spacing();
        DrawDesynthSection();
        DrawDesynthConfirmDialog();
    }

    public void Dispose()
    {
    }

    private void RefreshSnapshotIfNeeded(bool force = false)
    {
        var request = BuildDesynthPreviewRequest();
        _snapshot = _inventoryCacheService.GetSnapshot(request, force || _refreshRequested);
        _refreshRequested = false;
    }

    private void DrawMateriaSection()
    {
        if (!ImGui.CollapsingHeader("マテリア精製", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        var disabled = !_materiaFeatureEnabled;
        if (disabled)
        {
            ImGui.Text("現在は無効中です。");
            ImGui.BeginDisabled();
        }

        if (ImGui.BeginTable("MateriaSummary", 2, ImGuiTableFlags.SizingFixedFit))
        {
            DrawSummaryRow("状態", GetMateriaStatusText());
            DrawSummaryRow("対象件数", GetMateriaCountText());
            ImGui.EndTable();
        }

        var enabled = _materiaService.IsEnabled;
        if (ImGui.Checkbox("自動精製を有効化", ref enabled) && _materiaFeatureEnabled)
        {
            if (enabled)
            {
                _materiaService.Enable();
            }
            else
            {
                _materiaService.Disable();
            }
        }

        if (disabled)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawDesynthSection()
    {
        if (!ImGui.CollapsingHeader("アイテム分解", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        var disabled = !_desynthFeatureEnabled;
        if (disabled)
        {
            ImGui.Text("現在は無効中です。");
            ImGui.BeginDisabled();
        }

        if (ImGui.BeginTable("DesynthSummary", 2, ImGuiTableFlags.SizingFixedFit))
        {
            DrawSummaryRow("状態", GetDesynthStatusText());
            DrawSummaryRow("対象件数", GetDesynthCountText());
            DrawSummaryRow("レベル範囲", $"{_configuration.DesynthMinLevel} - {_configuration.DesynthMaxLevel}");
            DrawSummaryRow("分解モード", GetTargetModeText());
            DrawSummaryRow("スコープ", "所持品のみ");
            ImGui.EndTable();
        }

        var isProcessing = _desynthService.IsProcessing;
        var canStart = _desynthFeatureEnabled
            && !isProcessing
            && _snapshot.IsLoggedIn
            && _snapshot.EffectiveDesynthQuantity > 0;

        if (!canStart)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("分解開始"))
        {
            if (TryPrepareDesynthConfirm())
            {
                _openDesynthConfirm = true;
            }
        }

        if (!canStart)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        if (!isProcessing)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("分解停止"))
        {
            _desynthService.Stop();
        }

        if (!isProcessing)
        {
            ImGui.EndDisabled();
        }

        if (disabled)
        {
            ImGui.EndDisabled();
        }

        if (_snapshot.IsLoggedIn && _snapshot.DesynthableQuantity == 0)
        {
            ImGui.TextDisabled("対象がありません。");
        }
    }

    private void DrawDesynthConfirmDialog()
    {
        if (_openDesynthConfirm)
        {
            ImGui.OpenPopup("分解確認");
            _openDesynthConfirm = false;
        }

        if (_desynthConfirmData == null)
        {
            return;
        }

        var dialogOpen = true;
        if (!ImGui.BeginPopupModal("分解確認", ref dialogOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        var data = _desynthConfirmData;
        ImGui.Text("分解を開始しますか？");
        ImGui.Separator();
        ImGui.Text($"対象件数: {data.EffectiveDesynthQuantity}件");
        ImGui.Text($"レベル範囲: {data.Request.MinLevel} - {data.Request.MaxLevel}");
        ImGui.Text("スコープ: 所持品のみ");

        if (data.Request.TargetMode == DesynthTargetMode.Count)
        {
            ImGui.Text($"上限: {data.Request.NormalizedTargetCount}件 / 候補: {data.DesynthableQuantity}件");
        }

        if (data.HighLevelThreshold >= 0)
        {
            ImGui.Text($"高IL対象: {data.HighLevelItemCount}件 (基準 IL{data.HighLevelThreshold}+)");
        }

        ImGui.Spacing();
        ImGui.Text("上位10件 (IL順)");
        foreach (var item in data.PreviewItems)
        {
            DrawPreviewItem(item, data.HighLevelThreshold >= 0 && item.ItemLevel >= data.HighLevelThreshold);
        }

        if (data.OtherItemCount > 0)
        {
            ImGui.Text($"他 {data.OtherItemCount} 件");
        }

        ImGui.Separator();
        if (ImGui.Button("実行"))
        {
            _ = StartDesynthAsync();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("キャンセル"))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawPreviewItem(InventoryItemInfo item, bool highlight)
    {
        var text = $"{item.Name} (IL {item.ItemLevel}) x{item.Quantity}";
        if (highlight)
        {
            ImGui.TextColored(WarningColor, text);
        }
        else
        {
            ImGui.Text(text);
        }
    }

    private string GetMateriaStatusText()
    {
        if (!_materiaFeatureEnabled)
        {
            return "無効中";
        }

        if (!_snapshot.IsLoggedIn)
        {
            return "ログインしていません";
        }

        return _materiaService.IsProcessing ? "処理中" : "待機中";
    }

    private string GetMateriaCountText()
    {
        if (!_snapshot.IsLoggedIn)
        {
            return "-";
        }

        return $"{_snapshot.ExtractableQuantity}件";
    }

    private string GetDesynthStatusText()
    {
        if (!_desynthFeatureEnabled)
        {
            return "無効中";
        }

        if (!_snapshot.IsLoggedIn)
        {
            return "ログインしていません";
        }

        return _desynthService.IsProcessing ? "処理中" : "待機中";
    }

    private string GetDesynthCountText()
    {
        if (!_snapshot.IsLoggedIn)
        {
            return "-";
        }

        return $"{_snapshot.EffectiveDesynthQuantity}件";
    }

    private string GetTargetModeText()
    {
        return _configuration.DesynthTargetMode switch
        {
            DesynthTargetMode.All => "すべて分解",
            DesynthTargetMode.Count => $"個数指定 ({_configuration.DesynthTargetCount}件)",
            _ => "未設定",
        };
    }

    private static void DrawSummaryRow(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text(label);
        ImGui.TableSetColumnIndex(1);
        ImGui.Text(value);
    }

    private DesynthPreviewRequest BuildDesynthPreviewRequest()
    {
        return new DesynthPreviewRequest(
            _configuration.DesynthMinLevel,
            _configuration.DesynthMaxLevel,
            _configuration.DesynthTargetMode,
            _configuration.DesynthTargetCount);
    }

    private bool TryPrepareDesynthConfirm()
    {
        RefreshSnapshotIfNeeded(force: true);

        if (!_snapshot.IsLoggedIn)
        {
            return false;
        }

        if (_snapshot.EffectiveDesynthQuantity <= 0)
        {
            return false;
        }

        var ordered = _snapshot.DesynthableItems
            .OrderByDescending(item => item.ItemLevel)
            .ThenBy(item => item.Container)
            .ThenBy(item => item.Slot)
            .ToList();

        var previewItems = ordered.Take(PreviewLimit).ToList();
        var otherCount = Math.Max(0, ordered.Count - previewItems.Count);

        var threshold = -1;
        var highLevelCount = 0;
        if (_configuration.DesynthWarningEnabled)
        {
            threshold = Math.Max(0, _snapshot.MaxItemLevel - _configuration.DesynthWarningThreshold);
            highLevelCount = ordered.Count(item => item.ItemLevel >= threshold);
        }

        _desynthConfirmData = new DesynthConfirmData(
            _snapshot.Request,
            _snapshot.EffectiveDesynthQuantity,
            _snapshot.DesynthableQuantity,
            previewItems,
            otherCount,
            threshold,
            highLevelCount);
        return true;
    }

    private async Task StartDesynthAsync()
    {
        var options = new DesynthOptions(
            _configuration.DesynthMinLevel,
            _configuration.DesynthMaxLevel,
            _configuration.DesynthTargetMode,
            _configuration.DesynthTargetCount);

        var result = await _desynthService.StartDesynthAsync(options);
        _lastResultMessage = $"分解結果: 成功 {result.ProcessedCount} / スキップ {result.SkippedCount}";
        if (result.Errors.Count > 0)
        {
            _lastResultMessage += $" / エラー {result.Errors.Count}";
        }

        _inventoryCacheService.MarkDirty();
        _refreshRequested = true;
    }

    private sealed record DesynthConfirmData(
        DesynthPreviewRequest Request,
        int EffectiveDesynthQuantity,
        int DesynthableQuantity,
        IReadOnlyList<InventoryItemInfo> PreviewItems,
        int OtherItemCount,
        int HighLevelThreshold,
        int HighLevelItemCount);
}
