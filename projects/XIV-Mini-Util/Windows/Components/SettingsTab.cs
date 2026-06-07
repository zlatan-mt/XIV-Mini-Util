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
    private string _titleBackgroundPendingPresetId = string.Empty;
    private string _titleBackgroundPresetMessage = string.Empty;
    private Vector4 _titleBackgroundPresetMessageColor = new(0.7f, 0.7f, 0.7f, 1f);
    private string _titleBackgroundSceneCopyMessage = string.Empty;
    private Vector4 _titleBackgroundSceneCopyMessageColor = new(0.7f, 0.7f, 0.7f, 1f);

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

        DrawCharaSelectSceneCompositionSettings();

        ImGui.Spacing();
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

        DrawTitleBackgroundSettings();

        ImGui.Spacing();
        ImGui.Separator();
        DrawLegacyCharaSelectDiagnostics();
    }

    private void DrawLegacyCharaSelectDiagnostics()
    {
        if (!ImGui.CollapsingHeader("旧診断 / Legacy experiments"))
        {
            return;
        }

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
    }

    private void DrawCharaSelectSceneCompositionSettings()
    {
        ImGui.Text("通常: キャラを残す撮影構成");
        ImGui.TextWrapped("キャラ選択画面の本物の選択キャラクターを残したまま、場所・立ち位置・エモートを構成します。背景だけ実験とは同時に使いません。");

        var enabled = _configuration.CharaSelectSceneCompositionEnabled;
        if (ImGui.Checkbox("撮影構成を有効にする", ref enabled))
        {
            if (enabled && _configuration.TitleBackgroundOverrideEnabled)
            {
                _titleScreenBackgroundService.SetEnabled(false);
            }

            _charaSelectService.SetSceneCompositionEnabled(enabled);
        }

        var selectedProfile = _charaSelectService.CurrentSceneProfile;
        DrawSceneCompositionDecisionSummary(selectedProfile);

        if (ImGui.BeginCombo("撮影 profile##CharaSelectSceneProfile", _charaSelectService.GetCurrentSceneProfileLabel()))
        {
            foreach (var profile in _charaSelectService.SceneProfiles)
            {
                if (ImGui.Selectable(CharaSelectSceneProfileRegistry.BuildLabel(profile), selectedProfile.Id == profile.Id))
                {
                    _charaSelectService.SetSceneProfileId(profile.Id);
                }
            }

            ImGui.EndCombo();
        }

        var useTerritory = _configuration.CharaSelectSceneUseProfileTerritory;
        if (ImGui.Checkbox("profile の場所を使う", ref useTerritory))
        {
            _charaSelectService.SetSceneUseProfileTerritory(useTerritory);
        }

        var stageStrategy = _configuration.CharaSelectSceneStageStrategy;
        if (ImGui.BeginCombo("場所変更ルート##CharaSelectStageStrategy", GetStageStrategyLabel(stageStrategy)))
        {
            foreach (CharaSelectStageStrategy strategy in Enum.GetValues(typeof(CharaSelectStageStrategy)))
            {
                if (ImGui.Selectable(GetStageStrategyLabel(strategy), stageStrategy == strategy))
                {
                    _charaSelectService.SetSceneStageStrategy(strategy);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextDisabled(GetStageStrategyDescription(stageStrategy));

        var useSavedEmote = _configuration.CharaSelectSceneUseSavedEmote;
        if (ImGui.Checkbox("保存済みエモートを再生する", ref useSavedEmote))
        {
            _charaSelectService.SetSceneUseSavedEmote(useSavedEmote);
        }

        ImGui.Text($"現在の保存エモート: {_charaSelectService.GetCurrentSelectedEmoteDisplayName()}");
        ImGui.Text($"最後に記録したエモート: {_charaSelectService.GetLastRecordedEmoteDisplayName()}");

        var usePosition = _configuration.CharaSelectSceneUseProfilePosition;
        if (ImGui.Checkbox("profile の立ち位置を使う（Phase3Aでは観測のみ）", ref usePosition))
        {
            _charaSelectService.SetSceneUseProfilePosition(usePosition);
        }

        var placementMode = _configuration.CharaSelectScenePlacementMode;
        if (ImGui.BeginCombo("立ち位置 mode##CharaSelectScenePlacementMode", placementMode.ToString()))
        {
            foreach (CharaSelectScenePlacementMode mode in Enum.GetValues(typeof(CharaSelectScenePlacementMode)))
            {
                if (ImGui.Selectable(mode.ToString(), placementMode == mode))
                {
                    _charaSelectService.SetScenePlacementMode(mode);
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.Button("撮影構成を適用"))
        {
            if (_configuration.TitleBackgroundOverrideEnabled)
            {
                _titleScreenBackgroundService.SetEnabled(false);
            }

            _charaSelectService.ApplyCurrentSceneProfile();
        }

        ImGui.SameLine();
        if (ImGui.Button("再生テスト##CharaSelectSceneReplay"))
        {
            _charaSelectService.ReplaySelectedEmote();
        }

        ImGui.TextDisabled("Phase3A は position write を行いません。OneShotAfterDisplay は Phase3B 用の枠です。");

        ImGui.Spacing();
        ImGui.Text("SS判定");
        var characterVisible = _configuration.LastSceneProfileCharacterVisibleResult;
        if (DrawSceneBinaryResultCombo("キャラ本体##SceneCharacterVisible", ref characterVisible, "見えた", "見えない"))
        {
            _charaSelectService.SetSceneCharacterVisibleResult(characterVisible);
        }

        var locationChanged = _configuration.LastSceneProfileLocationChangedResult;
        if (DrawSceneBinaryResultCombo("場所##SceneLocationChanged", ref locationChanged, "変わった", "変わらない"))
        {
            _charaSelectService.SetSceneLocationChangedResult(locationChanged);
        }

        ImGui.TextWrapped("場所が default のままなら「場所=変わらない」を選んでください。これは TerritoryTypeId の差し替えだけでは見た目のステージが変わっていないことを意味します。");

        var emotePlayed = _configuration.LastSceneProfileEmotePlayedResult;
        if (DrawSceneBinaryResultCombo("エモート##SceneEmotePlayed", ref emotePlayed, "再生した", "再生しない"))
        {
            _charaSelectService.SetSceneEmotePlayedResult(emotePlayed);
        }

        var brightness = _configuration.LastSceneProfileBrightnessResult;
        if (DrawSceneBrightnessResultCombo("明るさ##SceneBrightness", ref brightness))
        {
            _charaSelectService.SetSceneBrightnessResult(brightness);
        }

        if (ImGui.CollapsingHeader("Stage probe 詳細 / 開発者向け"))
        {
            ImGui.TextDisabled($"Route: {CharaSelectSceneCompositionPlanner.ForegroundPreservingRoute}");
            ImGui.TextDisabled($"Expected character visible: {selectedProfile.CharacterExpectedVisible}");
            ImGui.TextDisabled($"Brightness: {selectedProfile.ExpectedBrightness}");
            ImGui.TextDisabled($"Recommended: {selectedProfile.RecommendedAction}");
            ImGui.TextDisabled($"Profile territory: {selectedProfile.TerritoryTypeId}");
            ImGui.TextDisabled($"Profile path: {selectedProfile.TerritoryPath}");
            ImGui.TextDisabled($"Stage strategy last result: {_configuration.CharaSelectSceneStageStrategyLastResult}");
            ImGui.TextDisabled($"Stage strategy last reason: {_configuration.CharaSelectSceneStageStrategyLastReason}");
            ImGui.TextDisabled("Lobby position / Layout prefetch / ClientSelectData details は /xmucdiag に出力します。");
        }
    }

    private void DrawSceneCompositionDecisionSummary(CharaSelectSceneProfile selectedProfile)
    {
        var verdict = GetSceneCompositionVerdictLabel();
        var nextAction = CharaSelectSceneCompositionPlanner.BuildNextAction(_configuration);
        var statusColor = GetSceneCompositionVerdictColor();

        ImGui.Spacing();
        ImGui.Text("現在の判定");
        ImGui.TextColored(statusColor, verdict);
        ImGui.TextWrapped(
            $"結果: キャラ={GetSceneBinaryResultLabel(_configuration.LastSceneProfileCharacterVisibleResult, "見えた", "見えない")} / " +
            $"場所={GetSceneBinaryResultLabel(_configuration.LastSceneProfileLocationChangedResult, "変わった", "変わらない")} / " +
            $"エモート={GetSceneBinaryResultLabel(_configuration.LastSceneProfileEmotePlayedResult, "再生した", "再生しない")} / " +
            $"明るさ={GetSceneBrightnessResultLabel(_configuration.LastSceneProfileBrightnessResult)}");
        ImGui.TextWrapped($"次: {GetSceneNextActionLabel(nextAction)}");

        if (_configuration.CharaSelectSceneCompositionEnabled && _configuration.TitleBackgroundOverrideEnabled)
        {
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f), "背景だけ実験が同時に有効です。撮影構成を使う場合は背景だけ実験を無効にしてください。");
        }

        ImGui.TextDisabled($"期待: キャラ={BoolToVisibleLabel(selectedProfile.CharacterExpectedVisible)} / 明るさ={selectedProfile.ExpectedBrightness}");
    }

    private void DrawTitleBackgroundSettings()
    {
        ImGui.Text("Title Background");
        ImGui.TextWrapped("Character Select の背景だけを差し替えます。選択キャラクター本体は表示されない想定です。");

        DrawTitleBackgroundStatusSummary();
        DrawTitleBackgroundQuickActions();
        DrawTitleBackgroundDisplayModeSelector();
        DrawTitleBackgroundKnownLimitation();

        if (_configuration.TitleBackgroundSettingsDisplayMode == TitleBackgroundSettingsDisplayMode.Simple)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Advanced Settings");
        var runtimeMode = _configuration.TitleBackgroundRuntimeMode;
        if (ImGui.BeginCombo("実行範囲##TitleBackgroundRuntimeMode", GetTitleBackgroundRuntimeModeLabel(runtimeMode)))
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

        DrawTitleBackgroundCharacterSelectDeliveryModes();
        ImGui.Spacing();
        DrawTitleBackgroundEffectiveCandidateDetails();

        if (ImGui.Button("解除"))
        {
            _titleScreenBackgroundService.ClearOverride();
        }

        ImGui.SameLine();
        if (ImGui.Button("入力をクリア"))
        {
            ClearTitleBackgroundInputs();
        }

        if (!string.IsNullOrWhiteSpace(_configuration.TitleBackgroundTerritoryPath))
        {
            ImGui.TextDisabled($"LVB想定パス: {TitleBackgroundPathHelper.BuildLvbPath(_configuration.TitleBackgroundTerritoryPath)}");
        }

        if (_configuration.TitleBackgroundSettingsDisplayMode != TitleBackgroundSettingsDisplayMode.DeveloperDiagnostics)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Developer Diagnostics");
        DrawTitleBackgroundPresetSettings();
        ImGui.Spacing();
        DrawTitleBackgroundPhase2Settings();
        ImGui.Spacing();
        DrawTitleBackgroundAdvancedSettings();
    }

    private void DrawTitleBackgroundStatusSummary()
    {
        var summary = TitleBackgroundQuickCheckUiPresenter.BuildSummary(_configuration);
        var statusColor = summary.Level switch
        {
            TitleBackgroundQuickCheckLevel.OK => new Vector4(0.3f, 0.8f, 0.45f, 1f),
            TitleBackgroundQuickCheckLevel.WARN => new Vector4(1f, 0.75f, 0.35f, 1f),
            TitleBackgroundQuickCheckLevel.NG => new Vector4(1f, 0.45f, 0.45f, 1f),
            _ => new Vector4(0.7f, 0.7f, 0.7f, 1f),
        };

        ImGui.Spacing();
        ImGui.Text($"Title Background: {(_configuration.TitleBackgroundOverrideEnabled ? "ON" : "OFF")}");
        ImGui.Text(summary.CandidateLine);
        ImGui.TextColored(statusColor, summary.StatusLine);
        ImGui.TextWrapped(summary.NextActionLine);
        ImGui.TextDisabled(_titleScreenBackgroundService.GetStatusText());
    }

    private void DrawTitleBackgroundQuickActions()
    {
        ImGui.Spacing();
        ImGui.Text("Quick Actions");

        var enabled = _configuration.TitleBackgroundOverrideEnabled;
        if (ImGui.Checkbox("Enable Character Select Background", ref enabled))
        {
            if (enabled && _configuration.CharaSelectSceneCompositionEnabled)
            {
                _charaSelectService.DisableSceneCompositionForTitleBackgroundRoute();
            }

            if (enabled && _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.ResolveOnly)
            {
                _configuration.TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.CharaSelectOnly;
                _configuration.Save();
            }

            _titleScreenBackgroundService.SetEnabled(enabled);
        }

        DrawTitleBackgroundOverrideCandidateSelector(showManualSlot: false);
        DrawTitleBackgroundCameraFramingSelector();
        DrawTitleBackgroundCharacterVisualStatusSelector();

        if (ImGui.Button("Start QuickCheck"))
        {
            _titleScreenBackgroundService.StartQuickCheck();
        }

        ImGui.SameLine();
        if (ImGui.Button("Run Check"))
        {
            _titleScreenBackgroundService.RunQuickCheck();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset Check"))
        {
            _titleScreenBackgroundService.ResetQuickCheck();
        }

        var summary = TitleBackgroundQuickCheckUiPresenter.BuildSummary(_configuration);
        ImGui.Text(summary.LastResultLine);
        ImGui.Text(summary.LastReasonLine);
        ImGui.Text(summary.NextActionLine);
        ImGui.Text(summary.DetailLine);
        if (!string.IsNullOrWhiteSpace(_configuration.TitleBackgroundLastQuickCheckTime))
        {
            ImGui.Text($"Last Check Time: {_configuration.TitleBackgroundLastQuickCheckTime}");
        }
    }

    private void DrawTitleBackgroundDisplayModeSelector()
    {
        var displayMode = _configuration.TitleBackgroundSettingsDisplayMode;
        ImGui.Spacing();
        if (ImGui.BeginCombo("Display Mode##TitleBackgroundSettingsDisplayMode", GetTitleBackgroundSettingsDisplayModeLabel(displayMode)))
        {
            foreach (TitleBackgroundSettingsDisplayMode mode in Enum.GetValues(typeof(TitleBackgroundSettingsDisplayMode)))
            {
                if (ImGui.Selectable(GetTitleBackgroundSettingsDisplayModeLabel(mode), displayMode == mode))
                {
                    _configuration.TitleBackgroundSettingsDisplayMode = mode;
                    _configuration.Save();
                }
            }

            ImGui.EndCombo();
        }
    }

    private static void DrawTitleBackgroundKnownLimitation()
    {
        ImGui.Spacing();
        ImGui.Text("Known limitation:");
        ImGui.TextWrapped("Character source is not resolved by diagnostics; visual confirmation is required.");
        ImGui.TextDisabled("Character may appear off-center or too small depending on camera framing.");
    }

    private void DrawTitleBackgroundPresetSettings()
    {
        var selectedPresetId = string.IsNullOrEmpty(_titleBackgroundPendingPresetId)
            ? _configuration.TitleBackgroundSelectedPresetId
            : _titleBackgroundPendingPresetId;
        var selectedLabel = "未選択 / Custom";
        if (TitleBackgroundBuiltInPresetCatalog.TryGetById(selectedPresetId, out var selectedEntry))
        {
            selectedLabel = selectedEntry.DisplayName;
            if (!string.IsNullOrEmpty(_titleBackgroundPendingPresetId))
            {
                selectedLabel += " (未適用)";
            }
        }

        if (ImGui.BeginCombo("背景 preset##TitleBackgroundPreset", selectedLabel))
        {
            if (ImGui.Selectable("未選択 / Custom", string.IsNullOrEmpty(selectedPresetId)))
            {
                _titleBackgroundPendingPresetId = string.Empty;
                _configuration.TitleBackgroundSelectedPresetId = string.Empty;
                _configuration.Save();
            }

            foreach (var entry in TitleBackgroundBuiltInPresetCatalog.Presets)
            {
                if (ImGui.Selectable(entry.DisplayName, string.Equals(selectedPresetId, entry.Id, StringComparison.Ordinal)))
                {
                    _titleBackgroundPendingPresetId = entry.Id;
                }
            }

            ImGui.EndCombo();
        }

        if (TitleBackgroundBuiltInPresetCatalog.Presets.Count == 0)
        {
            ImGui.TextDisabled("実機確認済みの built-in preset はまだありません。");
        }

        if (ImGui.Button("preset を適用"))
        {
            var presetId = string.IsNullOrWhiteSpace(_titleBackgroundPendingPresetId)
                ? _configuration.TitleBackgroundSelectedPresetId
                : _titleBackgroundPendingPresetId;
            if (string.IsNullOrWhiteSpace(presetId))
            {
                _titleBackgroundPresetMessage = "preset が未選択です。";
                _titleBackgroundPresetMessageColor = new Vector4(1f, 0.75f, 0.35f, 1f);
            }
            else if (_titleScreenBackgroundService.TryApplyBuiltInPreset(presetId, out var errorMessage))
            {
                _titleBackgroundPendingPresetId = string.Empty;
                _titleBackgroundPresetMessage = "preset を適用しました。";
                _titleBackgroundPresetMessageColor = new Vector4(0.3f, 0.8f, 0.45f, 1f);
            }
            else
            {
                _titleBackgroundPresetMessage = $"preset 適用失敗: {errorMessage}";
                _titleBackgroundPresetMessageColor = new Vector4(1f, 0.45f, 0.45f, 1f);
            }
        }

        if (!string.IsNullOrWhiteSpace(_titleBackgroundPresetMessage))
        {
            ImGui.TextColored(_titleBackgroundPresetMessageColor, _titleBackgroundPresetMessage);
        }
    }

    private void DrawTitleBackgroundPhase2Settings()
    {
        if (!ImGui.CollapsingHeader("詳細設定 / 診断"))
        {
            return;
        }

        ImGui.TextDisabled("手入力と現在値保存は debug 補助です。通常は built-in preset を選んで適用します。");
        ImGui.TextDisabled("CharacterPosition は将来のキャラクター配置用で、Camera Focus とは別の値です。");

        DrawTitleBackgroundCharacterSelectDeliveryModes();
        ImGui.Spacing();
        DrawTitleBackgroundEffectiveCandidateDetails();
        DrawTitleBackgroundManualCandidateSlot1(BuildTitleBackgroundManualCandidateSlots()[0]);
        ImGui.Spacing();

        var territoryPath = _configuration.TitleBackgroundTerritoryPath;
        if (ImGui.InputTextWithHint("TerritoryPath##TitleBackgroundTerritoryPath", "ffxiv/.../level/...", ref territoryPath, 256))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
            _configuration.TitleBackgroundTerritoryPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(territoryPath);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        if (ImGui.Button("現在値を debug 保存"))
        {
            _titleScreenBackgroundService.CaptureCurrentLocationAndCamera();
        }

        ImGui.SameLine();
        var cameraOverrideEnabled = _configuration.TitleBackgroundCameraOverrideEnabled;
        if (ImGui.Checkbox("カメラ調整を有効化（実験）", ref cameraOverrideEnabled))
        {
            _titleScreenBackgroundService.SetCameraOverrideEnabled(cameraOverrideEnabled);
        }

        ImGui.TextDisabled(cameraOverrideEnabled
            ? "Camera override: ON。CharaSelectOnly の scene 差し替え後に FixOn hook で適用します。"
            : "Camera override: OFF。camera 値は上書きしません。FixOn hook 有効時は passthrough 観測のみ行います。");
        ImGui.TextDisabled("hook状態と保存失敗理由は /xmutbgdiag でも確認できます。");

        DrawTitleBackgroundCaptureResult();

        ImGui.Spacing();

        var cameraX = _configuration.TitleBackgroundCameraX;
        var cameraY = _configuration.TitleBackgroundCameraY;
        var cameraZ = _configuration.TitleBackgroundCameraZ;
        if (DrawTitleBackgroundVectorInput("Camera", ref cameraX, ref cameraY, ref cameraZ))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCameraX = cameraX;
            _configuration.TitleBackgroundCameraY = cameraY;
            _configuration.TitleBackgroundCameraZ = cameraZ;
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var focusX = _configuration.TitleBackgroundFocusX;
        var focusY = _configuration.TitleBackgroundFocusY;
        var focusZ = _configuration.TitleBackgroundFocusZ;
        if (DrawTitleBackgroundVectorInput("Focus", ref focusX, ref focusY, ref focusZ))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundFocusX = focusX;
            _configuration.TitleBackgroundFocusY = focusY;
            _configuration.TitleBackgroundFocusZ = focusZ;
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }
        ImGui.TextDisabled("Focus は FixOn focus 引数へ渡す値です。実機観測では post-FixOn LookAtVector に反映されます。");

        var fovY = _configuration.TitleBackgroundFovY;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputFloat("FOV Y##TitleBackgroundFovY", ref fovY, 1f, 5f, "%.2f"))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundFovY = TitleBackgroundPreset.ClampFovY(fovY);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        ImGui.Spacing();
        var characterX = _configuration.TitleBackgroundCharacterPositionX;
        var characterY = _configuration.TitleBackgroundCharacterPositionY;
        var characterZ = _configuration.TitleBackgroundCharacterPositionZ;
        if (DrawTitleBackgroundVectorInput("Character", ref characterX, ref characterY, ref characterZ))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCharacterPositionX = characterX;
            _configuration.TitleBackgroundCharacterPositionY = characterY;
            _configuration.TitleBackgroundCharacterPositionZ = characterZ;
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }
        ImGui.TextDisabled("Character は将来の配置/preset補助用です。Focus の代用には使いません。");

        if (ImGui.Button("カメラ値を適用"))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCameraX = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraX);
            _configuration.TitleBackgroundCameraY = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraY);
            _configuration.TitleBackgroundCameraZ = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraZ);
            _configuration.TitleBackgroundFocusX = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundFocusX);
            _configuration.TitleBackgroundFocusY = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundFocusY);
            _configuration.TitleBackgroundFocusZ = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundFocusZ);
            _configuration.TitleBackgroundFovY = TitleBackgroundPreset.ClampFovY(_configuration.TitleBackgroundFovY);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }
    }

    private void DrawTitleBackgroundCharacterSelectDeliveryModes()
    {
        var backgroundMode = _configuration.TitleBackgroundCharacterSelectBackgroundMode;
        var lightingMode = _configuration.TitleBackgroundCharacterSelectLightingMode;
        var changed = false;

        if (ImGui.BeginCombo("背景の扱い##TitleBackgroundCharacterSelectBackgroundMode", GetTitleBackgroundCharacterSelectBackgroundModeLabel(backgroundMode)))
        {
            foreach (TitleBackgroundCharacterSelectBackgroundMode candidate in Enum.GetValues(typeof(TitleBackgroundCharacterSelectBackgroundMode)))
            {
                if (ImGui.Selectable(GetTitleBackgroundCharacterSelectBackgroundModeLabel(candidate), backgroundMode == candidate))
                {
                    backgroundMode = candidate;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(TitleBackgroundQuickCheckUiPresenter.GetBackgroundModeTooltip(backgroundMode));
        }

        if (ImGui.BeginCombo("明るさの扱い##TitleBackgroundCharacterSelectLightingMode", GetTitleBackgroundCharacterSelectLightingModeLabel(lightingMode)))
        {
            foreach (TitleBackgroundCharacterSelectLightingMode candidate in Enum.GetValues(typeof(TitleBackgroundCharacterSelectLightingMode)))
            {
                if (ImGui.Selectable(GetTitleBackgroundCharacterSelectLightingModeLabel(candidate), lightingMode == candidate))
                {
                    lightingMode = candidate;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextWrapped("Character Select は background-only MVP です。custom override は selectedPresetId=none として扱い、キャラクター本体は表示されない想定です。");

        if (!changed)
        {
            return;
        }

        _configuration.TitleBackgroundCharacterSelectBackgroundMode = backgroundMode;
        _configuration.TitleBackgroundCharacterSelectLightingMode = lightingMode;
        _configuration.Save();
        _titleScreenBackgroundService.ApplyFromConfiguration();
    }

    private void DrawTitleBackgroundOverrideCandidateSelector(bool showManualSlot)
    {
        var manualSlots = BuildTitleBackgroundManualCandidateSlots();
        var availableCandidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates(manualSlots);
        var selectedCandidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
            _configuration.TitleBackgroundTerritoryPath,
            _configuration.TitleBackgroundTerritoryTypeId,
            _configuration.TitleBackgroundLayoutLayerFilterKey,
            availableCandidates);
        var selectedLabel = GetTitleBackgroundOverrideCandidateLabel(selectedCandidate);

        if (ImGui.BeginCombo("背景候補##TitleBackgroundOverrideCandidate", selectedLabel))
        {
            foreach (var candidate in availableCandidates)
            {
                if (ImGui.Selectable(GetTitleBackgroundOverrideCandidateLabel(candidate), selectedCandidate.Id == candidate.Id))
                {
                    ApplyTitleBackgroundOverrideCandidate(candidate);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextWrapped("背景のみモードではロビーシーン全体を差し替えます。選択キャラクター本体は表示されない想定です。");
        if (showManualSlot)
        {
            DrawTitleBackgroundManualCandidateSlot1(manualSlots[0]);
        }
    }

    private void DrawTitleBackgroundEffectiveCandidateDetails()
    {
        var manualSlots = BuildTitleBackgroundManualCandidateSlots();
        var candidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
            _configuration.TitleBackgroundTerritoryPath,
            _configuration.TitleBackgroundTerritoryTypeId,
            _configuration.TitleBackgroundLayoutLayerFilterKey,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates(manualSlots));

        ImGui.Text("Current effective candidate");
        ImGui.TextWrapped(GetTitleBackgroundOverrideCandidateLabel(candidate));
        ImGui.TextDisabled($"Path: {candidate.TerritoryPath}");
        ImGui.TextDisabled($"TerritoryId: {candidate.TerritoryId} / LayerFilterKey: {candidate.LayerFilterKey}");
        ImGui.TextDisabled($"Source: {candidate.Source} / Compatibility: {candidate.ExpectedCompatibility}");
    }

    private void ApplyTitleBackgroundOverrideCandidate(TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        _configuration.TitleBackgroundSelectedPresetId = string.Empty;
        _titleBackgroundPendingPresetId = string.Empty;
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(_configuration, candidate);
        _configuration.Save();
        _titleScreenBackgroundService.ApplyFromConfiguration();
    }

    private IReadOnlyList<TitleBackgroundCharacterSelectManualCandidateSlot> BuildTitleBackgroundManualCandidateSlots()
    {
        return
        [
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildManualSlot(
                1,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1Enabled,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1DisplayName,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryPath,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryId,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness),
        ];
    }

    private void DrawTitleBackgroundManualCandidateSlot1(TitleBackgroundCharacterSelectManualCandidateSlot slot)
    {
        if (!ImGui.TreeNode("Manual candidate slot 1##TitleBackgroundManualCandidate1"))
        {
            return;
        }

        var changed = false;
        var enabled = _configuration.TitleBackgroundCharacterSelectManualCandidate1Enabled;
        if (ImGui.Checkbox("Enable manual candidate slot 1##TitleBackgroundManualCandidate1Enabled", ref enabled))
        {
            _configuration.TitleBackgroundCharacterSelectManualCandidate1Enabled = enabled;
            changed = true;
        }

        var displayName = _configuration.TitleBackgroundCharacterSelectManualCandidate1DisplayName;
        if (ImGui.InputTextWithHint("Display name##TitleBackgroundManualCandidate1DisplayName", "Manual candidate slot 1", ref displayName, 128))
        {
            _configuration.TitleBackgroundCharacterSelectManualCandidate1DisplayName = displayName.Trim();
            changed = true;
        }

        var territoryPath = _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryPath;
        if (ImGui.InputTextWithHint("Territory path##TitleBackgroundManualCandidate1TerritoryPath", "ex5/.../level/...", ref territoryPath, 256))
        {
            _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(territoryPath);
            changed = true;
        }

        var territoryId = (int)_configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryId;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("Territory id##TitleBackgroundManualCandidate1TerritoryId", ref territoryId))
        {
            _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryId = (uint)Math.Clamp(territoryId, 0, int.MaxValue);
            changed = true;
        }

        var layerFilterKey = (int)_configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("Layer filter key##TitleBackgroundManualCandidate1LayerFilterKey", ref layerFilterKey))
        {
            _configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey = (uint)Math.Clamp(layerFilterKey, 0, int.MaxValue);
            changed = true;
        }

        var expectedBrightness = _configuration.TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness;
        if (ImGui.BeginCombo("Expected brightness##TitleBackgroundManualCandidate1ExpectedBrightness", expectedBrightness.ToString()))
        {
            foreach (TitleBackgroundCharacterSelectExpectedBrightness candidate in Enum.GetValues(typeof(TitleBackgroundCharacterSelectExpectedBrightness)))
            {
                if (ImGui.Selectable(candidate.ToString(), expectedBrightness == candidate))
                {
                    _configuration.TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness = candidate;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextDisabled($"Manual slot status: {(slot.Valid ? "valid" : slot.ValidationError)}");

        if (changed)
        {
            var updatedSlot = BuildTitleBackgroundManualCandidateSlots()[0];
            if (_configuration.TitleBackgroundCharacterSelectOverrideCandidateId == TitleBackgroundCharacterSelectOverrideCandidateRegistry.ManualSlot1CandidateId
                && TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryCreateManualCandidate(updatedSlot, out var candidate))
            {
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(_configuration, candidate);
            }

            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        ImGui.TreePop();
    }

    private void DrawTitleBackgroundCaptureResult()
    {
        var result = _titleScreenBackgroundService.LastCameraCaptureResult;
        if (!result.HasRun)
        {
            ImGui.TextDisabled("最後の保存結果: なし");
            return;
        }

        var color = result.Success
            ? new Vector4(0.3f, 0.8f, 0.45f, 1f)
            : new Vector4(1f, 0.45f, 0.45f, 1f);
        ImGui.TextColored(color, result.Success ? "最後の保存結果: 成功" : $"最後の保存結果: 失敗 - {result.FailureReason}");

        foreach (var message in result.Messages.Take(10))
        {
            ImGui.TextDisabled(message);
        }
    }

    private void DrawTitleBackgroundAdvancedSettings()
    {
        if (!ImGui.CollapsingHeader("native 診断"))
        {
            return;
        }

        var territoryTypeId = (int)_configuration.TitleBackgroundTerritoryTypeId;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("TerritoryTypeId##TitleBackgroundTerritoryTypeId", ref territoryTypeId))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
            _configuration.TitleBackgroundTerritoryTypeId = (uint)Math.Clamp(territoryTypeId, 0, int.MaxValue);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var layoutTerritoryTypeId = (int)_configuration.TitleBackgroundLayoutTerritoryTypeId;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("LayoutTerritoryTypeId##TitleBackgroundLayoutTerritoryTypeId", ref layoutTerritoryTypeId))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
            _configuration.TitleBackgroundLayoutTerritoryTypeId = (uint)Math.Clamp(layoutTerritoryTypeId, 0, int.MaxValue);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var layerFilterKey = (int)_configuration.TitleBackgroundLayoutLayerFilterKey;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("LayoutLayerFilterKey##TitleBackgroundLayoutLayerFilterKey", ref layerFilterKey))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
            _configuration.TitleBackgroundLayoutLayerFilterKey = (uint)Math.Clamp(layerFilterKey, 0, int.MaxValue);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        ImGui.Spacing();
        if (ImGui.Button("最後に観測した scene を override 値へコピー（検証用）"))
        {
            if (_titleScreenBackgroundService.TryCopyLastObservedCreateSceneToOverrideConfiguration(out var errorMessage))
            {
                _titleBackgroundPendingPresetId = string.Empty;
                _titleBackgroundSceneCopyMessage = "最後に観測した scene を override 値へコピーしました。";
                _titleBackgroundSceneCopyMessageColor = new Vector4(0.3f, 0.8f, 0.45f, 1f);
            }
            else
            {
                _titleBackgroundSceneCopyMessage = $"scene コピー失敗: {errorMessage}";
                _titleBackgroundSceneCopyMessageColor = new Vector4(1f, 0.45f, 0.45f, 1f);
            }
        }

        ImGui.TextDisabled("同じ scene を再指定する smoke test 用です。見た目の変化は想定しません。");
        ImGui.TextDisabled("CharaSelectOnly smoke ではカメラ調整を無効のままにしてください。コピー時にも Camera override は OFF にします。");
        if (!string.IsNullOrWhiteSpace(_titleBackgroundSceneCopyMessage))
        {
            ImGui.TextColored(_titleBackgroundSceneCopyMessageColor, _titleBackgroundSceneCopyMessage);
        }

        ImGui.Spacing();
        ImGui.Text("native signature");
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
            _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature = string.Empty;
            _configuration.TitleBackgroundSetCameraCurveMidPointSignature = string.Empty;
            _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = string.Empty;
            _configuration.Save();
            _titleScreenBackgroundService.ReloadNativeIntegration();
        }

        ImGui.TextDisabled("BGM / 天候 / 時刻は後続接続用の設定枠です。今回のカメラ保存には使いません。");
    }

    private void DrawTitleBackgroundSignatureInputs()
    {
        var createSceneSignature = _configuration.TitleBackgroundCreateSceneSignature;
        var fixOnSignature = _configuration.TitleBackgroundFixOnSignature;
        var lobbyUpdateSignature = _configuration.TitleBackgroundLobbyUpdateSignature;
        var loadLobbySceneSignature = _configuration.TitleBackgroundLoadLobbySceneSignature;
        var lobbyCurrentMapSignature = _configuration.TitleBackgroundLobbyCurrentMapSignature;
        var calculateLobbyCameraLookAtYSignature = _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature;
        var setCameraCurveMidPointSignature = _configuration.TitleBackgroundSetCameraCurveMidPointSignature;
        var calculateCameraCurveLowAndHighPointSignature = _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature;
        var changed = DrawTitleBackgroundSignatureInput("CreateScene", ref createSceneSignature);
        changed |= DrawTitleBackgroundSignatureInput("FixOn", ref fixOnSignature);
        changed |= DrawTitleBackgroundSignatureInput("LobbyUpdate", ref lobbyUpdateSignature);
        changed |= DrawTitleBackgroundSignatureInput("LoadLobbyScene", ref loadLobbySceneSignature);
        changed |= DrawTitleBackgroundSignatureInput("LobbyCurrentMap", ref lobbyCurrentMapSignature);
        changed |= DrawTitleBackgroundSignatureInput("CalculateLobbyCameraLookAtY", ref calculateLobbyCameraLookAtYSignature);
        changed |= DrawTitleBackgroundSignatureInput("SetCameraCurveMidPoint", ref setCameraCurveMidPointSignature);
        changed |= DrawTitleBackgroundSignatureInput("CalculateCameraCurveLowAndHighPoint", ref calculateCameraCurveLowAndHighPointSignature);

        if (!changed)
        {
            return;
        }

        _configuration.TitleBackgroundCreateSceneSignature = createSceneSignature.Trim();
        _configuration.TitleBackgroundFixOnSignature = fixOnSignature.Trim();
        _configuration.TitleBackgroundLobbyUpdateSignature = lobbyUpdateSignature.Trim();
        _configuration.TitleBackgroundLoadLobbySceneSignature = loadLobbySceneSignature.Trim();
        _configuration.TitleBackgroundLobbyCurrentMapSignature = lobbyCurrentMapSignature.Trim();
        _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature = calculateLobbyCameraLookAtYSignature.Trim();
        _configuration.TitleBackgroundSetCameraCurveMidPointSignature = setCameraCurveMidPointSignature.Trim();
        _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = calculateCameraCurveLowAndHighPointSignature.Trim();
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
        _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature = (_configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundSetCameraCurveMidPointSignature = (_configuration.TitleBackgroundSetCameraCurveMidPointSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = (_configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature ?? string.Empty).Trim();
    }

    private void ClearTitleBackgroundSelectedPreset()
    {
        if (string.IsNullOrEmpty(_configuration.TitleBackgroundSelectedPresetId)
            && string.IsNullOrEmpty(_titleBackgroundPendingPresetId))
        {
            return;
        }

        _configuration.TitleBackgroundSelectedPresetId = string.Empty;
        _titleBackgroundPendingPresetId = string.Empty;
        _titleBackgroundPresetMessage = "手入力により preset 選択を解除しました。";
        _titleBackgroundPresetMessageColor = new Vector4(1f, 0.75f, 0.35f, 1f);
    }

    private string GetSceneCompositionVerdictLabel()
    {
        if (!_configuration.CharaSelectSceneCompositionEnabled)
        {
            return "無効: 撮影構成はまだ使いません";
        }

        if (_configuration.TitleBackgroundOverrideEnabled)
        {
            return "要確認: 背景だけ実験と競合しています";
        }

        if (_configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && _configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.Yes
            && _configuration.LastSceneProfileEmotePlayedResult == CharaSelectSceneBinaryResult.Yes)
        {
            return "OK: キャラ・場所・エモートを確認済み";
        }

        if (_configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.No)
        {
            return "Blocked: キャラ本体が見えていません";
        }

        if (_configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && _configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.No)
        {
            return "Blocked: 場所が変わっていません";
        }

        return "未確認: SS判定を入力してください";
    }

    private Vector4 GetSceneCompositionVerdictColor()
    {
        if (!_configuration.CharaSelectSceneCompositionEnabled)
        {
            return new Vector4(0.7f, 0.7f, 0.7f, 1f);
        }

        if (_configuration.TitleBackgroundOverrideEnabled
            || _configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.No
            || (_configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
                && _configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.No))
        {
            return new Vector4(1f, 0.45f, 0.45f, 1f);
        }

        if (_configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && _configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.Yes
            && _configuration.LastSceneProfileEmotePlayedResult == CharaSelectSceneBinaryResult.Yes)
        {
            return new Vector4(0.3f, 0.8f, 0.45f, 1f);
        }

        return new Vector4(1f, 0.75f, 0.35f, 1f);
    }

    private static string GetSceneNextActionLabel(string nextAction)
    {
        return nextAction switch
        {
            "enable-scene-composition-and-select-profile" => "撮影構成を有効化し、profile を選びます",
            "disable-title-background-route-and-verify-foreground" => "背景だけ実験を無効化し、キャラ本体が残るか確認します",
            "inspect-character-visibility-route" => "キャラ本体が消える原因を診断します",
            "discover-visible-stage-source" => "キャラ本体を残したまま場所を変える source を探します",
            "fix-emote-replay-route" => "保存エモートの再生ルートを確認します",
            "implement-one-shot-placement" => "一回だけの立ち位置適用へ進めます",
            "verify-with-screenshot-and-set-manual-results" => "SSで確認し、下の判定を入力します",
            "verify-character-visible-background-and-emote-with-screenshot" => "SSでキャラ・場所・エモートを確認します",
            _ => nextAction,
        };
    }

    private static bool IsStatusError(string statusText)
    {
        return statusText.Contains("失敗", StringComparison.Ordinal)
            || statusText.Contains("エラー", StringComparison.Ordinal)
            || statusText.Contains("見つかりません", StringComparison.Ordinal)
            || statusText.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("error", StringComparison.OrdinalIgnoreCase);
    }

    private static string BoolToVisibleLabel(bool value)
    {
        return value ? "表示される想定" : "表示されない想定";
    }

    private static string GetTitleBackgroundRuntimeModeLabel(TitleBackgroundRuntimeMode mode)
    {
        return mode switch
        {
            TitleBackgroundRuntimeMode.ResolveOnly => "準備だけ（address解決）",
            TitleBackgroundRuntimeMode.Disabled => "無効",
            TitleBackgroundRuntimeMode.HookProbe => "診断だけ（見た目変更なし）",
            TitleBackgroundRuntimeMode.CharaSelectOnly => "キャラ選択だけ",
            TitleBackgroundRuntimeMode.TitleAndCharaSelect => "タイトル+キャラ選択",
            _ => mode.ToString(),
        };
    }

    private static string GetTitleBackgroundCharacterSelectBackgroundModeLabel(TitleBackgroundCharacterSelectBackgroundMode mode)
    {
        return TitleBackgroundQuickCheckUiPresenter.GetBackgroundModeUiLabel(mode);
    }

    private static string GetTitleBackgroundCharacterSelectLightingModeLabel(TitleBackgroundCharacterSelectLightingMode mode)
    {
        return mode switch
        {
            TitleBackgroundCharacterSelectLightingMode.Default => "既定",
            TitleBackgroundCharacterSelectLightingMode.DiagnosticsOnly => "診断のみ",
            TitleBackgroundCharacterSelectLightingMode.PreferBrightPreset => "明るい候補推奨",
            TitleBackgroundCharacterSelectLightingMode.PreferBrightLayer => "明るいレイヤー推奨",
            TitleBackgroundCharacterSelectLightingMode.EnvironmentOverrideExperimental => "環境 override（実験）",
            TitleBackgroundCharacterSelectLightingMode.DisableDarkeningExperimental => "暗転抑制（実験）",
            _ => mode.ToString(),
        };
    }

    private static bool DrawSceneBinaryResultCombo(
        string label,
        ref CharaSelectSceneBinaryResult result,
        string yesLabel,
        string noLabel)
    {
        var changed = false;
        if (ImGui.BeginCombo(label, GetSceneBinaryResultLabel(result, yesLabel, noLabel)))
        {
            foreach (CharaSelectSceneBinaryResult candidate in Enum.GetValues(typeof(CharaSelectSceneBinaryResult)))
            {
                if (ImGui.Selectable(GetSceneBinaryResultLabel(candidate, yesLabel, noLabel), result == candidate))
                {
                    result = candidate;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private static bool DrawSceneBrightnessResultCombo(string label, ref CharaSelectSceneBrightnessResult result)
    {
        var changed = false;
        if (ImGui.BeginCombo(label, GetSceneBrightnessResultLabel(result)))
        {
            foreach (CharaSelectSceneBrightnessResult candidate in Enum.GetValues(typeof(CharaSelectSceneBrightnessResult)))
            {
                if (ImGui.Selectable(GetSceneBrightnessResultLabel(candidate), result == candidate))
                {
                    result = candidate;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private static string GetSceneBinaryResultLabel(
        CharaSelectSceneBinaryResult result,
        string yesLabel,
        string noLabel)
    {
        return result switch
        {
            CharaSelectSceneBinaryResult.Unknown => "未確認",
            CharaSelectSceneBinaryResult.Yes => yesLabel,
            CharaSelectSceneBinaryResult.No => noLabel,
            _ => "未確認",
        };
    }

    private static string GetSceneBrightnessResultLabel(CharaSelectSceneBrightnessResult result)
    {
        return result switch
        {
            CharaSelectSceneBrightnessResult.Unknown => "未確認",
            CharaSelectSceneBrightnessResult.Dark => "暗い",
            CharaSelectSceneBrightnessResult.Acceptable => "許容",
            CharaSelectSceneBrightnessResult.Bright => "明るい",
            _ => "未確認",
        };
    }

    private static string GetStageStrategyLabel(CharaSelectStageStrategy strategy)
    {
        return strategy switch
        {
            CharaSelectStageStrategy.Disabled => "無効",
            CharaSelectStageStrategy.ObserveOnly => "診断のみ",
            CharaSelectStageStrategy.ClientSelectDataTerritoryPatch => "現在の安全 route",
            CharaSelectStageStrategy.LobbyPositionPatch => "Lobby position 実験",
            CharaSelectStageStrategy.LobbySheetResolvedPatch => "Lobby sheet 実験",
            CharaSelectStageStrategy.LayoutPrefetchOnly => "Layout prefetch のみ",
            CharaSelectStageStrategy.TitleBackgroundFullSceneFallback => "背景だけ fallback",
            _ => strategy.ToString(),
        };
    }

    private static string GetStageStrategyDescription(CharaSelectStageStrategy strategy)
    {
        return strategy switch
        {
            CharaSelectStageStrategy.ObserveOnly => "見た目は変えず、Character Select 中の stage source 候補だけを記録します。",
            CharaSelectStageStrategy.ClientSelectDataTerritoryPatch => "既存の ClientSelectData TerritoryTypeId 差し替えを使います。SSで場所が default のままなら source 未解決として扱います。",
            CharaSelectStageStrategy.TitleBackgroundFullSceneFallback => "背景だけ差し替え実験です。最終目的 route ではなく、キャラ本体は消える想定です。",
            CharaSelectStageStrategy.Disabled => "stage strategy を使いません。",
            _ => "現時点では安全な foreground-preserving 実装が未確定です。/xmucdiag で unavailable として出します。",
        };
    }

    private static string GetTitleBackgroundOverrideCandidateLabel(TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        return TitleBackgroundQuickCheckUiPresenter.BuildCandidateLabel(candidate);
    }

    private static string GetTitleBackgroundSettingsDisplayModeLabel(TitleBackgroundSettingsDisplayMode mode)
    {
        return mode switch
        {
            TitleBackgroundSettingsDisplayMode.Simple => "Simple",
            TitleBackgroundSettingsDisplayMode.Advanced => "Advanced",
            TitleBackgroundSettingsDisplayMode.DeveloperDiagnostics => "Developer Diagnostics",
            _ => mode.ToString(),
        };
    }

    private static string GetTitleBackgroundCameraFramingModeLabel(TitleBackgroundCharaSelectCameraFramingMode mode)
    {
        return mode switch
        {
            TitleBackgroundCharaSelectCameraFramingMode.Default => "Default",
            TitleBackgroundCharaSelectCameraFramingMode.LowerCamera => "Lower camera",
            TitleBackgroundCharaSelectCameraFramingMode.CenterCharacter => "Center character",
            TitleBackgroundCharaSelectCameraFramingMode.CloserCharacter => "Closer character",
            TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended => "n4f4 experimental",
            TitleBackgroundCharaSelectCameraFramingMode.CustomExperimental => "Custom experimental",
            _ => mode.ToString(),
        };
    }

    private static string GetTitleBackgroundCharacterVisualStatusLabel(TitleBackgroundCharacterVisualStatus status)
    {
        return status switch
        {
            TitleBackgroundCharacterVisualStatus.Unknown => "Unknown",
            TitleBackgroundCharacterVisualStatus.Visible => "Visible",
            TitleBackgroundCharacterVisualStatus.VisibleButTooSmall => "Too small",
            TitleBackgroundCharacterVisualStatus.VisibleTopDown => "Top-down",
            TitleBackgroundCharacterVisualStatus.NotVisible => "Not visible",
            TitleBackgroundCharacterVisualStatus.Offscreen => "Offscreen",
            _ => status.ToString(),
        };
    }

    private void DrawTitleBackgroundCameraFramingSelector()
    {
        var framingMode = _configuration.TitleBackgroundCharaSelectCameraFramingMode;
        if (ImGui.BeginCombo("Camera framing##TitleBackgroundCameraFraming", GetTitleBackgroundCameraFramingModeLabel(framingMode)))
        {
            foreach (TitleBackgroundCharaSelectCameraFramingMode mode in Enum.GetValues(typeof(TitleBackgroundCharaSelectCameraFramingMode)))
            {
                if (ImGui.Selectable(GetTitleBackgroundCameraFramingModeLabel(mode), framingMode == mode))
                {
                    _configuration.TitleBackgroundCharaSelectCameraFramingMode = mode;
                    _configuration.Save();
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawTitleBackgroundCharacterVisualStatusSelector()
    {
        var visualStatus = _configuration.TitleBackgroundCharacterVisualStatus;
        if (ImGui.BeginCombo("Character visual status##TitleBackgroundCharacterVisualStatus", GetTitleBackgroundCharacterVisualStatusLabel(visualStatus)))
        {
            foreach (TitleBackgroundCharacterVisualStatus status in Enum.GetValues(typeof(TitleBackgroundCharacterVisualStatus)))
            {
                if (ImGui.Selectable(GetTitleBackgroundCharacterVisualStatusLabel(status), visualStatus == status))
                {
                    _configuration.TitleBackgroundCharacterVisualStatus = status;
                    _configuration.Save();
                }
            }

            ImGui.EndCombo();
        }
        ImGui.TextDisabled("Manually record what you see in Character Select.");
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
        _configuration.TitleBackgroundSelectedPresetId = string.Empty;
        _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1Enabled = false;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1DisplayName = string.Empty;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryPath = string.Empty;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryId = 0;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey = 0;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness = TitleBackgroundCharacterSelectExpectedBrightness.Unknown;
        _titleBackgroundPendingPresetId = string.Empty;
        _configuration.TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.ResolveOnly;
        _configuration.TitleBackgroundCharacterSelectBackgroundMode = TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly;
        _configuration.TitleBackgroundCharacterSelectLightingMode = TitleBackgroundCharacterSelectLightingMode.Default;
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
        _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature = string.Empty;
        _configuration.TitleBackgroundSetCameraCurveMidPointSignature = string.Empty;
        _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = string.Empty;
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
