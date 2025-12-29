// Path: projects/XIV-Mini-Util/Windows/ConfigWindow.cs
// Description: 詳細設定を編集するための設定UIを提供する
// Reason: メインウィンドウをシンプルに保ちながら設定を管理するため
// RELEVANT FILES: projects/XIV-Mini-Util/Configuration.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs, projects/XIV-Mini-Util/Plugin.cs
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Numerics;
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
    }

    public void Dispose()
    {
        // WindowSystemからの破棄時に備えた明示的なフック
    }
}
