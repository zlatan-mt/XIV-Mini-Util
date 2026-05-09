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

public sealed class SettingsTab : ITabComponent
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

    private void DrawCharaSelectSettings()
    {
        ImGui.Text("ログイン / タイトル背景");
        ImGui.Separator();

        var emoteEnabled = _configuration.CharaSelectEmoteEnabled;
        if (ImGui.Checkbox("キャラ選択画面で保存したエモートを再生する", ref emoteEnabled))
        {
            _charaSelectService.SetEmoteEnabled(emoteEnabled);
        }

        ImGui.TextDisabled("ログイン中に記録したエモートを、次回以降のキャラ選択画面で再生します。");

        if (!emoteEnabled)
        {
            ImGui.BeginDisabled();
        }

        ImGui.Spacing();
        ImGui.Text($"現在の保存エモート: {_charaSelectService.GetCurrentSelectedEmoteDisplayName()}");
        ImGui.Text($"最後に記録したエモート: {_charaSelectService.GetLastRecordedEmoteDisplayName()}");

        if (ImGui.Button("前へ"))
        {
            _charaSelectService.SelectPreviousEmote();
        }

        ImGui.SameLine();
        if (ImGui.Button("次へ"))
        {
            _charaSelectService.SelectNextEmote();
        }

        ImGui.SameLine();
        if (ImGui.Button("再生"))
        {
            _charaSelectService.ReplaySelectedEmote();
        }

        if (_charaSelectService.IsRecordingEmote)
        {
            if (ImGui.Button("記録停止"))
            {
                _charaSelectService.StopRecordingEmote();
            }
        }
        else
        {
            if (ImGui.Button("記録開始"))
            {
                _charaSelectService.StartRecordingEmote();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("現在スロットへ保存"))
        {
            _charaSelectService.SaveLastRecordedEmoteToActiveSlot();
        }

        ImGui.SameLine();
        if (ImGui.Button("追加保存"))
        {
            _charaSelectService.AppendLastRecordedEmotePreset();
        }

        ImGui.SameLine();
        if (ImGui.Button("削除"))
        {
            _charaSelectService.ClearSelectedEmote();
        }

        if (_charaSelectService.IsRecordingEmote)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "記録中: 保存したいエモートを実行してください。");
        }

        if (!emoteEnabled)
        {
            ImGui.EndDisabled();
        }

        ImGui.Spacing();
        ImGui.Separator();

        ImGui.Text("ログイン先エリア preload / 診断");

        var preloadEnabled = _configuration.CharaSelectPreloadTerritoryEnabled;
        if (ImGui.Checkbox("ログイン待機中にログイン先エリアを事前読み込みする", ref preloadEnabled))
        {
            _charaSelectService.SetPreloadTerritoryEnabled(preloadEnabled);
        }

        ImGui.TextDisabled("これは背景画像の任意差し替えではなく、ログイン先テリトリーのLayout事前ロードです。");
        ImGui.TextDisabled("タイトル背景の差し替えは下の「タイトル背景」設定に分離します。");

        ImGui.Spacing();

        var overrideEnabled = _configuration.CharaSelectOverrideTerritoryEnabled;
        if (ImGui.Checkbox("ログイン先エリア診断でTerritoryTypeIdを固定する（実験）", ref overrideEnabled))
        {
            _charaSelectService.SetOverrideTerritoryEnabled(overrideEnabled);
        }

        var overrideTerritoryId = (int)_configuration.CharaSelectOverrideTerritoryTypeId;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("診断 TerritoryTypeId", ref overrideTerritoryId))
        {
            overrideTerritoryId = Math.Clamp(overrideTerritoryId, 0, ushort.MaxValue);
            _charaSelectService.SetOverrideTerritoryTypeId((ushort)overrideTerritoryId);
        }

        ImGui.Text($"診断固定エリア: {_charaSelectService.GetOverrideTerritoryDisplayName()}");
        ImGui.Text($"現在のログイン先: {_charaSelectService.GetCurrentLoginTerritoryDisplayName()}");
        ImGui.Text($"ログイン位置診断: {_charaSelectService.GetLoginPositionDisplayName()}");

        if (ImGui.Button("現在のログイン先を診断値に使う"))
        {
            _charaSelectService.UseCurrentLoginTerritoryForOverride();
        }

        ImGui.SameLine();
        if (ImGui.Button("診断読み込み"))
        {
            _charaSelectService.SetOverrideTerritoryTypeId((ushort)overrideTerritoryId);
            _charaSelectService.SetOverrideTerritoryEnabled(overrideTerritoryId != 0);
        }

        ImGui.SameLine();
        if (ImGui.Button("診断解除"))
        {
            _charaSelectService.ClearOverrideTerritory();
        }

        var overridePositionEnabled = _configuration.CharaSelectOverridePositionEnabled;
        if (ImGui.Checkbox("ログイン位置診断で地点を指定する（X/Y/Z）", ref overridePositionEnabled))
        {
            _charaSelectService.SetOverridePositionEnabled(overridePositionEnabled);
        }

        var overrideX = _configuration.CharaSelectOverridePositionX;
        var overrideY = _configuration.CharaSelectOverridePositionY;
        var overrideZ = _configuration.CharaSelectOverridePositionZ;

        ImGui.SetNextItemWidth(90f);
        var positionChanged = ImGui.InputFloat("X", ref overrideX, 1f, 10f, "%.2f");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        positionChanged |= ImGui.InputFloat("Y", ref overrideY, 1f, 10f, "%.2f");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        positionChanged |= ImGui.InputFloat("Z", ref overrideZ, 1f, 10f, "%.2f");
        if (positionChanged)
        {
            _charaSelectService.SetOverridePosition(overrideX, overrideY, overrideZ);
        }

        ImGui.Text($"診断地点: {_charaSelectService.GetOverridePositionDisplayName()}");
        if (ImGui.Button("現在位置を診断値に使う"))
        {
            _charaSelectService.UseCurrentPlayerPositionForOverride();
        }

        ImGui.SameLine();
        if (ImGui.Button("診断地点解除"))
        {
            _charaSelectService.SetOverridePositionEnabled(false);
        }

        ImGui.Spacing();

        var showLastDataCenterName = _configuration.CharaSelectShowLastDataCenterNameEnabled;
        if (ImGui.Checkbox("最後にログインしたDC名を記録する（実験）", ref showLastDataCenterName))
        {
            _charaSelectService.SetShowLastDataCenterNameEnabled(showLastDataCenterName);
        }

        ImGui.Text($"最後に記録したDC: {(_configuration.CharaSelectLastDataCenterName.Length == 0 ? "なし" : _configuration.CharaSelectLastDataCenterName)}");
        ImGui.TextDisabled("DataCenter表示の置換は対象addon確認後に有効化します。");

        ImGui.Spacing();
        ImGui.Separator();
        DrawTitleBackgroundSettings();
    }

    private void DrawTitleBackgroundSettings()
    {
        ImGui.Text("キャラクター選択画面背景");
        ImGui.TextDisabled("キャラ選択画面の scene load 時だけ preset 背景へ差し替えます。emote / pet / queue preload とは別機能です。");

        var enabled = _configuration.TitleBackgroundOverrideEnabled;
        if (ImGui.Checkbox("キャラ選択画面背景を差し替える（実験）", ref enabled))
        {
            _titleScreenBackgroundService.SetEnabled(enabled);
        }

        var runtimeMode = _configuration.TitleBackgroundRuntimeMode;
        if (ImGui.BeginCombo("実行モード##TitleBackgroundRuntimeMode", GetTitleBackgroundRuntimeModeLabel(runtimeMode)))
        {
            foreach (TitleBackgroundRuntimeMode mode in Enum.GetValues(typeof(TitleBackgroundRuntimeMode)))
            {
                if (!TitleBackgroundRuntimeModeHelper.IsRuntimeModeSelectable(mode))
                {
                    continue;
                }

                if (ImGui.Selectable(GetTitleBackgroundRuntimeModeLabel(mode), runtimeMode == mode))
                {
                    _configuration.TitleBackgroundRuntimeMode = mode;
                    _configuration.Save();
                    _titleScreenBackgroundService.ReloadNativeIntegration();
                }
            }

            ImGui.EndCombo();
        }
        ImGui.TextDisabled("Title + CharaSelect は未実装のため、実機確認までは選択肢から外しています。");

        var territoryPath = _configuration.TitleBackgroundTerritoryPath;
        if (ImGui.InputTextWithHint("TerritoryPath##TitleBackgroundTerritoryPath", "ffxiv/.../level/...", ref territoryPath, 256))
        {
            _configuration.TitleBackgroundTerritoryPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(territoryPath);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var territoryTypeId = (int)_configuration.TitleBackgroundTerritoryTypeId;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("TerritoryTypeId##TitleBackgroundTerritoryTypeId", ref territoryTypeId))
        {
            _configuration.TitleBackgroundTerritoryTypeId = (uint)Math.Clamp(territoryTypeId, 0, int.MaxValue);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var layoutTerritoryTypeId = (int)_configuration.TitleBackgroundLayoutTerritoryTypeId;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("LayoutTerritoryTypeId##TitleBackgroundLayoutTerritoryTypeId", ref layoutTerritoryTypeId))
        {
            _configuration.TitleBackgroundLayoutTerritoryTypeId = (uint)Math.Clamp(layoutTerritoryTypeId, 0, int.MaxValue);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var layerFilterKey = (int)_configuration.TitleBackgroundLayoutLayerFilterKey;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("LayoutLayerFilterKey##TitleBackgroundLayoutLayerFilterKey", ref layerFilterKey))
        {
            _configuration.TitleBackgroundLayoutLayerFilterKey = (uint)Math.Clamp(layerFilterKey, 0, int.MaxValue);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var characterX = _configuration.TitleBackgroundCharacterPositionX;
        var characterY = _configuration.TitleBackgroundCharacterPositionY;
        var characterZ = _configuration.TitleBackgroundCharacterPositionZ;
        if (DrawTitleBackgroundVectorInput("Character", ref characterX, ref characterY, ref characterZ))
        {
            _configuration.TitleBackgroundCharacterPositionX = characterX;
            _configuration.TitleBackgroundCharacterPositionY = characterY;
            _configuration.TitleBackgroundCharacterPositionZ = characterZ;
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var cameraX = _configuration.TitleBackgroundCameraX;
        var cameraY = _configuration.TitleBackgroundCameraY;
        var cameraZ = _configuration.TitleBackgroundCameraZ;
        if (DrawTitleBackgroundVectorInput("Camera", ref cameraX, ref cameraY, ref cameraZ))
        {
            _configuration.TitleBackgroundCameraX = cameraX;
            _configuration.TitleBackgroundCameraY = cameraY;
            _configuration.TitleBackgroundCameraZ = cameraZ;
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var fovY = _configuration.TitleBackgroundFovY;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputFloat("FOV Y##TitleBackgroundFovY", ref fovY, 1f, 5f, "%.2f"))
        {
            _configuration.TitleBackgroundFovY = TitleBackgroundPreset.ClampFovY(fovY);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }
        ImGui.TextDisabled("Camera / Focus / FOV は Phase 2 予約値です。Phase 1 では scene path 差し替えのみを実行します。");

        if (ImGui.Button("適用"))
        {
            _configuration.TitleBackgroundTerritoryPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(_configuration.TitleBackgroundTerritoryPath);
            _configuration.TitleBackgroundCharacterPositionX = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCharacterPositionX);
            _configuration.TitleBackgroundCharacterPositionY = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCharacterPositionY);
            _configuration.TitleBackgroundCharacterPositionZ = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCharacterPositionZ);
            _configuration.TitleBackgroundCameraX = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraX);
            _configuration.TitleBackgroundCameraY = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraY);
            _configuration.TitleBackgroundCameraZ = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraZ);
            _configuration.TitleBackgroundFovY = TitleBackgroundPreset.ClampFovY(_configuration.TitleBackgroundFovY);
            NormalizeTitleBackgroundSignatures();
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        ImGui.SameLine();
        if (ImGui.Button("解除"))
        {
            _titleScreenBackgroundService.ClearOverride();
        }

        ImGui.SameLine();
        if (ImGui.Button("入力をクリア"))
        {
            ClearTitleBackgroundInputs();
        }

        var statusText = _titleScreenBackgroundService.GetStatusText();
        var statusIsError = statusText.Contains("失敗", StringComparison.Ordinal)
            || statusText.Contains("エラー", StringComparison.Ordinal)
            || statusText.Contains("見つかりません", StringComparison.Ordinal)
            || statusText.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("error", StringComparison.OrdinalIgnoreCase);
        ImGui.TextColored(statusIsError
            ? new Vector4(1f, 0.45f, 0.45f, 1f)
            : new Vector4(0.7f, 0.7f, 0.7f, 1f), statusText);

        if (!string.IsNullOrWhiteSpace(_configuration.TitleBackgroundTerritoryPath))
        {
            ImGui.TextDisabled($"LVB想定パス: {TitleBackgroundPathHelper.BuildLvbPath(_configuration.TitleBackgroundTerritoryPath)}");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("native signature（上級者向け）");
        ImGui.TextDisabled("signature は場所IDやカットシーンIDではなく、ゲーム実行ファイル内の処理を探すための機械語の目印です。");
        ImGui.TextDisabled("現行clientで独自確認した値だけを入力します。既定では空のままfail-closedします。");
        DrawTitleBackgroundResolverModeInputs();
        DrawTitleBackgroundSignatureInputs();

        if (ImGui.Button("address再解決"))
        {
            NormalizeTitleBackgroundSignatures();
            _configuration.Save();
            _titleScreenBackgroundService.ReloadNativeIntegration();
        }

        ImGui.SameLine();
        if (ImGui.Button("signatureをクリア"))
        {
            _configuration.TitleBackgroundCreateSceneSignature = string.Empty;
            _configuration.TitleBackgroundFixOnSignature = string.Empty;
            _configuration.TitleBackgroundLobbyUpdateSignature = string.Empty;
            _configuration.TitleBackgroundLoadLobbySceneSignature = string.Empty;
            _configuration.TitleBackgroundLobbyCurrentMapSignature = string.Empty;
            _configuration.Save();
            _titleScreenBackgroundService.ReloadNativeIntegration();
        }

        ImGui.TextDisabled("BGM / 天候 / 時刻 / 現在地とカメラ保存は後続接続用の設定枠です。");
    }

    private void DrawTitleBackgroundSignatureInputs()
    {
        var createSceneSignature = _configuration.TitleBackgroundCreateSceneSignature;
        var fixOnSignature = _configuration.TitleBackgroundFixOnSignature;
        var lobbyUpdateSignature = _configuration.TitleBackgroundLobbyUpdateSignature;
        var loadLobbySceneSignature = _configuration.TitleBackgroundLoadLobbySceneSignature;
        var lobbyCurrentMapSignature = _configuration.TitleBackgroundLobbyCurrentMapSignature;
        var changed = DrawTitleBackgroundSignatureInput("CreateScene", ref createSceneSignature);
        changed |= DrawTitleBackgroundSignatureInput("FixOn", ref fixOnSignature);
        changed |= DrawTitleBackgroundSignatureInput("LobbyUpdate", ref lobbyUpdateSignature);
        changed |= DrawTitleBackgroundSignatureInput("LoadLobbyScene", ref loadLobbySceneSignature);
        changed |= DrawTitleBackgroundSignatureInput("LobbyCurrentMap", ref lobbyCurrentMapSignature);

        if (!changed)
        {
            return;
        }

        _configuration.TitleBackgroundCreateSceneSignature = createSceneSignature.Trim();
        _configuration.TitleBackgroundFixOnSignature = fixOnSignature.Trim();
        _configuration.TitleBackgroundLobbyUpdateSignature = lobbyUpdateSignature.Trim();
        _configuration.TitleBackgroundLoadLobbySceneSignature = loadLobbySceneSignature.Trim();
        _configuration.TitleBackgroundLobbyCurrentMapSignature = lobbyCurrentMapSignature.Trim();
        _configuration.Save();
    }

    private void DrawTitleBackgroundResolverModeInputs()
    {
        var createSceneMode = _configuration.TitleBackgroundCreateSceneResolverMode;
        var lobbyUpdateMode = _configuration.TitleBackgroundLobbyUpdateResolverMode;
        var changed = DrawTitleBackgroundResolverModeInput("CreateScene", ref createSceneMode);
        changed |= DrawTitleBackgroundResolverModeInput("LobbyUpdate", ref lobbyUpdateMode);

        if (!changed)
        {
            return;
        }

        _configuration.TitleBackgroundCreateSceneResolverMode = createSceneMode;
        _configuration.TitleBackgroundLobbyUpdateResolverMode = lobbyUpdateMode;
        _configuration.Save();
    }

    private static bool DrawTitleBackgroundResolverModeInput(string label, ref TitleBackgroundResolverMode mode)
    {
        if (!ImGui.BeginCombo($"{label} resolver##TitleBackground{label}ResolverMode", GetTitleBackgroundResolverModeLabel(mode)))
        {
            return false;
        }

        var changed = false;
        foreach (TitleBackgroundResolverMode candidate in Enum.GetValues(typeof(TitleBackgroundResolverMode)))
        {
            if (ImGui.Selectable(GetTitleBackgroundResolverModeLabel(candidate), mode == candidate))
            {
                mode = candidate;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static bool DrawTitleBackgroundSignatureInput(string label, ref string signature)
    {
        return ImGui.InputTextWithHint($"{label}##TitleBackground{label}Signature", "xx xx ?? ...", ref signature, 512);
    }

    private void NormalizeTitleBackgroundSignatures()
    {
        _configuration.TitleBackgroundCreateSceneSignature = (_configuration.TitleBackgroundCreateSceneSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundFixOnSignature = (_configuration.TitleBackgroundFixOnSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundLobbyUpdateSignature = (_configuration.TitleBackgroundLobbyUpdateSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundLoadLobbySceneSignature = (_configuration.TitleBackgroundLoadLobbySceneSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundLobbyCurrentMapSignature = (_configuration.TitleBackgroundLobbyCurrentMapSignature ?? string.Empty).Trim();
    }

    private static string GetTitleBackgroundRuntimeModeLabel(TitleBackgroundRuntimeMode mode)
    {
        return mode switch
        {
            TitleBackgroundRuntimeMode.ResolveOnly => "address解決のみ",
            TitleBackgroundRuntimeMode.Disabled => "無効",
            TitleBackgroundRuntimeMode.HookProbe => "hook probe（変更なし）",
            TitleBackgroundRuntimeMode.CharaSelectOnly => "キャラ選択のみ",
            TitleBackgroundRuntimeMode.TitleAndCharaSelect => "タイトル+キャラ選択",
            _ => mode.ToString(),
        };
    }

    private static string GetTitleBackgroundResolverModeLabel(TitleBackgroundResolverMode mode)
    {
        return mode switch
        {
            TitleBackgroundResolverMode.AutoDiagnosticOnly => "自動診断のみ",
            TitleBackgroundResolverMode.ManualDirectTextProbe => "手動DirectText probe",
            _ => mode.ToString(),
        };
    }

    private bool DrawTitleBackgroundVectorInput(string label, ref float x, ref float y, ref float z)
    {
        ImGui.Text(label);

        ImGui.SetNextItemWidth(90f);
        var changed = ImGui.InputFloat($"X##TitleBackground{label}X", ref x, 1f, 10f, "%.2f");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        changed |= ImGui.InputFloat($"Y##TitleBackground{label}Y", ref y, 1f, 10f, "%.2f");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        changed |= ImGui.InputFloat($"Z##TitleBackground{label}Z", ref z, 1f, 10f, "%.2f");

        if (changed)
        {
            x = TitleBackgroundPreset.SanitizeCoordinate(x);
            y = TitleBackgroundPreset.SanitizeCoordinate(y);
            z = TitleBackgroundPreset.SanitizeCoordinate(z);
        }

        return changed;
    }

    private void ClearTitleBackgroundInputs()
    {
        _configuration.TitleBackgroundOverrideEnabled = false;
        _configuration.TitleBackgroundCameraOverrideEnabled = false;
        _configuration.TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.ResolveOnly;
        _configuration.TitleBackgroundCreateSceneResolverMode = TitleBackgroundResolverMode.AutoDiagnosticOnly;
        _configuration.TitleBackgroundLobbyUpdateResolverMode = TitleBackgroundResolverMode.AutoDiagnosticOnly;
        _configuration.TitleBackgroundTerritoryPath = string.Empty;
        _configuration.TitleBackgroundTerritoryTypeId = 0;
        _configuration.TitleBackgroundLayoutTerritoryTypeId = 0;
        _configuration.TitleBackgroundLayoutLayerFilterKey = 0;
        _configuration.TitleBackgroundCharacterPositionX = 0f;
        _configuration.TitleBackgroundCharacterPositionY = 0f;
        _configuration.TitleBackgroundCharacterPositionZ = 0f;
        _configuration.TitleBackgroundCharacterRotation = 0f;
        _configuration.TitleBackgroundCameraX = 0f;
        _configuration.TitleBackgroundCameraY = 0f;
        _configuration.TitleBackgroundCameraZ = 0f;
        _configuration.TitleBackgroundFocusX = 0f;
        _configuration.TitleBackgroundFocusY = 0f;
        _configuration.TitleBackgroundFocusZ = 0f;
        _configuration.TitleBackgroundFovY = TitleBackgroundPreset.DefaultFovY;
        _configuration.TitleBackgroundWeatherId = 0;
        _configuration.TitleBackgroundTimeOffset = 0;
        _configuration.TitleBackgroundBgmPath = string.Empty;
        _configuration.TitleBackgroundCreateSceneSignature = string.Empty;
        _configuration.TitleBackgroundFixOnSignature = string.Empty;
        _configuration.TitleBackgroundLobbyUpdateSignature = string.Empty;
        _configuration.TitleBackgroundLoadLobbySceneSignature = string.Empty;
        _configuration.TitleBackgroundLobbyCurrentMapSignature = string.Empty;
        _configuration.Save();
        _titleScreenBackgroundService.ReloadNativeIntegration();
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

        ImGui.Spacing();
        ImGui.Text("Universalis検索");

        var showTopThree = _configuration.UniversalisShowTopThreeListings;
        if (ImGui.Checkbox("3位までの安値を表示", ref showTopThree))
        {
            _configuration.UniversalisShowTopThreeListings = showTopThree;
            _configuration.Save();
        }

        var searchRegionWide = _configuration.UniversalisSearchRegionWide;
        if (ImGui.Checkbox("データセンター外も検索", ref searchRegionWide))
        {
            _configuration.UniversalisSearchRegionWide = searchRegionWide;
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
