// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCharacterFacing.cs
// Description: 保存カメラyawからキャラがカメラを向くyawと自然状態からの校正offsetを求める純粋ロジック
// Reason: actor回転規約を確認runで数値校正し、目視と定数修正の往復を無くすため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundCharaSelectCharacterFacing
{
    // 2026-07-07 real run: naturalRotation=0 and expectedFromGeometry=0.
    // The old PI default turned the saved view 180 degrees away from the camera.
    public const float DefaultCalibrationOffset = 0f;
    public const float CalibrationOffset = DefaultCalibrationOffset;
    public const float CalibrationStableSampleThreshold = 0.01f;
    public const float CalibrationPersistenceMaxSpread = 0.05f;
    public const int CalibrationRequiredStableSampleCount = 5;
    public const int FacingSettledFrameThreshold = 120;
    private const float MinimumHorizontalDistance = 0.01f;

    public static float ComputeYaw(float savedDirH, float calibrationOffset = DefaultCalibrationOffset)
    {
        if (!float.IsFinite(savedDirH) || !float.IsFinite(calibrationOffset))
        {
            return 0f;
        }

        return TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(savedDirH + calibrationOffset);
    }

    public static bool TryDeriveCalibrationOffset(
        float naturalRotation,
        Vector3 characterPosition,
        Vector3 cameraPosition,
        out float offset,
        out float expectedFromGeometry)
    {
        offset = 0f;
        expectedFromGeometry = 0f;
        if (!float.IsFinite(naturalRotation)
            || !TitleBackgroundCameraMath.IsFiniteVector(characterPosition)
            || !TitleBackgroundCameraMath.IsFiniteVector(cameraPosition))
        {
            return false;
        }

        var delta = cameraPosition - characterPosition;
        if (new Vector2(delta.X, delta.Z).Length() < MinimumHorizontalDistance)
        {
            return false;
        }

        expectedFromGeometry = MathF.Atan2(delta.X, delta.Z);
        offset = TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(
            naturalRotation - expectedFromGeometry);
        return float.IsFinite(offset);
    }

    public static float AngularDistance(float first, float second)
    {
        if (!float.IsFinite(first) || !float.IsFinite(second))
        {
            return float.NaN;
        }

        return Math.Abs(TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(second - first));
    }

    public static bool IsStableCalibrationSample(
        float? previousNaturalRotation,
        float? previousExpectedFromGeometry,
        float naturalRotation,
        float expectedFromGeometry,
        float threshold = CalibrationStableSampleThreshold)
    {
        if (!previousNaturalRotation.HasValue
            || !previousExpectedFromGeometry.HasValue
            || !float.IsFinite(naturalRotation)
            || !float.IsFinite(expectedFromGeometry)
            || !float.IsFinite(threshold))
        {
            return false;
        }

        var naturalDelta = AngularDistance(previousNaturalRotation.Value, naturalRotation);
        var geometryDelta = AngularDistance(previousExpectedFromGeometry.Value, expectedFromGeometry);
        return float.IsFinite(naturalDelta)
            && float.IsFinite(geometryDelta)
            && naturalDelta < threshold
            && geometryDelta < threshold;
    }

    public static float AccumulateOffsetSpread(
        float anchorOffset,
        float? currentMinRelativeOffset,
        float? currentMaxRelativeOffset,
        float derivedOffset,
        out float minRelativeOffset,
        out float maxRelativeOffset)
    {
        minRelativeOffset = currentMinRelativeOffset ?? 0f;
        maxRelativeOffset = currentMaxRelativeOffset ?? 0f;
        if (!float.IsFinite(anchorOffset) || !float.IsFinite(derivedOffset))
        {
            return float.NaN;
        }

        var relative = TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(derivedOffset - anchorOffset);
        if (!float.IsFinite(relative))
        {
            return float.NaN;
        }

        minRelativeOffset = Math.Min(minRelativeOffset, relative);
        maxRelativeOffset = Math.Max(maxRelativeOffset, relative);
        return maxRelativeOffset - minRelativeOffset;
    }

    public static bool HasStableCalibrationWindow(
        int stableSampleCount,
        float? offsetSpread,
        int requiredSampleCount = CalibrationRequiredStableSampleCount,
        float maxSpread = CalibrationPersistenceMaxSpread)
    {
        return stableSampleCount >= requiredSampleCount
            && offsetSpread.HasValue
            && float.IsFinite(offsetSpread.Value)
            && float.IsFinite(maxSpread)
            && offsetSpread.Value <= maxSpread;
    }

    public static int AdvanceFacingSettledFrameCount(bool applied, int currentCount)
    {
        return applied
            ? Math.Max(0, currentCount) + 1
            : 0;
    }

    public static float? AccumulateSettledMaxAngularError(
        int consecutiveAppliedFrameCount,
        float? currentMaxAngularError,
        float? angularError,
        int settleFrameThreshold = FacingSettledFrameThreshold)
    {
        if (consecutiveAppliedFrameCount < settleFrameThreshold
            || !angularError.HasValue
            || !float.IsFinite(angularError.Value))
        {
            return currentMaxAngularError;
        }

        return Math.Max(currentMaxAngularError ?? 0f, angularError.Value);
    }
}
