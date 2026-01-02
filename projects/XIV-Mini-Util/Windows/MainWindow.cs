// Path: projects/XIV-Mini-Util/Windows/MainWindow.cs
// Description: メイン操作UIを提供しサービスの状態を制御する
// Reason: ユーザーがゲーム内で機能を操作できるようにするため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/MateriaExtractService.cs, projects/XIV-Mini-Util/Services/DesynthService.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiTabItemFlags = Dalamud.Bindings.ImGui.ImGuiTabItemFlags;
using ImGuiTableColumnFlags = Dalamud.Bindings.ImGui.ImGuiTableColumnFlags;
using ImGuiTableFlags = Dalamud.Bindings.ImGui.ImGuiTableFlags;
using ImGuiWindowFlags = Dalamud.Bindings.ImGui.ImGuiWindowFlags;
using XivMiniUtil;
using XivMiniUtil.Services;

namespace XivMiniUtil.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Configuration _configuration;
    private readonly MateriaExtractService _materiaService;
    private readonly DesynthService _desynthService;
    private readonly ShopDataCache _shopDataCache;
    private readonly ShopSearchService _shopSearchService;
    private readonly bool _materiaFeatureEnabled;
    private readonly bool _desynthFeatureEnabled;

    private DesynthWarningInfo? _warningInfo;
    private bool _showWarningDialog;
    private string? _lastResultMessage;
    private bool _selectSettingsTab;
    private int _settingsCategoryIndex;
    private string _shopAreaFilter = string.Empty;
    private string _importBase64 = string.Empty;
    private bool _showImportConfirm;
    private Configuration? _pendingImportConfig;
    private string? _configIoMessage;
    private Vector4 _configIoMessageColor = new(0.9f, 0.9f, 0.9f, 1f);

    // Search Tab State
    private string _searchQuery = string.Empty;
    private List<(uint Id, string Name)> _searchResults = new();
    private bool _isSearching;
    private string? _searchStatusMessage;
    private CancellationTokenSource? _searchCts;
    private readonly object _searchLock = new();

    public MainWindow(
        Configuration configuration,
        MateriaExtractService materiaService,
        DesynthService desynthService,
        ShopDataCache shopDataCache,
        ShopSearchService shopSearchService,
        bool materiaFeatureEnabled,
        bool desynthFeatureEnabled)
        : base("XIV Mini Util")
    {
        _configuration = configuration;
        _materiaService = materiaService;
        _desynthService = desynthService;
        _shopDataCache = shopDataCache;
        _shopSearchService = shopSearchService;
        _materiaFeatureEnabled = materiaFeatureEnabled;
        _desynthFeatureEnabled = desynthFeatureEnabled;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 360),
            MaximumSize = new Vector2(900, 700),
        };

        _desynthService.OnWarningRequired += ShowWarningDialog;
    }

    public new void Toggle()
    {
        IsOpen = !IsOpen;
    }

    public void OpenSettingsTab()
    {
        _selectSettingsTab = true;
        IsOpen = true;
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("MainTabs"))
        {
            if (ImGui.BeginTabItem("Home"))
            {
                DrawHomeTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Search"))
            {
                DrawSearchTab();
                ImGui.EndTabItem();
            }

            var settingsFlags = _selectSettingsTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem("Settings", settingsFlags))
            {
                _selectSettingsTab = false;
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        DrawWarningDialog();
        DrawResult();
    }

    public void Dispose()
    {
        _desynthService.OnWarningRequired -= ShowWarningDialog;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }

    public void ShowWarningDialog(DesynthWarningInfo info)
    {
        _warningInfo = info;
        _showWarningDialog = true;
    }

    private void DrawMateriaSection()
    {
        ImGui.Text("マテリア精製");
        if (!_materiaFeatureEnabled)
        {
            ImGui.Text("現在は無効中です。");
            ImGui.BeginDisabled();
        }

        var enabled = _materiaService.IsEnabled;
        if (ImGui.Checkbox("有効", ref enabled) && _materiaFeatureEnabled)
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

        ImGui.SameLine();
        ImGui.Text(_materiaFeatureEnabled
            ? (_materiaService.IsProcessing ? "処理中" : "待機中")
            : "無効中");

        if (!_materiaFeatureEnabled)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawDesynthActionSection()
    {
        ImGui.Text("アイテム分解");
        if (!_desynthFeatureEnabled)
        {
            ImGui.Text("現在は無効中です。");
            ImGui.BeginDisabled();
        }

        var isProcessing = _desynthService.IsProcessing;

        if (isProcessing)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("分解開始"))
        {
            if (_desynthFeatureEnabled)
            {
                _ = StartDesynthAsync();
            }
        }

        if (isProcessing)
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
            if (_desynthFeatureEnabled)
            {
                _desynthService.Stop();
            }
        }

        if (!isProcessing)
        {
            ImGui.EndDisabled();
        }

        if (!_desynthFeatureEnabled)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawHomeTab()
    {
        DrawMateriaSection();
        ImGui.Separator();
        DrawDesynthActionSection();
    }

    private void DrawSettingsTab()
    {
        if (ImGui.BeginTable("SettingsLayout", 2, ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 180f);
            ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            DrawSettingsCategoryList();

            ImGui.TableSetColumnIndex(1);
            DrawSettingsCategoryContent();

            ImGui.EndTable();
        }

        ImGui.Separator();
        DrawConfigIoSection();
    }

    private void DrawSettingsCategoryList()
    {
        var categories = new[]
        {
            _materiaFeatureEnabled ? "General & Materia" : "General & Materia (無効中)",
            _desynthFeatureEnabled ? "Desynthesis" : "Desynthesis (無効中)",
            "Shop Search",
        };

        if (ImGui.BeginChild("SettingsCategories", new Vector2(0, 0), true))
        {
            for (var i = 0; i < categories.Length; i++)
            {
                if (ImGui.Selectable(categories[i], _settingsCategoryIndex == i))
                {
                    _settingsCategoryIndex = i;
                }
            }
        }

        ImGui.EndChild();
    }

    private void DrawSettingsCategoryContent()
    {
        ImGui.BeginChild("SettingsContent", new Vector2(0, 0), false);

        switch (_settingsCategoryIndex)
        {
            case 0:
                DrawGeneralSettings();
                break;
            case 1:
                DrawDesynthSettings();
                break;
            case 2:
                DrawShopSearchSettings();
                break;
            default:
                DrawGeneralSettings();
                break;
        }

        ImGui.EndChild();
    }

    private void DrawGeneralSettings()
    {
        ImGui.Text("一般設定");
        ImGui.Separator();
        ImGui.Text("マテリア精製");
        if (!_materiaFeatureEnabled)
        {
            ImGui.Text("現在は無効中です。");
            ImGui.BeginDisabled();
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

        ImGui.Text(_materiaFeatureEnabled
            ? (_materiaService.IsProcessing ? "状態: 処理中" : "状態: 待機中")
            : "状態: 無効中");

        if (!_materiaFeatureEnabled)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawDesynthSettings()
    {
        ImGui.Text("分解設定");
        ImGui.Separator();
        if (!_desynthFeatureEnabled)
        {
            ImGui.Text("現在は無効中です。");
            ImGui.BeginDisabled();
        }

        var minLevel = _configuration.DesynthMinLevel;
        var maxLevel = _configuration.DesynthMaxLevel;
        if (ImGui.InputInt("最小レベル", ref minLevel))
        {
            _configuration.DesynthMinLevel = Math.Clamp(minLevel, 1, 999);
            _configuration.Save();
        }

        if (ImGui.InputInt("最大レベル", ref maxLevel))
        {
            _configuration.DesynthMaxLevel = Math.Clamp(maxLevel, 1, 999);
            _configuration.Save();
        }

        var jobCondition = _configuration.DesynthJobCondition;
        if (ImGui.BeginCombo("ジョブ条件", jobCondition.ToString()))
        {
            foreach (JobCondition condition in Enum.GetValues(typeof(JobCondition)))
            {
                var selected = condition == jobCondition;
                if (ImGui.Selectable(condition.ToString(), selected))
                {
                    _configuration.DesynthJobCondition = condition;
                    _configuration.Save();
                }
            }
            ImGui.EndCombo();
        }

        var targetMode = _configuration.DesynthTargetMode;
        if (ImGui.BeginCombo("分解対象", GetTargetModeLabel(targetMode)))
        {
            foreach (DesynthTargetMode mode in Enum.GetValues(typeof(DesynthTargetMode)))
            {
                var selected = mode == targetMode;
                if (ImGui.Selectable(GetTargetModeLabel(mode), selected))
                {
                    _configuration.DesynthTargetMode = mode;
                    _configuration.Save();
                }
            }
            ImGui.EndCombo();
        }

        if (_configuration.DesynthTargetMode == DesynthTargetMode.Count)
        {
            var targetCount = _configuration.DesynthTargetCount;
            if (ImGui.InputInt("分解する個数", ref targetCount))
            {
                _configuration.DesynthTargetCount = Math.Clamp(targetCount, 1, 999);
                _configuration.Save();
            }
        }

        var warningEnabled = _configuration.DesynthWarningEnabled;
        if (ImGui.Checkbox("高レベル警告を有効", ref warningEnabled))
        {
            _configuration.DesynthWarningEnabled = warningEnabled;
            _configuration.Save();
        }

        var warningThreshold = _configuration.DesynthWarningThreshold;
        if (ImGui.InputInt("警告しきい値", ref warningThreshold))
        {
            _configuration.DesynthWarningThreshold = Math.Clamp(warningThreshold, 1, 999);
            _configuration.Save();
        }

        if (!_desynthFeatureEnabled)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawShopSearchSettings()
    {
        ImGui.Text("販売場所検索");
        ImGui.Separator();

        var cacheReady = _shopDataCache.IsInitialized;
        if (!cacheReady)
        {
            ImGui.Text("ショップデータを準備中です。");
        }

        var echoEnabled = _configuration.ShopSearchEchoEnabled;
        if (ImGui.Checkbox("チャットに検索結果を表示", ref echoEnabled))
        {
            _configuration.ShopSearchEchoEnabled = echoEnabled;
            _configuration.Save();
        }

        var windowEnabled = _configuration.ShopSearchWindowEnabled;
        if (ImGui.Checkbox("検索結果ウィンドウを表示（4件以上）", ref windowEnabled))
        {
            _configuration.ShopSearchWindowEnabled = windowEnabled;
            _configuration.Save();
        }

        var autoTeleportEnabled = _configuration.ShopSearchAutoTeleportEnabled;
        if (ImGui.Checkbox("検索時/マップピン時に自動テレポ", ref autoTeleportEnabled))
        {
            _configuration.ShopSearchAutoTeleportEnabled = autoTeleportEnabled;
            _configuration.Save();
        }

        ImGui.Separator();
        DrawShopSearchPriorityList();
        ImGui.Separator();
        if (!cacheReady)
        {
            ImGui.BeginDisabled();
        }

        DrawShopSearchAddArea();

        if (!cacheReady)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawShopSearchPriorityList()
    {
        ImGui.Text("エリア優先度");

        var priorities = _configuration.ShopSearchAreaPriority;
        if (priorities.Count == 0)
        {
            ImGui.Text("優先度リストが空です。");
        }

        if (ImGui.BeginTable("ShopPriorityTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("エリア");
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 140f);
            ImGui.TableHeadersRow();

            for (var i = 0; i < priorities.Count; i++)
            {
                var territoryId = priorities[i];
                var territoryName = _shopDataCache.GetTerritoryName(territoryId);
                var label = string.IsNullOrWhiteSpace(territoryName)
                    ? $"不明 (ID:{territoryId})"
                    : territoryName;

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text(label);

                ImGui.TableSetColumnIndex(1);
                ImGui.PushID(i);

                if (ImGui.Button("↑") && i > 0)
                {
                    (priorities[i - 1], priorities[i]) = (priorities[i], priorities[i - 1]);
                    _configuration.Save();
                }

                ImGui.SameLine();
                if (ImGui.Button("↓") && i < priorities.Count - 1)
                {
                    (priorities[i + 1], priorities[i]) = (priorities[i], priorities[i + 1]);
                    _configuration.Save();
                }

                ImGui.SameLine();
                if (ImGui.Button("削除"))
                {
                    priorities.RemoveAt(i);
                    _configuration.Save();
                    ImGui.PopID();
                    break;
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        if (ImGui.Button("優先度をリセット"))
        {
            _configuration.ResetShopSearchAreaPriority();
        }
    }

    private void DrawShopSearchAddArea()
    {
        ImGui.Text("エリア追加");
        ImGui.InputTextWithHint("##ShopAreaFilter", "エリア名で検索", ref _shopAreaFilter, 64);

        var available = _shopDataCache.GetAllShopTerritories();
        var priorities = _configuration.ShopSearchAreaPriority;
        var filtered = available
            .Where(info => !priorities.Contains(info.TerritoryTypeId))
            .Where(info => string.IsNullOrWhiteSpace(_shopAreaFilter)
                || info.TerritoryName.Contains(_shopAreaFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (ImGui.BeginCombo("エリア追加", "追加するエリアを選択"))
        {
            if (filtered.Count == 0)
            {
                ImGui.Text("候補がありません。");
            }

            foreach (var info in filtered)
            {
                if (ImGui.Selectable(info.TerritoryName))
                {
                    priorities.Add(info.TerritoryTypeId);
                    _configuration.Save();
                    _shopAreaFilter = string.Empty;
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawConfigIoSection()
    {
        ImGui.Text("設定のバックアップ");
        ImGui.Separator();

        if (ImGui.Button("エクスポート (クリップボード)"))
        {
            var exportText = _configuration.ExportToBase64();
            ImGui.SetClipboardText(exportText);
            SetConfigIoMessage("エクスポートしました。", false);
        }

        ImGui.SameLine();
        if (ImGui.Button("クリップボードから読み込み"))
        {
            _importBase64 = ImGui.GetClipboardText() ?? string.Empty;
        }

        ImGui.InputTextMultiline("##ImportBase64", ref _importBase64, 4096, new Vector2(-1, 80));

        if (ImGui.Button("インポート"))
        {
            if (_configuration.TryParseImport(_importBase64, out var imported, out var error))
            {
                _pendingImportConfig = imported;
                _showImportConfirm = true;
            }
            else
            {
                SetConfigIoMessage(error, true);
            }
        }

        DrawImportConfirmDialog();

        if (!string.IsNullOrWhiteSpace(_configIoMessage))
        {
            ImGui.TextColored(_configIoMessageColor, _configIoMessage);
        }
    }

    private void DrawImportConfirmDialog()
    {
        if (_showImportConfirm)
        {
            ImGui.OpenPopup("設定インポート確認");
            _showImportConfirm = false;
        }

        var dialogOpen = true;
        if (ImGui.BeginPopupModal("設定インポート確認", ref dialogOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("現在の設定を上書きします。");
            ImGui.Text("よろしいですか？");
            ImGui.Separator();

            if (ImGui.Button("インポートする"))
            {
                if (_pendingImportConfig != null)
                {
                    _configuration.ApplyFrom(_pendingImportConfig);
                    _configuration.Save();
                    SetConfigIoMessage("インポートしました。", false);
                }

                _pendingImportConfig = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("キャンセル"))
            {
                _pendingImportConfig = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void SetConfigIoMessage(string message, bool isError)
    {
        _configIoMessage = message;
        _configIoMessageColor = isError
            ? new Vector4(0.9f, 0.3f, 0.3f, 1f)
            : new Vector4(0.3f, 0.7f, 0.4f, 1f);
    }

    private void DrawWarningDialog()
    {
        if (_showWarningDialog)
        {
            ImGui.OpenPopup("分解警告");
            _showWarningDialog = false;
        }

        var dialogOpen = true;
        if (ImGui.BeginPopupModal("分解警告", ref dialogOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (_warningInfo != null)
            {
                ImGui.Text("高レベルアイテムの分解を検出しました。");
                ImGui.Text($"アイテム: {_warningInfo.ItemName}");
                ImGui.Text($"レベル: {_warningInfo.ItemLevel} / 最高: {_warningInfo.MaxItemLevel}");
            }

            ImGui.Separator();
            if (ImGui.Button("はい"))
            {
                _desynthService.ConfirmWarning(true);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("いいえ"))
            {
                _desynthService.ConfirmWarning(false);
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawResult()
    {
        if (!string.IsNullOrWhiteSpace(_lastResultMessage))
        {
            ImGui.Separator();
            ImGui.TextWrapped(_lastResultMessage);
        }
    }

    private async Task StartDesynthAsync()
    {
        var options = new DesynthOptions(
            _configuration.DesynthMinLevel,
            _configuration.DesynthMaxLevel,
            !_configuration.DesynthWarningEnabled,
            _configuration.DesynthTargetMode,
            _configuration.DesynthTargetCount);

        var result = await _desynthService.StartDesynthAsync(options);
        _lastResultMessage = $"分解結果: 成功 {result.ProcessedCount} / スキップ {result.SkippedCount}";
        if (result.Errors.Count > 0)
        {
            _lastResultMessage += $" / エラー {result.Errors.Count}";
        }
    }

    private static string GetTargetModeLabel(DesynthTargetMode mode)
    {
        return mode switch
        {
            DesynthTargetMode.All => "すべて分解",
            DesynthTargetMode.Count => "個数を指定して分解",
            _ => mode.ToString(),
        };
    }

    private void DrawSearchTab()
    {
        ImGui.Text("アイテム名で販売場所を検索");
        ImGui.Separator();

        if (!_shopDataCache.IsInitialized)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "ショップデータを準備中です...");
            return;
        }

        var enterPressed = ImGui.InputTextWithHint("##ItemNameSearch", "アイテム名を入力...", ref _searchQuery, 100, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();

        if ((ImGui.Button("検索") || enterPressed) && !string.IsNullOrWhiteSpace(_searchQuery))
        {
            _ = ExecuteSearchAsync(_searchQuery);
        }

        if (_isSearching)
        {
            ImGui.Text("検索中...");
        }
        else if (_searchStatusMessage != null)
        {
            ImGui.TextWrapped(_searchStatusMessage);
        }

        ImGui.Separator();

        ImGui.BeginChild("SearchResults", new Vector2(0, -1), true);

        lock (_searchLock)
        {
            if (_searchResults.Count > 0)
            {
                ImGui.Text($"検索結果: {_searchResults.Count}件");
                foreach (var (id, name) in _searchResults)
                {
                    if (ImGui.Selectable($"{name}##{id}"))
                    {
                        _shopSearchService.Search(id);
                    }
                }
            }
            else if (!_isSearching && !string.IsNullOrEmpty(_searchStatusMessage) && _searchStatusMessage.Contains("0件"))
            {
                ImGui.TextDisabled("該当なし");
            }
        }

        ImGui.EndChild();
    }

    private async Task ExecuteSearchAsync(string query)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _isSearching = true;
        _searchStatusMessage = null;

        try
        {
            // UIスレッドをブロックしないようにバックグラウンドで実行
            var results = await Task.Run(() => _shopDataCache.SearchItemsByName(query, 50).ToList(), token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            lock (_searchLock)
            {
                _searchResults = results;
                _searchStatusMessage = results.Count == 0 ? "該当するアイテムがありません（0件）" : null;
            }
        }
        catch (Exception ex)
        {
            if (ex is not TaskCanceledException)
            {
                _searchStatusMessage = $"検索エラー: {ex.Message}";
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _isSearching = false;
            }
        }
    }
}
