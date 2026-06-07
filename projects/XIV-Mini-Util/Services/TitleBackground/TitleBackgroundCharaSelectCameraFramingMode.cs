// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCameraFramingMode.cs
// Description: Character Select 背景のカメラ framing モードを表す
// Reason: n4f4 などで top-down になるカメラを調整するための preset offset を選ばせるため
namespace XivMiniUtil.Services.TitleBackground;

public enum TitleBackgroundCharaSelectCameraFramingMode
{
    Default = 0,
    LowerCamera = 1,
    CenterCharacter = 2,
    CloserCharacter = 3,
    CandidateRecommended = 4,
    CustomExperimental = 5,
}
