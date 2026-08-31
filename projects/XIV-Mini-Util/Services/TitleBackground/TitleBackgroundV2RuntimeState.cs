// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundV2RuntimeState.cs
// Description: Title Background V2 (Il Mheg proof) の run-scoped 可変状態を集約する holder。
// Reason: V2 は「scene-ready で 1 回 + bounded 初期化ウィンドウ内だけ」カメラ構図を書く。legacy の
//         毎フレーム reassert (_savedViewPoseMaintain / Phase2G) は V2 active 中は一切 arm しない。
//         その bounded ウィンドウの終了条件と診断カウンタをここに閉じ込め、service 直下の可変 field を増やさない。
namespace XivMiniUtil.Services.TitleBackground;

// V2 framing one-shot の bounded 初期化ウィンドウ設定。
// - scene-ready で最初の適用を試み、camera 未準備なら RetryBudget 回まで後続フレームで再試行する。
// - 1 回でも書込に成功したら SettleBudget フレームだけ追い書きしてエンジンの load 直後 curve pass に勝つ。
// - どちらの予算も尽きたら当該 scene generation では恒久停止する（新 generation で再 arm）。
internal static class TitleBackgroundV2FramingWindow
{
    public const int RetryBudget = 5;
    public const int SettleBudget = 8;
}

internal sealed class TitleBackgroundV2RuntimeState
{
    // 現在 arm 中の scene generation（LoadLobbyScene 由来）。0 は未 arm。
    public int SceneGeneration { get; private set; }

    // この generation で 1 回でも framing 書込に成功したか。
    public bool FramingApplied { get; private set; }

    // この generation の framing 書込試行回数（成功・失敗込み）。
    public int FramingAttemptCount { get; private set; }

    // この generation の framing 書込成功回数。
    public int FramingAppliedCount { get; private set; }

    // 残り retry 予算（camera 未準備での再試行）。
    public int RetryRemaining { get; private set; }

    // 残り settle 予算（成功後の追い書き）。
    public int SettleRemaining { get; private set; }

    // bounded ウィンドウが恒久停止済みか（当該 generation）。
    public bool WindowClosed { get; private set; }

    public string LastFramingStatus { get; private set; } = "not-run";
    public string LastStopReason { get; private set; } = "not-run";

    // 適用した pose（診断・report 用。カメラには input params のみ書く）。
    public float LastAppliedYaw { get; private set; }
    public float LastAppliedPitch { get; private set; }
    public float LastAppliedDistance { get; private set; }
    public float LastAppliedFovY { get; private set; }
    public int LastAppliedFrame { get; private set; } = -1;

    // scene-ready シグナル観測回数（当該セッション、report 用）。
    public int SceneReadyObservedCount { get; private set; }

    // login 後に V2 native 書込が停止済みであることを観測したか。
    public bool PostLoginWritesStopped { get; private set; }

    // 当該 scene generation 向けに bounded ウィンドウを arm する。
    public void ArmForSceneGeneration(int sceneGeneration)
    {
        if (sceneGeneration <= 0 || sceneGeneration == SceneGeneration)
        {
            return;
        }

        SceneGeneration = sceneGeneration;
        FramingApplied = false;
        FramingAttemptCount = 0;
        FramingAppliedCount = 0;
        RetryRemaining = TitleBackgroundV2FramingWindow.RetryBudget;
        SettleRemaining = TitleBackgroundV2FramingWindow.SettleBudget;
        WindowClosed = false;
        LastFramingStatus = "armed";
        LastStopReason = "none";
        LastAppliedFrame = -1;
    }

    public void NotifySceneReadyObserved() => SceneReadyObservedCount++;

    // まだ書込を試みてよいか（bounded ウィンドウ内か）。close 判定は RecordFramingAttempt が行う。
    public bool ShouldAttemptFraming(int activeSceneGeneration, int runtimeSceneGeneration)
    {
        return !WindowClosed
            && SceneGeneration > 0
            && SceneGeneration == activeSceneGeneration
            && SceneGeneration == runtimeSceneGeneration;
    }

    public void RecordFramingAttempt(bool success, int frame, string status)
    {
        FramingAttemptCount++;
        LastFramingStatus = status;

        if (success)
        {
            FramingApplied = true;
            FramingAppliedCount++;
            LastAppliedFrame = frame;
        }

        if (FramingApplied)
        {
            // 一度でも成功したら、以後は success / failure に関係なく settle 予算を 1 消費する。
            // これで success→failure が続いても必ず有限回で恒久停止する（無期限 attempt 経路を塞ぐ）。
            if (SettleRemaining > 0)
            {
                SettleRemaining--;
            }

            if (SettleRemaining <= 0)
            {
                CloseWindow("settle-window-complete");
            }

            return;
        }

        // まだ 1 度も成功していない間は failure が retry 予算を消費する。使い切ったら恒久停止。
        if (RetryRemaining > 0)
        {
            RetryRemaining--;
        }

        if (RetryRemaining <= 0)
        {
            CloseWindow("retry-exhausted");
        }
    }

    public void RecordAppliedPose(float yaw, float pitch, float distance, float fovY)
    {
        LastAppliedYaw = yaw;
        LastAppliedPitch = pitch;
        LastAppliedDistance = distance;
        LastAppliedFovY = fovY;
    }

    public void CloseWindow(string reason)
    {
        if (WindowClosed)
        {
            return;
        }

        WindowClosed = true;
        LastStopReason = reason;
    }

    public void MarkPostLoginWritesStopped() => PostLoginWritesStopped = true;

    // service reload / dispose / override 無効化で全 run-scoped 状態を破棄する。
    public void Reset()
    {
        SceneGeneration = 0;
        FramingApplied = false;
        FramingAttemptCount = 0;
        FramingAppliedCount = 0;
        RetryRemaining = 0;
        SettleRemaining = 0;
        WindowClosed = false;
        LastFramingStatus = "not-run";
        LastStopReason = "not-run";
        LastAppliedYaw = 0f;
        LastAppliedPitch = 0f;
        LastAppliedDistance = 0f;
        LastAppliedFovY = 0f;
        LastAppliedFrame = -1;
        SceneReadyObservedCount = 0;
        PostLoginWritesStopped = false;
    }
}
