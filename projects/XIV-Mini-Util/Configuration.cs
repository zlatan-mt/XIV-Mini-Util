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
    public DesynthTargetMode DesynthTargetMode { get; set; } = DesynthTargetMode.All;
    public int DesynthTargetCount { get; set; } = 1;

    // 販売場所検索設定
    public List<uint> ShopSearchAreaPriority { get; set; } = new()
    {
        // デフォルト: 三大都市優先
        128,  // リムサ・ロミンサ：下甲板層
        129,  // リムサ・ロミンサ：上甲板層
        130,  // ウルダハ：ナル回廊
        131,  // ウルダハ：ザル回廊
        132,  // グリダニア：新市街
        133,  // グリダニア：旧市街
    };

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
