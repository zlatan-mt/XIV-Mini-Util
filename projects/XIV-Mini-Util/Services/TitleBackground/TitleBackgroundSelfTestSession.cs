// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundSelfTestSession.cs
// Description: TitleBackground self-test のセッション状態を保持する
// Reason: self-test 状態を TitleScreenBackgroundService の本体ロジックから分離するため
namespace XivMiniUtil.Services.TitleBackground;

internal sealed class TitleBackgroundSelfTestSession(
    string selectedPresetId,
    string territoryPath,
    uint territoryId,
    uint layerFilterKey,
    TitleBackgroundCharaSelectCameraCurve curve)
{
    public string SelectedPresetId { get; } = selectedPresetId;
    public string TerritoryPath { get; } = territoryPath;
    public uint TerritoryId { get; } = territoryId;
    public uint LayerFilterKey { get; } = layerFilterKey;
    public TitleBackgroundCharaSelectCameraCurve Curve { get; } = curve;
    public int Frame { get; set; }
}
