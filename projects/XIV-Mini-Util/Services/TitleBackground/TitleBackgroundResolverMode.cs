// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundResolverMode.cs
// Description: タイトル背景native addressの解決候補をhook targetへ昇格する条件
// Reason: DirectText候補を通常実行へ自動昇格せず、明示probeだけで段階検証するため
namespace XivMiniUtil.Services.TitleBackground;

public enum TitleBackgroundResolverMode
{
    AutoDiagnosticOnly = 0,
    ManualDirectTextProbe = 1,
}
