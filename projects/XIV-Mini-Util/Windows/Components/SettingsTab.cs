// Path: projects/XIV-Mini-Util/Windows/Components/SettingsTab.cs
// Description: 設定タブのUIと設定入出力を担当する
// Reason: MainWindowから設定UIを分離するため
using Dalamud.Bindings.ImGui;
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiInputTextFlags = Dalamud.Bindings.ImGui.ImGuiInputTextFlags;
using ImGuiTableColumnFlags = Dalamud.Bindings.ImGui.ImGuiTableColumnFlags;
using ImGuiTableFlags = Dalamud.Bindings.ImGui.ImGuiTableFlags;
using ImGuiWindowFlags = Dalamud.Bindings.ImGui.ImGuiWindowFlags;
using XivMiniUtil.Services.Desynth;
using XivMiniUtil.Services.Materia;
using XivMiniUtil.Services.Notification;
using XivMiniUtil.Services.Shop;

namespace XivMiniUtil.Windows.Components;

public sealed class SettingsTab : ITabComponent
{
    private readonly Configuration _configuration;
    private readonly MateriaExtractService _materiaService;
    private readonly DesynthService _desynthService;
    private readonly ShopDataCache _shopDataCache;
    private readonly DiscordService _discordService;
    private readonly bool _materiaFeatureEnabled;
    private readonly bool _desynthFeatureEnabled;

    private int _settingsCategoryIndex;
    private string _shopAreaFilter = string.Empty;
    private string _cachedShopAreaFilter = string.Empty;
    private int _cachedPriorityHash;
    private int _cachedTerritoryGroupsVersion = -1;
    private List<ShopTerritoryGroup> _cachedFilteredTerritories = new();
    private string _importBase64 = string.Empty;
    private bool _showImportConfirm;
    private Configuration? _pendingImportConfig;
    private string? _configIoMessage;
    private Vector4 _configIoMessageColor = new(0.9f, 0.9f, 0.9f, 1f);

    // Submarine Settings State

    public SettingsTab(
        Configuration configuration,
        MateriaExtractService materiaService,
        DesynthService desynthService,
        ShopDataCache shopDataCache,
        DiscordService discordService,
        bool materiaFeatureEnabled,
        bool desynthFeatureEnabled)
    {
        _configuration = configuration;
        _materiaService = materiaService;
        _desynthService = desynthService;
        _shopDataCache = shopDataCache;
        _discordService = discordService;
        _materiaFeatureEnabled = materiaFeatureEnabled;
        _desynthFeatureEnabled = desynthFeatureEnabled;
    }

    public void Draw()
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

    public void Dispose()
    {
    }

