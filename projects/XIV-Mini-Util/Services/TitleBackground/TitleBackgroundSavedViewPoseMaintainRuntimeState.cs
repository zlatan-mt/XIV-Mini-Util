// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundSavedViewPoseMaintainRuntimeState.cs
// Description: FixOn後からpre-login CharaSelect終了まで、保存pose入力paramsをcurve更新後に再assertするbounded状態
// Reason: engineのcurve処理がDirH/DirV/Distanceを次フレームにdefaultへ戻すことを実機traceで確認したため
namespace XivMiniUtil.Services.TitleBackground;

internal sealed class TitleBackgroundSavedViewPoseMaintainRuntimeState
{
    public bool Active { get; private set; }

    public int SceneGeneration { get; private set; }

    public TitleBackgroundCharaSelectView SavedView { get; private set; } = TitleBackgroundCharaSelectView.None;

    public int AppliedCallCount { get; private set; }

    public int AppliedFrameCount { get; private set; }

    public int? LastAppliedFrame { get; private set; }

    public string StopReason { get; private set; } = "not-started";

    public void Arm(TitleBackgroundCharaSelectView savedView, int sceneGeneration)
    {
        Active = true;
        SceneGeneration = sceneGeneration;
        SavedView = savedView;
        AppliedCallCount = 0;
        AppliedFrameCount = 0;
        LastAppliedFrame = null;
        StopReason = "active";
    }

    public void MarkApplied(int? frame)
    {
        AppliedCallCount++;
        if (frame.HasValue && LastAppliedFrame != frame)
        {
            AppliedFrameCount++;
        }

        LastAppliedFrame = frame;
    }

    public void Stop(string reason)
    {
        if (!Active)
        {
            return;
        }

        Active = false;
        StopReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
    }

    public void Reset()
    {
        Active = false;
        SceneGeneration = 0;
        SavedView = TitleBackgroundCharaSelectView.None;
        AppliedCallCount = 0;
        AppliedFrameCount = 0;
        LastAppliedFrame = null;
        StopReason = "not-started";
    }
}
