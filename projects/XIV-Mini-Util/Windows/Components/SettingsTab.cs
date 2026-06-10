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
using XivMiniUtil.Services.CharaSelect;
using XivMiniUtil.Services.Checklist;
using XivMiniUtil.Services.Notification;
using XivMiniUtil.Services.Shop;
using XivMiniUtil.Services.TitleBackground;

namespace XivMiniUtil.Windows.Components;

public sealed partial class SettingsTab : ITabComponent
{
    private readonly Configuration _configuration;
    private readonly MateriaExtractService _materiaService;
    private readonly DesynthService _desynthService;
    private readonly ShopDataCache _shopDataCache;
    private readonly DiscordService _discordService;
    private readonly ChecklistService _checklistService;
    private readonly DutyReadyNotificationService _dutyReadyNotificationService;
    private readonly CharaSelectService _charaSelectService;
    private readonly TitleScreenBackgroundService _titleScreenBackgroundService;
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
    private string _titleBackgroundPendingPresetId = string.Empty;
    private string _titleBackgroundPresetMessage = string.Empty;
    private Vector4 _titleBackgroundPresetMessageColor = new(0.7f, 0.7f, 0.7f, 1f);
    private string _titleBackgroundSceneCopyMessage = string.Empty;
    private Vector4 _titleBackgroundSceneCopyMessageColor = new(0.7f, 0.7f, 0.7f, 1f);
    private string _titleBackgroundCameraProfileMessage = string.Empty;
    private Vector4 _titleBackgroundCameraProfileMessageColor = new(0.7f, 0.7f, 0.7f, 1f);

    // Submarine Settings State

    public SettingsTab(
        Configuration configuration,
        MateriaExtractService materiaService,
        DesynthService desynthService,
        ShopDataCache shopDataCache,
        DiscordService discordService,
        ChecklistService checklistService,
        DutyReadyNotificationService dutyReadyNotificationService,
        CharaSelectService charaSelectService,
        TitleScreenBackgroundService titleScreenBackgroundService,
        bool materiaFeatureEnabled,
        bool desynthFeatureEnabled)
    {
        _configuration = configuration;
        _materiaService = materiaService;
        _desynthService = desynthService;
        _shopDataCache = shopDataCache;
        _discordService = discordService;
        _checklistService = checklistService;
        _dutyReadyNotificationService = dutyReadyNotificationService;
        _charaSelectService = charaSelectService;
        _titleScreenBackgroundService = titleScreenBackgroundService;
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
            "Checklist",
            "シャキ通知",
            "Login / Title Background",
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
                DrawChecklistSettings();
                break;
            case 4:
                DrawDutyReadySettings();
                break;
            case 5:
                DrawCharaSelectSettings();
                break;
            case 6:
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

    private void DrawDutyReadySettings()
    {
        ImGui.Text("シャキ通知");
        ImGui.Separator();

        var enabled = _configuration.DutyReadySoundNotificationEnabled;
        if (ImGui.Checkbox("シャキ通知音を有効化", ref enabled))
        {
            _configuration.DutyReadySoundNotificationEnabled = enabled;
            _configuration.Save();
        }

        var durationSeconds = _configuration.DutyReadySoundDurationSeconds;
        if (ImGui.InputInt("通知時間 (秒)", ref durationSeconds))
        {
            _configuration.DutyReadySoundDurationSeconds = Math.Clamp(durationSeconds, 3, 30);
            _configuration.Save();
        }

        ImGui.TextDisabled("3〜30秒。確認画面が消えた場合は設定時間内でも停止します。");
        ImGui.TextDisabled("FFXIVウィンドウが前面ではない場合だけAlarm05.wavを鳴らします。");
        ImGui.TextDisabled("申請をOK/キャンセルした場合、または停止ボタンを押した場合は停止します。");

        ImGui.Spacing();
        if (ImGui.Button("テスト再生"))
        {
            _dutyReadyNotificationService.PlayTest();
        }

        ImGui.SameLine();
        if (ImGui.Button("5秒後にテスト再生"))
        {
            _ = _dutyReadyNotificationService.PlayTestAfterDelayAsync(TimeSpan.FromSeconds(5));
        }

        ImGui.SameLine();
        if (ImGui.Button("通知音を停止"))
        {
            _dutyReadyNotificationService.StopNotification();
        }
    }

    private void DrawChecklistSettings()
    {
        ImGui.Text("日課チェックリスト");
        ImGui.Separator();

        var checklistEnabled = _configuration.ChecklistFeatureEnabled;
        if (ImGui.Checkbox("チェックリスト機能を有効化", ref checklistEnabled))
        {
            _configuration.ChecklistFeatureEnabled = checklistEnabled;
            _configuration.Save();
        }

        var discordEnabled = _configuration.ChecklistDiscordNotificationEnabled;
        if (ImGui.Checkbox("チェックリストのDiscord通知を有効化", ref discordEnabled))
        {
            _configuration.ChecklistDiscordNotificationEnabled = discordEnabled;
            _configuration.Save();
        }

        var weeklyResetDay = _configuration.ChecklistWeeklyResetDay;
        if (ImGui.BeginCombo("週次リセット曜日", weeklyResetDay.ToString()))
        {
            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                var selected = day == weeklyResetDay;
                if (ImGui.Selectable(day.ToString(), selected))
                {
                    _configuration.ChecklistWeeklyResetDay = day;
                    _configuration.Save();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Checklistタブで各項目の時刻・通知先を個別設定できます。");

        if (ImGui.Button("Daily項目を全て未完了に戻す"))
        {
            _checklistService.ResetItems(Models.Checklist.ChecklistFrequency.Daily);
        }

        ImGui.SameLine();
        if (ImGui.Button("Weekly項目を全て未完了に戻す"))
        {
            _checklistService.ResetItems(Models.Checklist.ChecklistFrequency.Weekly);
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
