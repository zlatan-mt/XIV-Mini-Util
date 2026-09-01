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
using XivMiniUtil.Services.CharaSelect;
using XivMiniUtil.Services.Notification;
using XivMiniUtil.Services.Shop;
using XivMiniUtil.Services.TitleBackground;

namespace XivMiniUtil.Windows.Components;

public sealed partial class SettingsTab : ITabComponent
{
    private readonly Configuration _configuration;
    private readonly ShopDataCache _shopDataCache;
    private readonly DiscordService _discordService;
    private readonly DutyReadyNotificationService _dutyReadyNotificationService;
    private readonly CharaSelectService _charaSelectService;
    private readonly TitleScreenBackgroundService _titleScreenBackgroundService;
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
        ShopDataCache shopDataCache,
        DiscordService discordService,
        DutyReadyNotificationService dutyReadyNotificationService,
        CharaSelectService charaSelectService,
        TitleScreenBackgroundService titleScreenBackgroundService,
        bool desynthFeatureEnabled)
    {
        _configuration = configuration;
        _shopDataCache = shopDataCache;
        _discordService = discordService;
        _dutyReadyNotificationService = dutyReadyNotificationService;
        _charaSelectService = charaSelectService;
        _titleScreenBackgroundService = titleScreenBackgroundService;
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

    }

    public void Dispose()
    {
    }

    private void DrawSettingsCategoryList()
    {
        var categories = new[]
        {
            _desynthFeatureEnabled ? "分解" : "分解（無効中）",
            "ショップ検索",
            "チェックリスト",
            "シャキ通知",
            "ログイン背景",
            "キャラ選択",
            "潜水艦",
            "バックアップ",
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
                DrawDesynthSettings();
                break;
            case 1:
                DrawShopSearchSettings();
                break;
            case 2:
                DrawChecklistSettings();
                break;
            case 3:
                DrawDutyReadySettings();
                break;
            case 4:
                DrawTitleBackgroundSettings();
                break;
            case 5:
                DrawCharaSelectEmoteSettings();
                break;
            case 6:
                DrawSubmarineSettings();
                break;
            case 7:
                DrawConfigIoSection();
                break;
            default:
                DrawDesynthSettings();
                break;
        }

        ImGui.EndChild();
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
                    _charaSelectService.SyncFromConfiguration();
                    _titleScreenBackgroundService.ReloadNativeIntegration();
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


}
