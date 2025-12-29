// Path: projects/XIV-Mini-Util/Windows/MainWindow.cs
// Description: メイン操作UIを提供しサービスの状態を制御する
// Reason: ユーザーがゲーム内で機能を操作できるようにするため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/MateriaExtractService.cs, projects/XIV-Mini-Util/Services/DesynthService.cs, projects/XIV-Mini-Util/Windows/ConfigWindow.cs
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Numerics;
using XivMiniUtil;
using XivMiniUtil.Services;

namespace XivMiniUtil.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Configuration _configuration;
    private readonly MateriaExtractService _materiaService;
    private readonly DesynthService _desynthService;

    private DesynthWarningInfo? _warningInfo;
    private bool _showWarningDialog;
    private string? _lastResultMessage;

    public MainWindow(
        Configuration configuration,
        MateriaExtractService materiaService,
        DesynthService desynthService)
        : base("XIV Mini Util")
    {
        _configuration = configuration;
        _materiaService = materiaService;
        _desynthService = desynthService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 360),
            MaximumSize = new Vector2(900, 700),
        };

        _desynthService.OnWarningRequired += ShowWarningDialog;
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
    }

    public override void Draw()
    {
        DrawMateriaSection();
        ImGui.Separator();
        DrawDesynthSection();
        DrawWarningDialog();
        DrawResult();
    }

    public void Dispose()
    {
        _desynthService.OnWarningRequired -= ShowWarningDialog;
    }

    public void ShowWarningDialog(DesynthWarningInfo info)
    {
        _warningInfo = info;
        _showWarningDialog = true;
    }

    private void DrawMateriaSection()
    {
        ImGui.Text("マテリア精製");
        var enabled = _materiaService.IsEnabled;
        if (ImGui.Checkbox("有効", ref enabled))
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
        ImGui.Text(_materiaService.IsProcessing ? "処理中" : "待機中");
    }

    private void DrawDesynthSection()
    {
        ImGui.Text("アイテム分解");

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

        if (_desynthService.IsProcessing)
        {
            if (ImGui.Button("分解停止"))
            {
                _desynthService.Stop();
            }
        }
        else
        {
            if (ImGui.Button("分解開始"))
            {
                _ = StartDesynthAsync();
            }
        }
    }

    private void DrawWarningDialog()
    {
        if (_showWarningDialog)
        {
            ImGui.OpenPopup("分解警告");
            _showWarningDialog = false;
        }

        if (ImGui.BeginPopupModal("分解警告", ImGuiWindowFlags.AlwaysAutoResize))
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
            !_configuration.DesynthWarningEnabled);

        var result = await _desynthService.StartDesynthAsync(options);
        _lastResultMessage = $"分解結果: 成功 {result.ProcessedCount} / スキップ {result.SkippedCount}";
        if (result.Errors.Count > 0)
        {
            _lastResultMessage += $" / エラー {result.Errors.Count}";
        }
    }
}
