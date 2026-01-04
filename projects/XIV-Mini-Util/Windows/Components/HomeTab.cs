// Path: projects/XIV-Mini-Util/Windows/Components/HomeTab.cs
// Description: ホームタブ（マテリア精製/分解）のUIを描画する
// Reason: MainWindowの責務を分割し可読性を高めるため
using Dalamud.Bindings.ImGui;
using System.Numerics;
using System.Threading.Tasks;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using XivMiniUtil.Services.Desynth;
using XivMiniUtil.Services.Materia;

namespace XivMiniUtil.Windows.Components;

public sealed class HomeTab : ITabComponent
{
    private readonly Configuration _configuration;
    private readonly MateriaExtractService _materiaService;
    private readonly DesynthService _desynthService;
    private readonly bool _materiaFeatureEnabled;
    private readonly bool _desynthFeatureEnabled;
    private string? _lastResultMessage;

    public HomeTab(
        Configuration configuration,
        MateriaExtractService materiaService,
        DesynthService desynthService,
        bool materiaFeatureEnabled,
        bool desynthFeatureEnabled)
    {
        _configuration = configuration;
        _materiaService = materiaService;
        _desynthService = desynthService;
        _materiaFeatureEnabled = materiaFeatureEnabled;
        _desynthFeatureEnabled = desynthFeatureEnabled;
    }

    public string? LastResultMessage => _lastResultMessage;

    public void Draw()
    {
        DrawMateriaSection();
        ImGui.Separator();
        DrawDesynthActionSection();
    }

    public void Dispose()
    {
    }

    private void DrawMateriaSection()
    {
        ImGui.Text("マテリア精製");
        if (!_materiaFeatureEnabled)
        {
            ImGui.Text("現在は無効中です。");
            ImGui.BeginDisabled();
        }

        var enabled = _materiaService.IsEnabled;
        if (ImGui.Checkbox("有効", ref enabled) && _materiaFeatureEnabled)
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
        ImGui.Text(_materiaFeatureEnabled
            ? (_materiaService.IsProcessing ? "処理中" : "待機中")
            : "無効中");

        if (!_materiaFeatureEnabled)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawDesynthActionSection()
    {
        ImGui.Text("アイテム分解");
        if (!_desynthFeatureEnabled)
        {
            ImGui.Text("現在は無効中です。");
            ImGui.BeginDisabled();
        }

        var isProcessing = _desynthService.IsProcessing;

        if (isProcessing)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("分解開始"))
        {
            if (_desynthFeatureEnabled)
            {
                _ = StartDesynthAsync();
            }
        }

        if (isProcessing)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        if (!isProcessing)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("分解停止"))
        {
            if (_desynthFeatureEnabled)
            {
                _desynthService.Stop();
            }
        }

        if (!isProcessing)
        {
            ImGui.EndDisabled();
        }

        if (!_desynthFeatureEnabled)
        {
            ImGui.EndDisabled();
        }
    }

    private async Task StartDesynthAsync()
    {
        var options = new DesynthOptions(
            _configuration.DesynthMinLevel,
            _configuration.DesynthMaxLevel,
            !_configuration.DesynthWarningEnabled,
            _configuration.DesynthTargetMode,
            _configuration.DesynthTargetCount);

        var result = await _desynthService.StartDesynthAsync(options);
        _lastResultMessage = $"分解結果: 成功 {result.ProcessedCount} / スキップ {result.SkippedCount}";
        if (result.Errors.Count > 0)
        {
            _lastResultMessage += $" / エラー {result.Errors.Count}";
        }
    }
}
