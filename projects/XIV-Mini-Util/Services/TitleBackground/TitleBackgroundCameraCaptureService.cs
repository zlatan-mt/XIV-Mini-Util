// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraCaptureService.cs
// Description: ログイン中の現在地、カメラ、Focus、FOV を typed API で取得する
// Reason: 手入力調整前に現在の構図を保存し、取得不能時は fail-closed にするため
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Lumina.Excel.Sheets;
using System.Numerics;
using XivMiniUtil.Services.CharaSelect;
using ClientVector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;

namespace XivMiniUtil.Services.TitleBackground;

internal sealed unsafe class TitleBackgroundCameraCaptureService
{
    private const float MinFocusDistance = 0.001f;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;

    public TitleBackgroundCameraCaptureService(
        IClientState clientState,
        IObjectTable objectTable,
        IDataManager dataManager,
        IPluginLog log)
    {
        _clientState = clientState;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _log = log;
    }

    public TitleBackgroundCameraCaptureResult Capture(Configuration configuration)
    {
        try
        {
            if (!_clientState.IsLoggedIn)
            {
                return TitleBackgroundCameraCaptureResult.Fail("ログイン中ではないため保存できません。");
            }

            var territoryTypeId = _clientState.TerritoryType;
            if (territoryTypeId == 0)
            {
                return TitleBackgroundCameraCaptureResult.Fail("現在の TerritoryTypeId を取得できませんでした。");
            }

            if (!TryResolveTerritoryPath(territoryTypeId, out var territoryPath, out var territoryError))
            {
                return TitleBackgroundCameraCaptureResult.Fail(territoryError);
            }

            if (!TryCaptureActiveCamera(out var camera, out var focus, out var fovY, out var cameraMessages, out var cameraError))
            {
                return TitleBackgroundCameraCaptureResult.Fail(cameraError);
            }

            var characterPosition = TryCaptureCharacterPosition(out var characterRotation);
            var layoutLayerFilterKey = TryResolveNearestLayerFilterKey(territoryTypeId, characterPosition, out var resolvedLayerFilterKey, out var layerMessage)
                ? resolvedLayerFilterKey
                : null;

            var draft = new TitleBackgroundCameraCaptureDraft(
                territoryPath,
                territoryTypeId,
                camera,
                focus,
                fovY,
                territoryTypeId,
                layoutLayerFilterKey,
                characterPosition,
                characterRotation);
            var existing = TitleBackgroundCameraCapturePresetBuilder.FromConfiguration(configuration);
            if (!TitleBackgroundCameraCapturePresetBuilder.TryBuild(
                draft,
                existing,
                out var preset,
                out var fovState,
                out var presetMessages,
                out var presetError))
            {
                return TitleBackgroundCameraCaptureResult.Fail(presetError);
            }

            var messages = new List<string>();
            messages.AddRange(cameraMessages);
            if (!string.IsNullOrWhiteSpace(layerMessage))
            {
                messages.Add(layerMessage);
            }

            messages.AddRange(presetMessages);
            return TitleBackgroundCameraCaptureResult.Succeed(preset, fovState, messages);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TitleBackground camera capture failed.");
            return TitleBackgroundCameraCaptureResult.Fail($"保存中にエラーが発生しました: {ex.Message}");
        }
    }

