// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundEnvironmentSnapshot.cs
// Description: Character Select 中の環境（時刻・天候・降雨）を read-only でスナップショットし、明るい候補探索の手がかりに変換する
// Reason: 明るいレイヤー/シーン探しを支援するため。環境値への書き込みは一切行わず、判定ロジックを実機なしでテスト可能にする
namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundEnvironmentSnapshot(
    bool Available,
    string ReadStatus,
    float DayTimeSeconds,
    byte ActiveWeather,
    float Rain)
{
    public static TitleBackgroundEnvironmentSnapshot Unavailable(string status) =>
        new(false, status, 0f, 0, 0f);
}

internal enum TitleBackgroundEnvironmentDaylight
{
    Unknown,
    Night,
    Twilight,
    Daylight,
}

internal readonly record struct TitleBackgroundBrightnessExploration(
    TitleBackgroundEnvironmentDaylight Daylight,
    float DayTimeHours,
    bool Rainy,
    string BrightnessHint,
    string ExplorationHint);

internal static class TitleBackgroundBrightnessExplorationLogic
{
    // Eorzea の 1 日は 24 時間ぶんの秒数で表現される。明るさは時刻が支配的なので、
    // 時刻帯から「現在の環境が暗い/明るい」を推定し、明るい候補探索の手がかりを返す。
    public const float DaylightStartHour = 7f;
    public const float DaylightEndHour = 17f;
    public const float TwilightStartHour = 5f;
    public const float TwilightEndHour = 19f;
    public const float RainThreshold = 0.05f;

    public static TitleBackgroundBrightnessExploration Evaluate(TitleBackgroundEnvironmentSnapshot snapshot)
    {
        if (!snapshot.Available || !float.IsFinite(snapshot.DayTimeSeconds))
        {
            return new TitleBackgroundBrightnessExploration(
                TitleBackgroundEnvironmentDaylight.Unknown,
                0f,
                false,
                "unknown",
                "environment unavailable; capture during Character Select to evaluate brightness");
        }

        var hours = NormalizeHours(snapshot.DayTimeSeconds / 3600f);
        var rainy = float.IsFinite(snapshot.Rain) && snapshot.Rain > RainThreshold;
        var daylight = ClassifyDaylight(hours);
        var brightnessHint = daylight switch
        {
            TitleBackgroundEnvironmentDaylight.Daylight => rainy ? "daylight-but-rainy" : "daylight",
            TitleBackgroundEnvironmentDaylight.Twilight => "twilight-dim",
            TitleBackgroundEnvironmentDaylight.Night => "night-dark",
            _ => "unknown",
        };
        var explorationHint = BuildExplorationHint(daylight, rainy);
        return new TitleBackgroundBrightnessExploration(daylight, hours, rainy, brightnessHint, explorationHint);
    }

    public static TitleBackgroundEnvironmentDaylight ClassifyDaylight(float hours)
    {
        if (!float.IsFinite(hours))
        {
            return TitleBackgroundEnvironmentDaylight.Unknown;
        }

        var normalized = NormalizeHours(hours);
        if (normalized >= DaylightStartHour && normalized < DaylightEndHour)
        {
            return TitleBackgroundEnvironmentDaylight.Daylight;
        }

        if (normalized >= TwilightStartHour && normalized < TwilightEndHour)
        {
            return TitleBackgroundEnvironmentDaylight.Twilight;
        }

        return TitleBackgroundEnvironmentDaylight.Night;
    }

    private static string BuildExplorationHint(TitleBackgroundEnvironmentDaylight daylight, bool rainy)
    {
        return daylight switch
        {
            TitleBackgroundEnvironmentDaylight.Daylight => rainy
                ? "scene is daytime but rainy; try a manual candidate with a clear-weather / brighter layer"
                : "scene is already daytime; if still dark, try a brighter layerFilterKey via a manual candidate slot",
            TitleBackgroundEnvironmentDaylight.Twilight => "scene is at twilight; try a daytime scene or a brighter layerFilterKey via a manual candidate slot",
            TitleBackgroundEnvironmentDaylight.Night => "scene is at night; try a daytime scene or a brighter layerFilterKey via a manual candidate slot",
            _ => "environment unavailable; capture during Character Select to evaluate brightness",
        };
    }

    private static float NormalizeHours(float hours)
    {
        if (!float.IsFinite(hours))
        {
            return 0f;
        }

        var normalized = hours % 24f;
        if (normalized < 0f)
        {
            normalized += 24f;
        }

        return normalized;
    }
}
