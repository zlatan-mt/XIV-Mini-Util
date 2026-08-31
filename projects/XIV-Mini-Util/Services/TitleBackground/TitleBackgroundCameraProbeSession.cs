// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraProbeSession.cs
// Description: TitleBackground camera probe のセッション状態を保持する
// Reason: camera probe 状態を TitleScreenBackgroundService の本体ロジックから分離するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal sealed class TitleBackgroundCameraProbeSession
{
    public TitleBackgroundCameraProbeSession(
        TitleBackgroundCameraProbeSettingsSnapshot originalSettings,
        Vector3 baselineCamera,
        Vector3 baselineFocus,
        Vector3 probeCamera,
        Vector3 probeFocus)
    {
        OriginalSettings = originalSettings;
        BaselineCamera = baselineCamera;
        BaselineFocus = baselineFocus;
        ProbeCamera = probeCamera;
        ProbeFocus = probeFocus;
    }

    public DateTimeOffset ArmedAt { get; } = DateTimeOffset.Now;
    public TitleBackgroundCameraProbeSettingsSnapshot OriginalSettings { get; }
    public Vector3 BaselineCamera { get; }
    public Vector3 BaselineFocus { get; }
    public Vector3 ProbeCamera { get; }
    public Vector3 ProbeFocus { get; }
}