    private bool TryResolveTerritoryPath(uint territoryTypeId, out string territoryPath, out string errorMessage)
    {
        territoryPath = string.Empty;
        errorMessage = string.Empty;

        try
        {
            var territorySheet = _dataManager.GetExcelSheet<TerritoryType>();
            var territory = territorySheet.GetRow(territoryTypeId);
            var bg = territory.Bg.ToString();
            if (string.IsNullOrWhiteSpace(bg))
            {
                errorMessage = $"TerritoryType {territoryTypeId} の Bg が空です。";
                return false;
            }

            if (!TitleBackgroundPathHelper.TryNormalizeAndValidateTerritoryPath(
                bg,
                out territoryPath,
                out errorMessage))
            {
                return false;
            }

            var lvbPath = TitleBackgroundPathHelper.BuildLvbPath(territoryPath);
            if (!_dataManager.FileExists(lvbPath))
            {
                errorMessage = $"LVB が見つかりません: {lvbPath}";
                territoryPath = string.Empty;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"TerritoryPath 解決に失敗しました: {ex.Message}";
            territoryPath = string.Empty;
            return false;
        }
    }

    private bool TryCaptureActiveCamera(
        out Vector3 camera,
        out Vector3 focus,
        out float? fovY,
        out IReadOnlyList<string> messages,
        out string errorMessage)
    {
        camera = default;
        focus = default;
        fovY = null;
        messages = [];
        errorMessage = string.Empty;

        var cameraManager = CameraManager.Instance();
        if (cameraManager == null)
        {
            errorMessage = "CameraManager.Instance() を取得できませんでした。";
            return false;
        }

        var activeCamera = cameraManager->GetActiveCamera();
        if (activeCamera == null)
        {
            errorMessage = "active camera を取得できませんでした。";
            return false;
        }

        camera = ToNumerics(activeCamera->CameraBase.SceneCamera.Position);
        var lookAtVector = ToNumerics(activeCamera->CameraBase.SceneCamera.LookAtVector);
        if (!IsFiniteVector(camera))
        {
            errorMessage = "Camera eye position に不正値が含まれています。";
            return false;
        }

        if (!TryDeriveFocus(camera, lookAtVector, activeCamera->Distance, out focus, out var focusMessage))
        {
            errorMessage = focusMessage;
            return false;
        }

        var capturedMessages = new List<string>
        {
            "Camera source: CameraManager.GetActiveCamera().CameraBase.SceneCamera.Position",
            focusMessage,
        };

        if (float.IsFinite(activeCamera->FoV) && activeCamera->FoV > 0f)
        {
            fovY = activeCamera->FoV;
            capturedMessages.Add("FOV source: CameraManager.GetActiveCamera().FoV");
        }
        else
        {
            capturedMessages.Add("FOV source unavailable: active camera FoV が不正値のため既存値を維持");
        }

        messages = capturedMessages;
        return true;
    }

    private Vector3? TryCaptureCharacterPosition(out float? characterRotation)
    {
        characterRotation = null;
        var localPlayer = _objectTable.LocalPlayer;
        if (localPlayer == null)
        {
            return null;
        }

        var position = localPlayer.Position;
        var captured = new Vector3(position.X, position.Y, position.Z);
        if (!IsFiniteVector(captured))
        {
            return null;
        }

        if (float.IsFinite(localPlayer.Rotation))
        {
            characterRotation = localPlayer.Rotation;
        }

        return captured;
    }

    private bool TryResolveNearestLayerFilterKey(uint territoryTypeId, Vector3? characterPosition, out uint? layerFilterKey, out string message)
    {
        layerFilterKey = null;
        message = string.Empty;
        if (!characterPosition.HasValue)
        {
            message = "LayoutLayerFilterKey 取得失敗: CharacterPosition が取得できないため nearest Level を解決しません。";
            return false;
        }

        if (territoryTypeId > ushort.MaxValue)
        {
            message = "LayoutLayerFilterKey 取得失敗: TerritoryTypeId が ushort 範囲外です。";
            return false;
        }

        try
        {
            var levelSheet = _dataManager.GetExcelSheet<Level>();
            var candidates = levelSheet.Select(level => new CharaSelectLevelCandidate(
                level.RowId,
                level.Territory.RowId > ushort.MaxValue ? (ushort)0 : (ushort)level.Territory.RowId,
                level.Type,
                level.X,
                level.Y,
                level.Z));
            var position = characterPosition.Value;
            var resolved = CharaSelectLevelResolver.ResolveNearest(
                candidates,
                (ushort)territoryTypeId,
                position.X,
                position.Y,
                position.Z);
            if (!resolved.IsValid)
            {
                message = "LayoutLayerFilterKey 取得失敗: 現在地に対応する Level row が見つかりません。";
                return false;
            }

            layerFilterKey = resolved.Type;
            message = $"LayoutLayerFilterKey source: nearest Level row {resolved.RowId}";
            return true;
        }
        catch (Exception ex)
        {
            message = $"LayoutLayerFilterKey 取得失敗: {ex.Message}";
            return false;
        }
    }

    private static bool TryDeriveFocus(
        Vector3 camera,
        Vector3 lookAtVector,
        float distance,
        out Vector3 focus,
        out string message)
    {
        focus = default;
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

    private static Vector3 ToNumerics(ClientVector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z);
    }
}
