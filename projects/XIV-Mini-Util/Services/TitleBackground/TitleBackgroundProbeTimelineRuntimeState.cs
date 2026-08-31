// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundProbeTimelineRuntimeState.cs
// Description: probe session / camera probe timeline / phase2C timelineのセッション限定診断状態を保持する
// Reason: 巨大サービスから同一ライフサイクルの可変状態を責務単位で分離するため
namespace XivMiniUtil.Services.TitleBackground;

// probe session / camera probe timeline / phase2C timelineのセッション限定診断状態（config非保存）。
internal sealed class TitleBackgroundProbeTimelineRuntimeState
{
    public TitleBackgroundProbeSession? ActiveProbeSession { get; set; }

    public TitleBackgroundProbeSession? LastProbeSession { get; set; }

    public TitleBackgroundProbeCounters AutomaticProbeCounters { get; set; } = new();

    public bool AutomaticProbeCountersEnabled { get; set; }

    public TitleBackgroundCameraProbeSession? CameraProbeSession { get; set; }

    public int CameraProbeTimelineFrameCounter { get; set; } = -1;

    public string CameraProbeTimelineStatus { get; set; } = "not-run";

    public string CameraProbeTimelineError { get; set; } = string.Empty;

    public Dictionary<int, TitleBackgroundCameraProbeTimelineSnapshot> CameraProbeTimelineSnapshots { get; } = [];

    public Dictionary<int, TitleBackgroundCameraProbeTimelineEventCounts> CameraProbeTimelineEventCounts { get; } = [];

    public Dictionary<int, TitleBackgroundCameraProbeLobbyUpdateSnapshot> CameraProbeLobbyUpdateSnapshots { get; } = [];

    public int Phase2CTimelineFrameCounter { get; set; } = -1;

    public string Phase2CTimelineStatus { get; set; } = "not-run";

    public string Phase2CTimelineError { get; set; } = string.Empty;

    public Dictionary<int, TitleBackgroundPhase2CTimelineSnapshot> Phase2CTimelineSnapshots { get; } = [];
}
