// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraCaptureDraft.cs
// Description: 現在地とカメラ保存値を Configuration 反映前に正規化/検証する
// Reason: 取得不能値や不正値で既存設定を破壊しない fail-closed 境界を作るため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal sealed record TitleBackgroundCameraCaptureDraft(
    string TerritoryPath,
    uint TerritoryTypeId,
    Vector3 Camera,
    Vector3 Focus,
    float? FovY,
    uint? LayoutTerritoryTypeId,
    uint? LayoutLayerFilterKey,
    Vector3? CharacterPosition,
    float? CharacterRotation);

internal static class TitleBackgroundCameraCapturePresetBuilder
{
    public static TitleBackgroundPreset FromConfiguration(Configuration configuration)
    {
        return new TitleBackgroundPreset
        {
            TerritoryPath = configuration.TitleBackgroundTerritoryPath,
            TerritoryTypeId = configuration.TitleBackgroundTerritoryTypeId,
            LayoutTerritoryTypeId = configuration.TitleBackgroundLayoutTerritoryTypeId,
            LayoutLayerFilterKey = configuration.TitleBackgroundLayoutLayerFilterKey,
            CharacterPosition = new Vector3(
                configuration.TitleBackgroundCharacterPositionX,
                configuration.TitleBackgroundCharacterPositionY,
                configuration.TitleBackgroundCharacterPositionZ),
            CharacterRotation = configuration.TitleBackgroundCharacterRotation,
            CameraX = configuration.TitleBackgroundCameraX,
            CameraY = configuration.TitleBackgroundCameraY,
            CameraZ = configuration.TitleBackgroundCameraZ,
            FocusX = configuration.TitleBackgroundFocusX,
            FocusY = configuration.TitleBackgroundFocusY,
            FocusZ = configuration.TitleBackgroundFocusZ,
            FovY = configuration.TitleBackgroundFovY,
            WeatherId = configuration.TitleBackgroundWeatherId,
            TimeOffset = configuration.TitleBackgroundTimeOffset,
            BgmPath = configuration.TitleBackgroundBgmPath,
        }.Normalize();
    }

    public static bool TryBuild(
        TitleBackgroundCameraCaptureDraft draft,
        TitleBackgroundPreset existing,
        out TitleBackgroundPreset preset,
        out TitleBackgroundCaptureValueState fovState,
        out IReadOnlyList<string> messages,
        out string errorMessage)
    {
        preset = new TitleBackgroundPreset();
        messages = [];
        fovState = TitleBackgroundCaptureValueState.Unavailable;

        if (!TitleBackgroundPathHelper.TryNormalizeAndValidateTerritoryPath(
            draft.TerritoryPath,
            out var normalizedPath,
            out errorMessage))
        {
            return false;
        }

        if (draft.TerritoryTypeId == 0)
        {
            errorMessage = "TerritoryTypeId を取得できませんでした。";
            return false;
        }

        if (!IsFiniteVector(draft.Camera))
        {
            errorMessage = "Camera position に不正値が含まれています。";
            return false;
        }

        if (!IsFiniteVector(draft.Focus))
        {
            errorMessage = "Focus point に不正値が含まれています。";
            return false;
        }

        if (draft.FovY.HasValue && !float.IsFinite(draft.FovY.Value))
        {
            errorMessage = "FOV Y に不正値が含まれています。";
            return false;
        }

        if (draft.CharacterPosition.HasValue && !IsFiniteVector(draft.CharacterPosition.Value))
        {
            errorMessage = "CharacterPosition に不正値が含まれています。";
            return false;
        }

        if (draft.CharacterRotation.HasValue && !float.IsFinite(draft.CharacterRotation.Value))
        {
            errorMessage = "CharacterRotation に不正値が含まれています。";
            return false;
        }

        var capturedMessages = new List<string>
        {
            $"TerritoryPath 保存成功: {normalizedPath}",
            $"TerritoryTypeId 保存成功: {draft.TerritoryTypeId}",
            $"Camera 保存成功: {FormatVector(draft.Camera)}",
            $"Focus 保存成功: {FormatVector(draft.Focus)}",
        };

        var fovY = existing.FovY;
        if (draft.FovY.HasValue)
        {
            fovY = draft.FovY.Value;
            fovState = TitleBackgroundCaptureValueState.Captured;
            capturedMessages.Add($"FOV Y 保存成功: {TitleBackgroundPreset.ClampFovY(fovY):0.###}");
        }
        else
        {
            fovState = TitleBackgroundCaptureValueState.KeptExisting;
            capturedMessages.Add($"FOV Y 取得失敗: 既存値を維持 ({existing.FovY:0.###})");
        }

        if (draft.LayoutTerritoryTypeId.HasValue)
        {
            capturedMessages.Add($"LayoutTerritoryTypeId 保存成功: {draft.LayoutTerritoryTypeId.Value}");
        }
        else
        {
            capturedMessages.Add("LayoutTerritoryTypeId 取得失敗: 既存値を維持");
        }

        if (draft.LayoutLayerFilterKey.HasValue)
        {
            capturedMessages.Add($"LayoutLayerFilterKey 保存成功: {draft.LayoutLayerFilterKey.Value}");
        }
        else
        {
            capturedMessages.Add("LayoutLayerFilterKey 取得失敗: 既存値を維持");
        }

        if (draft.CharacterPosition.HasValue)
        {
            capturedMessages.Add($"CharacterPosition 保存成功: {FormatVector(draft.CharacterPosition.Value)}");
        }
        else
        {
            capturedMessages.Add("CharacterPosition 取得失敗: 既存値を維持");
        }

        if (draft.CharacterRotation.HasValue)
        {
            capturedMessages.Add($"CharacterRotation 保存成功: {draft.CharacterRotation.Value:0.###}");
        }
        else
        {
            capturedMessages.Add("CharacterRotation 取得失敗: 既存値を維持");
        }

        preset = new TitleBackgroundPreset
        {
            TerritoryPath = normalizedPath,
            TerritoryTypeId = draft.TerritoryTypeId,
            LayoutTerritoryTypeId = draft.LayoutTerritoryTypeId ?? existing.LayoutTerritoryTypeId,
            LayoutLayerFilterKey = draft.LayoutLayerFilterKey ?? existing.LayoutLayerFilterKey,
            CharacterPosition = draft.CharacterPosition ?? existing.CharacterPosition,
            CharacterRotation = draft.CharacterRotation ?? existing.CharacterRotation,
            CameraX = draft.Camera.X,
            CameraY = draft.Camera.Y,
            CameraZ = draft.Camera.Z,
            FocusX = draft.Focus.X,
            FocusY = draft.Focus.Y,
            FocusZ = draft.Focus.Z,
            FovY = fovY,
            WeatherId = existing.WeatherId,
            TimeOffset = existing.TimeOffset,
            BgmPath = existing.BgmPath,
        }.Normalize();

        if (!preset.Validate(out errorMessage))
        {
            return false;
        }

        messages = capturedMessages;
        return true;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.X:0.###}, {value.Y:0.###}, {value.Z:0.###})";
    }
}
