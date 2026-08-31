// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundEnvironmentNoonRuntimeState.cs
// Description: 背景セッション中の環境正午上書きの診断状態（config非保存）を保持する
// Reason: 適用結果をセッション限定の診断値として追跡し、report builder等から参照できるようにするため
namespace XivMiniUtil.Services.TitleBackground;

// 背景セッション限定の環境正午上書き診断状態（セッションを跨いで永続化しない）。
internal sealed class TitleBackgroundEnvironmentNoonRuntimeState
{
    public int AppliedFrameCount { get; set; }

    public string LastStatus { get; set; } = "not-applied";

    // candidate-specific 時刻ポリシー（FRU のみ非正午）の診断。
    public string LastPolicyName { get; set; } = "noon";

    public float LastRequestedDayTimeSeconds { get; set; } = TitleBackgroundEnvironmentNoonWriter.NoonDayTimeSeconds;

    // pre-login（背景セッション中・未ログイン）でのみ採取した EnvManager.DayTimeSeconds の readback。
    public float? LastPreLoginDayTimeSecondsReadback { get; set; }

    // OneClick run 開始時に呼ぶ。前 run の pre-login snapshot / policy を次 run へ持ち越さない
    // （AppliedFrameCount / LastStatus 等の lifetime 値は既存挙動のまま触らない）。
    public void ResetRunScopedSnapshot()
    {
        LastPolicyName = "noon";
        LastRequestedDayTimeSeconds = TitleBackgroundEnvironmentNoonWriter.NoonDayTimeSeconds;
        LastPreLoginDayTimeSecondsReadback = null;
    }
}
