// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraProbeSettingsSnapshot.cs
// Description: camera probe が変更する前の TitleBackground camera 設定を保持する
// Reason: 診断用の camera 設定 snapshot を TitleScreenBackgroundService の本体ロジックから分離するため
namespace XivMiniUtil.Services.TitleBackground;

internal sealed record TitleBackgroundCameraProbeSettingsSnapshot(
    string SelectedPresetId,
    bool CameraOverrideEnabled,
    float CameraX,
    float CameraY,
    float CameraZ,
    float FocusX,
    float FocusY,
    float FocusZ)
{
    public static TitleBackgroundCameraProbeSettingsSnapshot Capture(Configuration configuration)
    {
        return new TitleBackgroundCameraProbeSettingsSnapshot(
            configuration.TitleBackgroundSelectedPresetId,
            configuration.TitleBackgroundCameraOverrideEnabled,
            configuration.TitleBackgroundCameraX,
            configuration.TitleBackgroundCameraY,
            configuration.TitleBackgroundCameraZ,
            configuration.TitleBackgroundFocusX,
            configuration.TitleBackgroundFocusY,
            configuration.TitleBackgroundFocusZ);
    }

    public void ApplyTo(Configuration configuration)
    {
        configuration.TitleBackgroundSelectedPresetId = SelectedPresetId;
        configuration.TitleBackgroundCameraOverrideEnabled = CameraOverrideEnabled;
        configuration.TitleBackgroundCameraX = CameraX;
        configuration.TitleBackgroundCameraY = CameraY;
        configuration.TitleBackgroundCameraZ = CameraZ;
        configuration.TitleBackgroundFocusX = FocusX;
        configuration.TitleBackgroundFocusY = FocusY;
        configuration.TitleBackgroundFocusZ = FocusZ;
    }
}
