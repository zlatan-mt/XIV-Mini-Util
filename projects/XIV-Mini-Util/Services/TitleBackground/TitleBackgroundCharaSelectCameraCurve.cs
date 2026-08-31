// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCameraCurve.cs
// Description: Character select lobby camera の LookAt curve 派生値を表す
// Reason: native curve hook 実装前に CharacterPosition.Y 由来の low/mid/high を純粋ロジックで固定するため
namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundCharaSelectCameraCurve(
    float Low,
    float Mid,
    float High)
{
    public static TitleBackgroundCharaSelectCameraCurve Default { get; } = new(
        TitleBackgroundCharaSelectCameraLogic.MagicLow,
        TitleBackgroundCharaSelectCameraLogic.MagicMid,
        TitleBackgroundCharaSelectCameraLogic.MagicHigh);
}
