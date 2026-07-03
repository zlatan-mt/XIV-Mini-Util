// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundEnvironmentNoonWriter.cs
// Description: 背景セッション中のみ EnvManager の時刻をエオルゼア正午へ unsafe 書込する
// Reason: ログイン画面（Title Background）が実時刻・天候をそのまま反映して暗くなる問題への対策。
//         書込は呼び出し側が pre-login + 背景セッション中 + hook Ready のゲートを満たす場合に限り
//         毎フレーム呼ばれる想定。ここではさらに null チェックのみを行い、それ以外の判断は持たない。
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;

namespace XivMiniUtil.Services.TitleBackground;

internal static unsafe class TitleBackgroundEnvironmentNoonWriter
{
    // エオルゼア時間の正午（24h × 3600 秒の半分）。背景セッション中のみ毎フレーム上書きし、
    // ログインした瞬間に呼び出し側のゲートが外れて書込が止まる（post-login へはリークしない）。
    public const float NoonDayTimeSeconds = 43200f;

    // Read-only probe (TitleBackgroundEnvironmentProbe) と同じ EnvManager インスタンスへ書き込む。
    // 呼び出し側のゲート（IsLoggedIn=false かつ背景セッション中かつ hook Ready）を信頼し、
    // ここでは EnvManager 自体の存在確認のみ行う。天候・露出・EnvSet 等、時刻以外は一切触らない。
    public static bool TryApplyNoon()
    {
        var manager = EnvManager.Instance();
        if (manager == null)
        {
            return false;
        }

        manager->DayTimeSeconds = NoonDayTimeSeconds;
        return true;
    }
}
