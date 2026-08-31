// Path: projects/XIV-Mini-Util/Windows/Components/HomeTab.cs
// Description: ホームタブ（マテリア精製/装着マテリア回収/一括確認/分解）のUIを描画する
// Reason: MainWindowの責務を分割し可読性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/MateriaExtractService.cs, projects/XIV-Mini-Util/Services/Materia/MateriaRetrievalService.cs, projects/XIV-Mini-Util/Models/Materia/MateriaRetrievalQueueItem.cs
using Dalamud.Bindings.ImGui;
using System.Numerics;
using System.Threading.Tasks;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiTableFlags = Dalamud.Bindings.ImGui.ImGuiTableFlags;
using ImGuiTreeNodeFlags = Dalamud.Bindings.ImGui.ImGuiTreeNodeFlags;
using ImGuiWindowFlags = Dalamud.Bindings.ImGui.ImGuiWindowFlags;
using XivMiniUtil.Models.Common;
using XivMiniUtil.Models.Desynth;
using XivMiniUtil.Models.Materia;
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
    private readonly MateriaRetrievalService _materiaRetrievalService;
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
    private bool _openMateriaRetrievalBatchConfirm;
    private MateriaRetrievalBatchPreview? _materiaRetrievalBatchPreview;

    public HomeTab(
        Configuration configuration,
        MateriaExtractService materiaService,
        MateriaRetrievalService materiaRetrievalService,
        DesynthService desynthService,
        InventoryCacheService inventoryCacheService,
        bool materiaFeatureEnabled,
        bool desynthFeatureEnabled)
    {
        _configuration = configuration;
        _materiaService = materiaService;
        _materiaRetrievalService = materiaRetrievalService;
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
        DrawMateriaRetrievalSection();
        DrawMateriaRetrievalBatchConfirmDialog();
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

    private void DrawMateriaRetrievalSection()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("装着マテリア回収");

        if (ImGui.BeginTable("MateriaRetrievalSummary", 2, ImGuiTableFlags.SizingFixedFit))
        {
            var snapshot = _materiaRetrievalService.QueueSnapshot;
            DrawSummaryRow("キュー", $"残り {snapshot.RemainingCount}件（実行中 {snapshot.RunningCount} / 待機 {snapshot.WaitingCount}）");
            DrawSummaryRow("状態", _materiaRetrievalService.StatusText);
            DrawSummaryRow("実行中", snapshot.RunningItemName);
            DrawSummaryRow("待機先頭", snapshot.WaitingHeadName);
            DrawSummaryRow("成功数", $"{snapshot.SuccessCount}件");
            DrawSummaryRow("失敗数", $"{snapshot.FailureCount}件 / 中止・スキップ {snapshot.SkippedCount}件");
            if (snapshot.WaitingItemNames.Count > 0)
            {
                var waitingNames = string.Join("、", snapshot.WaitingItemNames);
                if (snapshot.AdditionalWaitingCount > 0)
                {
                    waitingNames += $"、ほか {snapshot.AdditionalWaitingCount}件";
                }

                DrawSummaryRow("待機一覧", waitingNames);
            }

            if (snapshot.FailureReasonLines.Count > 0)
            {
                var failureReasons = string.Join(" / ", snapshot.FailureReasonLines);
                if (snapshot.AdditionalFailureReasonCount > 0)
                {
                    failureReasons += $" / ほか {snapshot.AdditionalFailureReasonCount}件";
                }

                DrawSummaryRow("失敗・中止理由", failureReasons);
            }

            ImGui.EndTable();
        }

        var canStop = _materiaRetrievalService.IsProcessing;
        if (!canStop)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("回収を中止"))
        {
            _materiaRetrievalService.Stop();
        }

        if (!canStop)
        {
            ImGui.EndDisabled();
        }

        var canBatch = _materiaFeatureEnabled;
        if (!canBatch)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("手持ちの装着マテリアを一括回収"))
        {
            _materiaRetrievalBatchPreview = _materiaRetrievalService.BuildHandInventoryBatchPreview();
            if (_materiaRetrievalBatchPreview.CandidateCount > 0)
            {
                _openMateriaRetrievalBatchConfirm = true;
            }
        }

        if (!canBatch)
        {
            ImGui.EndDisabled();
        }

        if (_materiaRetrievalBatchPreview is { CandidateCount: 0 } emptyPreview)
        {
            ImGui.TextDisabled($"一括回収候補なし。{BuildBatchExclusionSummary(emptyPreview)}");
        }

        if (!string.IsNullOrWhiteSpace(_materiaRetrievalService.LastMessage))
        {
            ImGui.TextWrapped(_materiaRetrievalService.LastMessage);
        }
    }

    private void DrawMateriaRetrievalBatchConfirmDialog()
    {
        if (_openMateriaRetrievalBatchConfirm)
        {
            ImGui.OpenPopup("手持ちマテリア一括回収確認");
            _openMateriaRetrievalBatchConfirm = false;
        }

        var preview = _materiaRetrievalBatchPreview;
        if (preview == null)
        {
            return;
        }

        var dialogOpen = true;
        if (!ImGui.BeginPopupModal("手持ちマテリア一括回収確認", ref dialogOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!dialogOpen)
            {
                _materiaRetrievalBatchPreview = null;
            }

            return;
        }

        ImGui.Text("通常所持品の装着マテリアを回収キューへ追加します。");
        ImGui.Separator();
        ImGui.Text($"対象 {preview.CandidateCount}装備 / マテリア合計 {preview.TotalMateriaCount}個");
        ImGui.TextWrapped(BuildBatchExclusionSummary(preview));

        var previewItems = preview.Candidates.Take(5).ToArray();
        foreach (var candidate in previewItems)
        {
            ImGui.Text($"・{candidate.DisplayName} ({candidate.StartingMateriaCount}個)");
        }

        if (preview.CandidateCount > previewItems.Length)
        {
            ImGui.Text($"ほか {preview.CandidateCount - previewItems.Length}件");
        }

        ImGui.Separator();
        if (ImGui.Button("開始"))
        {
            _materiaRetrievalService.QueueHandInventoryBatch();
            _materiaRetrievalBatchPreview = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("キャンセル"))
        {
            _materiaRetrievalBatchPreview = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private static string BuildBatchExclusionSummary(MateriaRetrievalBatchPreview preview)
    {
        var summary = new List<string>();
        foreach (var pair in preview.RejectedByReason.OrderBy(pair => pair.Key.ToString()))
        {
            summary.Add($"{GetCandidateRejectionReasonText(pair.Key)} {pair.Value}件");
        }

        if (preview.DuplicateCount > 0)
        {
            summary.Add($"キュー重複 {preview.DuplicateCount}件");
        }

        return summary.Count == 0
            ? "除外なし"
            : $"除外: {string.Join(" / ", summary)}";
    }

    private static string GetCandidateRejectionReasonText(MateriaRetrievalCandidateRejectionReason reason)
    {
        return reason switch
        {
            MateriaRetrievalCandidateRejectionReason.FeatureDisabled => "機能無効",
            MateriaRetrievalCandidateRejectionReason.NotLoggedIn => "未ログイン",
            MateriaRetrievalCandidateRejectionReason.UnsupportedTarget => "対象外コンテキスト",
            MateriaRetrievalCandidateRejectionReason.MateriaAttachUnavailable => "MateriaAttach取得失敗",
            MateriaRetrievalCandidateRejectionReason.InventoryManagerUnavailable => "InventoryManager未取得",
            MateriaRetrievalCandidateRejectionReason.ContainerUnavailable => "コンテナ未取得",
            MateriaRetrievalCandidateRejectionReason.SlotUnavailable => "slot不正",
            MateriaRetrievalCandidateRejectionReason.TargetUnavailable => "対象消失",
            MateriaRetrievalCandidateRejectionReason.ItemIdZero => "item ID 0",
            MateriaRetrievalCandidateRejectionReason.ItemIdMismatch => "item ID不一致",
            MateriaRetrievalCandidateRejectionReason.TargetPositionMismatch => "位置不一致",
            MateriaRetrievalCandidateRejectionReason.NotEquippable => "装備対象外",
            MateriaRetrievalCandidateRejectionReason.MateriaCountZero => "マテリアなし",
            MateriaRetrievalCandidateRejectionReason.ItemSheetUnavailable => "Itemシート未取得",
            MateriaRetrievalCandidateRejectionReason.ItemSheetRowUnavailable => "Item行未取得",
            MateriaRetrievalCandidateRejectionReason.Duplicate => "重複",
            _ => reason.ToString(),
        };
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
