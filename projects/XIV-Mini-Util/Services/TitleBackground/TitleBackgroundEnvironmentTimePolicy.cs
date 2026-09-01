// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundEnvironmentTimePolicy.cs
// Description: 背景セッション中に EnvManager へ書き込むエオルゼア時刻を candidate ごとに解決する。
// Reason: 既定は正午固定（Il Mheg / Elpis で実績あり）。FRU は時刻のみの単独試行として
//         13:00 を使う。過去の正午の白飛びは時刻単独の因果確認ではないため、時刻 sweep は行わない。
//         TitleEdit（RokasKil/TitleEdit）の time semantics は preset の TimeOffset を
//         `hour*100 + minute` の ushort で保持し、native time setter へ渡す。旧FRU preset の
//         time=15:17 は historical reference（TimeOffset=1517）として扱う。MiniUtil は既存の実績ある
//         `EnvManager.DayTimeSeconds`（エオルゼア日の秒, 0..86400）へ書くため、同じ時計時刻を
//         `hour*3600 + minute*60` へ換算する（13:00 -> 46800 秒）。新規 native hook は追加しない。
namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundEnvironmentTimePolicy
{
    // エオルゼア時間の正午（既定）。
    public const float NoonDayTimeSeconds = TitleBackgroundEnvironmentNoonWriter.NoonDayTimeSeconds;

    // FRU クリア後ステージの時刻。13:00 の単独 production trial を DayTimeSeconds へ換算した値。
    public const int FruClockHour = 13;
    public const int FruClockMinute = 0;
    public const float FruDayTimeSeconds = (FruClockHour * 3600) + (FruClockMinute * 60);

    // candidate id から書き込むべき DayTimeSeconds を返す。FRU 以外は既存の正午固定を維持する。
    public static float ResolveDayTimeSeconds(string? candidateId)
    {
        return string.Equals(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(candidateId),
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
            StringComparison.Ordinal)
            ? FruDayTimeSeconds
            : NoonDayTimeSeconds;
    }

    // 診断・テスト用: 適用中の時刻ポリシー名。
    public static string ResolvePolicyName(string? candidateId)
    {
        return string.Equals(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(candidateId),
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
            StringComparison.Ordinal)
            ? "fru-clock-13-00"
            : "noon";
    }
}
