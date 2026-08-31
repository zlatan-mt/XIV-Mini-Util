// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundEnvironmentClearSkyRuntimeState.cs
// Description: 背景セッション中の環境天候（晴れ）上書きの診断状態（config非保存）を保持する
// Reason: 適用結果をセッション限定の診断値として追跡し、report builder等から参照できるようにするため
namespace XivMiniUtil.Services.TitleBackground;

// 背景セッション限定の環境天候（晴れ）上書き診断状態（セッションを跨いで永続化しない）。
internal sealed class TitleBackgroundEnvironmentClearSkyRuntimeState
{
    public int AppliedFrameCount { get; set; }

    public string LastStatus { get; set; } = "not-applied";

    public string LastCandidateId { get; set; } = "none";

    public byte? LastRequestedWeatherId { get; set; }

    public byte? LastAppliedWeatherId { get; set; }

    // pre-login（背景セッション中・未ログイン）でのみ採取した EnvManager.ActiveWeather の readback。
    // post-login / current-world の天候（report 生成時の live 読取）と混同しないための凍結値。
    public byte? LastPreLoginWeatherReadback { get; set; }

    public bool PreLoginSnapshotCaptured { get; set; }

    // OneClick run 開始時に呼ぶ。前 run の pre-login snapshot を次 run へ持ち越さない
    // （AppliedFrameCount / LastStatus 等の lifetime 値は既存挙動のまま触らない）。
    public void ResetRunScopedSnapshot()
    {
        LastCandidateId = "none";
        LastRequestedWeatherId = null;
        LastAppliedWeatherId = null;
        LastPreLoginWeatherReadback = null;
        PreLoginSnapshotCaptured = false;
    }
}
