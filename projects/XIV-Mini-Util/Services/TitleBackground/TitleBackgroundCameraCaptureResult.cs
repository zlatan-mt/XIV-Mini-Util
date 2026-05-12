// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraCaptureResult.cs
// Description: 現在地とカメラ保存の結果を UI/diag へ渡す DTO
// Reason: 保存成功、部分維持、失敗理由を Configuration 変更前に明示するため
namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundCaptureValueState
{
    Unavailable,
    Captured,
    KeptExisting,
}

internal sealed class TitleBackgroundCameraCaptureResult
{
    public static TitleBackgroundCameraCaptureResult NotRun { get; } = new(
        "not-run",
        false,
        string.Empty,
        null,
        TitleBackgroundCaptureValueState.Unavailable,
        []);

    public TitleBackgroundCameraCaptureResult(
        string status,
        bool success,
        string failureReason,
        TitleBackgroundPreset? preset,
        TitleBackgroundCaptureValueState fovState,
        IReadOnlyList<string> messages)
    {
        Status = status;
        Success = success;
        FailureReason = failureReason;
        Preset = preset;
        FovState = fovState;
        Messages = messages;
        CapturedAt = DateTimeOffset.Now;
    }

    public DateTimeOffset CapturedAt { get; }
    public string Status { get; }
    public bool Success { get; }
    public string FailureReason { get; }
    public TitleBackgroundPreset? Preset { get; }
    public TitleBackgroundCaptureValueState FovState { get; }
    public IReadOnlyList<string> Messages { get; }
    public bool HasRun => !string.Equals(Status, "not-run", StringComparison.Ordinal);

    public static TitleBackgroundCameraCaptureResult Fail(string reason, IReadOnlyList<string>? messages = null)
    {
        return new TitleBackgroundCameraCaptureResult(
            "failed",
            false,
            reason,
            null,
            TitleBackgroundCaptureValueState.Unavailable,
            messages ?? [reason]);
    }

    public static TitleBackgroundCameraCaptureResult Succeed(
        TitleBackgroundPreset preset,
        TitleBackgroundCaptureValueState fovState,
        IReadOnlyList<string> messages)
    {
        return new TitleBackgroundCameraCaptureResult(
            "success",
            true,
            string.Empty,
            preset,
            fovState,
            messages);
    }
}
