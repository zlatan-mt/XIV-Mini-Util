// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundEnvironmentClearSkyWriter.cs
// Description: 背景セッション中のみ EnvManager の天候を晴れ(Clear Skies)へ unsafe 書込する
// Reason: ログイン画面（Title Background）が実天候（雨など）をそのまま反映して暗い雨天スカイドーム・
//         濡れ表現になる問題への対策。書込は呼び出し側が pre-login + 背景セッション中 + hook Ready の
//         ゲートを満たす場合に限り毎フレーム呼ばれる想定。ここではさらに null チェックのみを行い、
//         それ以外の判断は持たない。
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;

namespace XivMiniUtil.Services.TitleBackground;

internal static unsafe class TitleBackgroundEnvironmentClearSkyWriter
{
    // FFXIV の Weather シート row 1 = "Clear Skies"（晴れ）。EnvManager.ActiveWeather は
    // このシートの row id を byte で保持するフィールド（TitleBackgroundEnvironmentProbe が
    // read-only で参照している同フィールド）。
    public const byte ClearSkiesWeatherId = 1;

    // Read-only probe (TitleBackgroundEnvironmentProbe) と同じ EnvManager インスタンスへ書き込む。
    // 呼び出し側のゲート（IsLoggedIn=false かつ背景セッション中かつ hook Ready）を信頼し、
    // ここでは EnvManager 自体の存在確認のみ行う。時刻・露出・EnvSet 等、天候以外は一切触らない。
    //
    // EnvManager には ActiveWeather（byte）以外に天候専用の遷移フィールドは存在しない
    // （ActiveTransitionTime / CurrentTransitionTime / TransitionProgress / TransitionTime は
    // 時刻遷移と共用のフィールドであり、天候専用の "NextWeather" 等は構造体に存在しないことを
    // リフレクションで確認済み）。そのため ActiveWeather のみを毎フレーム上書きすれば足りる。
    public static bool TryApplyClearSky()
    {
        var manager = EnvManager.Instance();
        if (manager == null)
        {
            return false;
        }

        manager->ActiveWeather = ClearSkiesWeatherId;
        return true;
    }
}
