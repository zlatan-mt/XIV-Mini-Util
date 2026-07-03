// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundEnvironmentNoonRuntimeState.cs
// Description: 背景セッション中の環境正午上書きの診断状態（config非保存）を保持する
// Reason: 適用結果をセッション限定の診断値として追跡し、report builder等から参照できるようにするため
namespace XivMiniUtil.Services.TitleBackground;

// 背景セッション限定の環境正午上書き診断状態（セッションを跨いで永続化しない）。
internal sealed class TitleBackgroundEnvironmentNoonRuntimeState
{
    public int AppliedFrameCount { get; set; }

    public string LastStatus { get; set; } = "not-applied";
}
