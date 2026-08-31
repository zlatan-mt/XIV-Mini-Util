// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCameraInput.cs
// Description: Character select lobby camera adapter の永続設定由来入力を表す
// Reason: preset の CharacterPosition / CharacterRotation を raw camera field と分離して扱うため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundCharaSelectCameraInput(
    Vector3 CharacterPosition,
    float CharacterRotation)
{
    public static TitleBackgroundCharaSelectCameraInput FromConfiguration(Configuration configuration)
    {
        return Create(
            new Vector3(
                configuration.TitleBackgroundCharacterPositionX,
                configuration.TitleBackgroundCharacterPositionY,
                configuration.TitleBackgroundCharacterPositionZ),
            configuration.TitleBackgroundCharacterRotation);
    }

    public static TitleBackgroundCharaSelectCameraInput Create(Vector3 characterPosition, float characterRotation)
    {
        return new TitleBackgroundCharaSelectCameraInput(
            TitleBackgroundCharaSelectCameraLogic.SanitizeVector(characterPosition),
            TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(characterRotation));
    }
}
