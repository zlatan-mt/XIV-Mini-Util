// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCharacterFacing.cs
// Description: 保存カメラyawからキャラがカメラを向くための固定yawを求める純粋ロジック
// Reason: 座標規約の初回実機校正を定数1つに閉じ込め、毎フレーム動的値を回転源にしないため
namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundCharaSelectCharacterFacing
{
    // 初回校正値。DirHは焦点からカメラへの方向なので、ゲームのactor前方規約の最有力値としてπ反転する。
    public const float CalibrationOffset = MathF.PI;

    public static float ComputeYaw(float savedDirH, float calibrationOffset = CalibrationOffset)
    {
        if (!float.IsFinite(savedDirH) || !float.IsFinite(calibrationOffset))
        {
            return 0f;
        }

        return TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(savedDirH + calibrationOffset);
    }
}
