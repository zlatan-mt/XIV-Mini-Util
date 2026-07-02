// Path: projects/XIV-Mini-Util/Plugin.cs
// Description: Dalamudプラグインのエントリーポイントを定義する
// Reason: ライフサイクルの責務をpartialファイルへ分離するため

using Dalamud.Plugin;

namespace XivMiniUtil;

public sealed partial class Plugin : IDalamudPlugin
{
    public string Name => "XIV Mini Util";
}