    private void DrawSettingsCategoryList()
    {
        var categories = new[]
        {
            _materiaFeatureEnabled ? "General & Materia" : "General & Materia (無効中)",
            _desynthFeatureEnabled ? "Desynthesis" : "Desynthesis (無効中)",
            "Shop Search",
            "Submarines",
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
            case 3:
                DrawSubmarineSettings();
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

    private void DrawSubmarineSettings()
    {
        ImGui.Text("潜水艦探索管理");
        ImGui.Separator();

        var trackerEnabled = _configuration.SubmarineTrackerEnabled;
        if (ImGui.Checkbox("機能を有効化", ref trackerEnabled))
        {
            _configuration.SubmarineTrackerEnabled = trackerEnabled;
            _configuration.Save();
        }

        if (!trackerEnabled)
        {
            ImGui.BeginDisabled();
        }

        ImGui.Spacing();
        ImGui.Text("通知設定 (Discord Webhook)");

        var notificationEnabled = _configuration.SubmarineNotificationEnabled;
        if (ImGui.Checkbox("通知を有効化", ref notificationEnabled))
        {
            _configuration.SubmarineNotificationEnabled = notificationEnabled;
            _configuration.Save();
        }

        ImGui.Spacing();

        var url = _configuration.DiscordWebhookUrl;
        if (ImGui.InputText("Webhook URL", ref url, 200, ImGuiInputTextFlags.Password))
        {
            _configuration.DiscordWebhookUrl = url;
            _configuration.Save();
        }
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "※URLは慎重に管理してください。通知にはキャラクター名が含まれます。");

        ImGui.Spacing();
        if (ImGui.Button("テスト通知を送信"))
        {
            _ = _discordService.SendTestNotificationAsync();
        }

        if (!trackerEnabled)
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

        DrawShopDataCacheStatus();

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

        var verboseLogging = _configuration.ShopDataVerboseLogging;
        if (ImGui.Checkbox("ショップデータ詳細ログを有効化", ref verboseLogging))
        {
            _configuration.ShopDataVerboseLogging = verboseLogging;
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

    private void DrawShopDataCacheStatus()
    {
        var status = _shopDataCache.BuildStatus;
        if (status.State == ShopCacheBuildState.Running)
        {
            ImGui.Text($"構築中: {status.Phase}");
            if (!string.IsNullOrWhiteSpace(status.Message))
            {
                ImGui.TextDisabled(status.Message);
            }

            if (status.Processed > 0)
            {
                ImGui.TextDisabled($"処理件数: {status.Processed:N0}");
            }

            if (ImGui.Button("構築をキャンセル"))
            {
                _shopDataCache.CancelBuild();
            }
        }
        else
        {
            if (ImGui.Button("ショップデータを再構築"))
            {
                _ = _shopDataCache.RebuildAsync("手動再構築");
            }

            if (status.State == ShopCacheBuildState.Canceled)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("構築はキャンセルされました。");
            }
            else if (status.State == ShopCacheBuildState.Failed)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "構築に失敗しました。");
            }
        }

        ImGui.Separator();
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

        var priorities = _configuration.ShopSearchAreaPriority;
        UpdateFilteredTerritories(priorities);

        if (ImGui.BeginCombo("エリア追加", "追加するエリアを選択"))
        {
            if (_cachedFilteredTerritories.Count == 0)
            {
                ImGui.Text("候補がありません。");
            }

            foreach (var group in _cachedFilteredTerritories)
            {
                if (ImGui.Selectable(group.TerritoryName))
                {
                    // 同名エリアは代表IDのみ追加する
                    if (!priorities.Contains(group.RepresentativeTerritoryTypeId))
                    {
                        priorities.Add(group.RepresentativeTerritoryTypeId);
                        _configuration.Save();
                    }
                    _shopAreaFilter = string.Empty;
                }
            }

            ImGui.EndCombo();
        }
    }

    private void UpdateFilteredTerritories(IReadOnlyList<uint> priorities)
    {
        var priorityHash = ComputePriorityHash(priorities);
        var cacheVersion = _shopDataCache.BuildVersion;

        if (_cachedShopAreaFilter == _shopAreaFilter
            && _cachedPriorityHash == priorityHash
            && _cachedTerritoryGroupsVersion == cacheVersion)
        {
            return;
        }

        var priorityNames = BuildPriorityTerritoryNames(priorities);
        var groups = _shopDataCache.GetShopTerritoryGroups();
        _cachedFilteredTerritories = groups
            .Where(group => !priorityNames.Contains(group.TerritoryName))
            .Where(group => string.IsNullOrWhiteSpace(_shopAreaFilter)
                || group.TerritoryName.Contains(_shopAreaFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _cachedShopAreaFilter = _shopAreaFilter;
        _cachedPriorityHash = priorityHash;
        _cachedTerritoryGroupsVersion = cacheVersion;
    }

    private static int ComputePriorityHash(IReadOnlyList<uint> priorities)
    {
        var hash = new HashCode();
        foreach (var priority in priorities)
        {
            hash.Add(priority);
        }
        return hash.ToHashCode();
    }

    private HashSet<string> BuildPriorityTerritoryNames(IReadOnlyList<uint> priorities)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var territoryId in priorities)
        {
            var territoryName = _shopDataCache.GetTerritoryName(territoryId);
            if (!string.IsNullOrWhiteSpace(territoryName))
            {
                names.Add(territoryName);
            }
        }
        return names;
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

    private static string GetTargetModeLabel(DesynthTargetMode mode)
    {
        return mode switch
        {
            DesynthTargetMode.All => "すべて分解",
            DesynthTargetMode.Count => "個数を指定して分解",
            _ => mode.ToString(),
        };
    }
}
