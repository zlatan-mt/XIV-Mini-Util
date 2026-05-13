// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraMath.cs
// Description: Title Background camera diagnostics の純粋な計算を提供する
// Reason: 実機依存の CameraManager 読み取りと focus 導出ロジックを分離してテスト可能にするため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundCameraMath
{
    private const float MinFocusDistance = 0.001f;

    public static bool TryDeriveFocus(
        Vector3 camera,
        Vector3 lookAtVector,
        float distance,
        out Vector3 focus,
        out string message)
    {
        focus = default;
        if (!IsFiniteVector(camera))
        {
            message = "Focus 取得失敗: camera eye position に不正値が含まれています。";
            return false;
        }

        if (!IsFiniteVector(lookAtVector))
        {
            message = "Focus 取得失敗: camera look-at vector に不正値が含まれています。";
            return false;
        }

        if (!float.IsFinite(distance) || distance <= MinFocusDistance)
        {
            message = "Focus 取得失敗: camera distance が取得不能または 0 です。";
            return false;
        }

        var length = lookAtVector.Length();
        if (!float.IsFinite(length) || length <= MinFocusDistance)
        {
            message = "Focus 取得失敗: camera look-at vector が 0 です。";
            return false;
        }

        focus = camera + (lookAtVector / length * distance);
        if (!IsFiniteVector(focus))
        {
            message = "Focus 取得失敗: 導出した focus point に不正値が含まれています。";
            return false;
        }

        message = "Focus source: SceneCamera.LookAtVector + active camera Distance から安全に導出";
        return true;
    }

    public static bool IsFiniteVector(Vector3 value)
    {
        return float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z);
    }
}
