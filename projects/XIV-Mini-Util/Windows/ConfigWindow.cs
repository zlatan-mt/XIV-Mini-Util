// Path: projects/XIV-Mini-Util/Windows/ConfigWindow.cs
// Description: 詳細設定を編集するための設定UIを提供する
// Reason: メインウィンドウをシンプルに保ちながら設定を管理するため
// RELEVANT FILES: projects/XIV-Mini-Util/Configuration.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs, projects/XIV-Mini-Util/Plugin.cs
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using XivMiniUtil;

namespace XivMiniUtil.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _configuration;

    public ConfigWindow(Configuration configuration)
        : base("XIV Mini Util 設定")
    {
        _configuration = configuration;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 260),
            MaximumSize = new Vector2(800, 600),
        };
    }

    public override void Draw()
    {
        ImGui.Text("分解設定");

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

        ImGui.Separator();
        DrawShopSearchPriority();
    }

    public void Dispose()
    {
        // WindowSystemからの破棄時に備えた明示的なフック
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

    private void DrawShopSearchPriority()
    {
        ImGui.Text("販売場所検索");
        ImGui.Text("エリア優先度");

        var priorities = _configuration.ShopSearchAreaPriority;
        if (priorities.Count == 0)
        {
            ImGui.Text("優先度リストが空です。");
        }

        // リストの並び替えと編集を1パスで行う
        for (var i = 0; i < priorities.Count; i++)
        {
            ImGui.PushID(i);
            var value = (int)priorities[i];
            if (ImGui.InputInt("##PriorityId", ref value))
            {
                priorities[i] = (uint)Math.Max(0, value);
                _configuration.Save();
            }

            ImGui.SameLine();
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

        if (ImGui.Button("エリア追加"))
        {
            priorities.Add(0);
            _configuration.Save();
        }
    }
}
