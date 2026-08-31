// Path: projects/XIV-Mini-Util/Windows/Components/SettingsTab.CharaSelect.cs
// Description: キャラクター選択画面のエモート設定UI
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace XivMiniUtil.Windows.Components;

public sealed partial class SettingsTab
{
    private void DrawCharaSelectEmoteSettings()
    {
        ImGui.Text("キャラ選択 エモート");
        ImGui.Separator();

        var emoteEnabled = _configuration.CharaSelectEmoteEnabled;
        if (ImGui.Checkbox("保存したエモートを再生する", ref emoteEnabled))
        {
            _charaSelectService.SetEmoteEnabled(emoteEnabled);
        }

        ImGui.TextDisabled($"現在: {_charaSelectService.GetCurrentSelectedEmoteDisplayName()}");
        DrawCharaSelectEmoteAdjustment(emoteEnabled);
    }

    private void DrawCharaSelectEmoteAdjustment(bool emoteEnabled)
    {
        if (!emoteEnabled)
        {
            ImGui.BeginDisabled();
        }

        ImGui.Text($"最後に記録: {_charaSelectService.GetLastRecordedEmoteDisplayName()}");
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
        else if (ImGui.Button("記録開始"))
        {
            _charaSelectService.StartRecordingEmote();
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
            ImGui.TextColored(
                new Vector4(1f, 0.8f, 0.2f, 1f),
                "記録中: 保存したいエモートを実行してください。");
        }

        if (!emoteEnabled)
        {
            ImGui.EndDisabled();
        }
    }
}
