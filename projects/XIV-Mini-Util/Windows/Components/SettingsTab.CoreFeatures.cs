// Path: projects/XIV-Mini-Util/Windows/Components/SettingsTab.CoreFeatures.cs
// Description: SettingsTab の core feature 設定 UI を描画する
// Reason: SettingsTab 本体をカテゴリ制御と設定入出力に絞るため
using Dalamud.Bindings.ImGui;
using System.Numerics;
using XivMiniUtil.Services.Desynth;

namespace XivMiniUtil.Windows.Components;

public sealed partial class SettingsTab
{
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
        ImGui.TextDisabled("チェックリストタブで各項目の時刻・通知先を個別設定できます。");

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
