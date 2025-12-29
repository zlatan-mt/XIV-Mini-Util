// Path: projects/XIV-Mini-Util/Configuration.cs
// Description: プラグイン設定の保存と読み込みを管理する
// Reason: 再起動後もユーザー設定を維持するため
// RELEVANT FILES: projects/XIV-Mini-Util/Plugin.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs, projects/XIV-Mini-Util/Windows/ConfigWindow.cs
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace XivMiniUtil;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // マテリア精製設定
    public bool MateriaExtractEnabled { get; set; } = false;

    // アイテム分解設定
    public int DesynthMinLevel { get; set; } = 1;
    public int DesynthMaxLevel { get; set; } = 999;
    public JobCondition DesynthJobCondition { get; set; } = JobCondition.Any;
    public bool DesynthWarningEnabled { get; set; } = true;
    public int DesynthWarningThreshold { get; set; } = 100;

    private IDalamudPluginInterface? _pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
    }

    public void Save()
    {
        // 設定変更時は即時保存する
        _pluginInterface?.SavePluginConfig(this);
    }
}
