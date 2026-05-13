// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraMath.cs
// Description: Title Background camera diagnostics の純粋な検証を提供する
// Reason: 実機依存の CameraManager 読み取りと raw camera field の妥当性確認を分離するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundCameraMath
{
    public static bool IsFiniteVector(Vector3 value)
    {
        return float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z);
    }
}
