// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundEnvironmentProbe.cs
// Description: EnvManager から時刻・天候・降雨を read-only でスナップショットする
// Reason: 明るい候補探索の手がかり取得のため。環境値・ライティングへの書き込みは一切行わない
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;

namespace XivMiniUtil.Services.TitleBackground;

internal static unsafe class TitleBackgroundEnvironmentProbe
{
    // Read-only: returns the active environment time/weather/rain. Never writes any
    // environment, lighting, exposure, or EnvSet value.
    public static TitleBackgroundEnvironmentSnapshot Capture()
    {
        try
        {
            var manager = EnvManager.Instance();
            if (manager == null)
            {
                return TitleBackgroundEnvironmentSnapshot.Unavailable("env-manager-null");
            }

            return new TitleBackgroundEnvironmentSnapshot(
                true,
                "read",
                manager->DayTimeSeconds,
                manager->ActiveWeather,
                manager->EnvState.Rain);
        }
        catch (Exception ex)
        {
            return TitleBackgroundEnvironmentSnapshot.Unavailable(ex.GetType().Name);
        }
    }
}
