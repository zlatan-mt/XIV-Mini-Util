// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.CameraProfiles.cs
// Description: TitleBackground の character-select camera profile 解決と legacy visible profile 操作を提供する
// Reason: カメラプロファイル関連 UI 補助処理を TitleScreenBackgroundService の本体状態管理から分離するため
using System.Numerics;
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    private TitleBackgroundCharacterSelectCameraProfile ResolveCurrentTitleBackgroundCameraProfile(string? candidateId = null)
    {
        var resolvedCandidateId = string.IsNullOrWhiteSpace(candidateId)
            ? ResolveCurrentOverrideCandidate().Id
            : candidateId;
        return TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGetPreferredCameraProfile(
                resolvedCandidateId,
                _configuration.TitleBackgroundCharaSelectCameraFramingMode,
                _configuration.TitleBackgroundCapturedCameraProfileEnabled,
                _configuration.TitleBackgroundCapturedDirH,
                _configuration.TitleBackgroundCapturedDirV,
                _configuration.TitleBackgroundCapturedDistance,
                BuildCapturedProfilePosition(),
                BuildCapturedProfileLookAt(),
                out var profile)
            ? profile
            : TitleBackgroundCharacterSelectCameraProfile.None;
    }

    private Vector3 BuildCapturedProfilePosition()
    {
        return new Vector3(
            _configuration.TitleBackgroundCapturedPositionX,
            _configuration.TitleBackgroundCapturedPositionY,
            _configuration.TitleBackgroundCapturedPositionZ);
    }

    private Vector3 BuildCapturedProfileLookAt()
    {
        return new Vector3(
            _configuration.TitleBackgroundCapturedLookAtX,
            _configuration.TitleBackgroundCapturedLookAtY,
            _configuration.TitleBackgroundCapturedLookAtZ);
    }

    private static string BuildVisibleProfileAppliedState(
        TitleBackgroundCharacterSelectCameraProfile profile,
        bool runtimeHasProfilePose,
        bool cameraFramingApplied)
    {
        if (!profile.HasProfile)
        {
            return "False";
        }

        if (runtimeHasProfilePose)
        {
            return "True";
        }

        return cameraFramingApplied ? "Partial" : "False";
    }

    private static string BuildCameraProfileApplyRoute(
        TitleBackgroundCharacterSelectCameraProfile profile,
        bool runtimeHasProfilePose,
        bool cameraFramingApplied)
    {
        if (!profile.HasProfile)
        {
            return "none";
        }

        if (string.Equals(profile.ProfileSource, "captured", StringComparison.Ordinal))
        {
            return runtimeHasProfilePose ? "captured-profile" : "none";
        }

        return runtimeHasProfilePose
            ? "one-shot-setup"
            : cameraFramingApplied ? "generated-curve" : "none";
    }

    public IReadOnlyList<string> GetTitleBackgroundCameraProfileDiagnosticLines()
    {
        var cameraProfile = ResolveCurrentTitleBackgroundCameraProfile();
        var titleBackgroundProfileLabel = cameraProfile.HasProfile
            ? $"{cameraProfile.ProfileId} / {cameraProfile.ProfileSource}"
            : "none";
        var poseAvailable = TryBuildPresetCameraRuntimePose(out var titlePose, out var poseError);
        var hasCurrent = TryGetLatestPhase2CTimelineSnapshot(out var current);
        var currentDirH = hasCurrent ? current.LobbyDirH ?? current.DirH : null;
        var currentDirV = hasCurrent ? current.LobbyDirV ?? current.DirV : null;
        var currentDistance = hasCurrent ? current.LobbyDistance ?? current.Distance : null;
        var currentPosition = hasCurrent ? current.SceneCameraPosition : _cameraObservation.LastPostFixOnSceneCameraPosition;
        var currentLookAt = hasCurrent ? current.SceneCameraLookAtVector ?? current.LobbyLastLookAtVector : _cameraObservation.LastPostFixOnLookAtVector;
        var legacyRoute = _charaSelectService?.GetLegacyCompositionRouteSnapshot()
            ?? CharaSelectCompositionRouteRuntimeSnapshot.Empty;
        var bridgeRoute = _charaSelectService?.GetTitleBackgroundBridgeRouteSnapshot()
            ?? CharaSelectCompositionRouteRuntimeSnapshot.Empty;
        var capturedProfileAvailable = _configuration.TitleBackgroundCapturedCameraProfileEnabled
            && _configuration.TitleBackgroundCapturedDistance > 0f;
        float? legacyDirH = capturedProfileAvailable ? _configuration.TitleBackgroundCapturedDirH : currentDirH;
        float? legacyDirV = capturedProfileAvailable ? _configuration.TitleBackgroundCapturedDirV : currentDirV;
        float? legacyDistance = capturedProfileAvailable ? _configuration.TitleBackgroundCapturedDistance : currentDistance;
        Vector3? legacyPosition = capturedProfileAvailable ? BuildCapturedProfilePosition() : currentPosition;
        Vector3? legacyLookAt = capturedProfileAvailable ? BuildCapturedProfileLookAt() : currentLookAt;
        var legacyProfileId = capturedProfileAvailable
            ? "n4f4-visible-captured"
            : FormatNone(legacyRoute.ProfileId);

        return
        [
            "Legacy composition ON snapshot",
            $"legacy.enabled={legacyRoute.LegacyEnabled}",
            $"legacy.characterVisible={BuildCharacterVisibleLabel()}",
            $"legacy.currentDirH={FormatFloat(legacyDirH)}",
            $"legacy.currentDirV={FormatFloat(legacyDirV)}",
            $"legacy.currentDistance={FormatFloat(legacyDistance)}",
            $"legacy.currentPosition={FormatVector(legacyPosition)}",
            $"legacy.currentLookAt={FormatVector(legacyLookAt)}",
            $"legacy.profileId={legacyProfileId}",
            $"legacy.applyRoute={FormatNone(legacyRoute.ApplyRoute)}",
            $"legacy.clientSelectDataPatched={legacyRoute.ClientSelectDataPatched}",
            $"legacy.refreshDisplayCalled={legacyRoute.RefreshDisplayCalled}",
            $"legacy.updateDisplayDetourCalled={legacyRoute.UpdateDisplayDetourCalled}",
            $"legacy.cameraSource={(capturedProfileAvailable ? "LobbyCamera" : legacyRoute.CameraSource)}",
            "Title Background bridge snapshot",
            $"bridge.enabled={bridgeRoute.BridgeEnabled}",
            $"bridge.characterVisible={BuildCharacterVisibleLabel()}",
            $"bridge.currentDirH={FormatFloat(currentDirH)}",
            $"bridge.currentDirV={FormatFloat(currentDirV)}",
            $"bridge.currentDistance={FormatFloat(currentDistance)}",
            $"bridge.currentPosition={FormatVector(currentPosition)}",
            $"bridge.currentLookAt={FormatVector(currentLookAt)}",
            $"bridge.profileId={titleBackgroundProfileLabel}",
            $"bridge.applyRoute={BuildCameraProfileApplyRoute(cameraProfile, _charaSelectCameraAdapter.RuntimeState.HasCameraPose, GetPhase2GApplyCount() > 0)}",
            $"bridge.clientSelectDataPatched={bridgeRoute.ClientSelectDataPatched}",
            $"bridge.refreshDisplayCalled={bridgeRoute.RefreshDisplayCalled}",
            $"bridge.updateDisplayDetourCalled={bridgeRoute.UpdateDisplayDetourCalled}",
            $"bridge.cameraSource={bridgeRoute.CameraSource}",
            "Delta",
            $"dirH.delta={FormatFloatDelta(currentDirH, legacyDirH)}",
            $"dirV.delta={FormatFloatDelta(currentDirV, legacyDirV)}",
            $"distance.delta={FormatFloatDelta(currentDistance, legacyDistance)}",
            $"position.delta={FormatVectorDelta(currentPosition, legacyPosition)}",
            $"lookAt.delta={FormatVectorDelta(currentLookAt, legacyLookAt)}",
            "Legacy shooting composition camera profile: none observed in reusable profile resolver",
            $"Title Background camera profile: {titleBackgroundProfileLabel}",
            $"Captured camera profile enabled={_configuration.TitleBackgroundCapturedCameraProfileEnabled}",
            $"Captured camera profile source={FormatNone(_configuration.TitleBackgroundCapturedCameraProfileSource)}",
            $"Captured camera profile capturedAt={FormatNone(_configuration.TitleBackgroundCapturedCameraProfileCapturedAt)}",
            $"Captured camera profile dirH={FormatFloat(_configuration.TitleBackgroundCapturedDirH)}",
            $"Captured camera profile dirV={FormatFloat(_configuration.TitleBackgroundCapturedDirV)}",
            $"Captured camera profile distance={FormatFloat(_configuration.TitleBackgroundCapturedDistance)}",
            $"Captured camera profile position={FormatVector(BuildCapturedProfilePosition())}",
            $"Captured camera profile lookAt={FormatVector(BuildCapturedProfileLookAt())}",
            $"Title Background camera profile yaw={FormatFloat(poseAvailable ? titlePose.Yaw : null)}",
            $"Title Background camera profile pitch={FormatFloat(poseAvailable ? titlePose.Pitch : null)}",
            $"Title Background camera profile distance={FormatFloat(poseAvailable ? titlePose.Distance : null)}",
            $"Title Background camera profile lookAtOffset={FormatVector(cameraProfile.LookAtOffset)}",
            $"Title Background camera profile positionOffset={FormatVector(cameraProfile.PositionOffset)}",
            $"Title Background camera profile error={FormatNone(poseAvailable ? string.Empty : poseError)}",
            $"Difference yaw delta={FormatFloatDelta(currentDirH, legacyDirH)}",
            $"Difference pitch delta={FormatFloatDelta(currentDirV, legacyDirV)}",
            $"Difference distance delta={FormatFloatDelta(currentDistance, legacyDistance)}",
            $"Difference lookAt delta={FormatVectorDelta(currentLookAt, legacyLookAt)}",
            $"current camera capture currentDirH={FormatFloat(currentDirH)}",
            $"current camera capture currentDirV={FormatFloat(currentDirV)}",
            $"current camera capture currentDistance={FormatFloat(currentDistance)}",
            $"current camera capture currentPosition={FormatVector(currentPosition)}",
            $"current camera capture currentLookAt={FormatVector(currentLookAt)}",
        ];
    }

    private string BuildCharacterVisibleLabel()
    {
        return _configuration.TitleBackgroundCharacterVisualStatus == TitleBackgroundCharacterVisualStatus.Visible
            ? "True"
            : _configuration.TitleBackgroundCharacterVisualStatus == TitleBackgroundCharacterVisualStatus.Unknown
                ? "Unknown"
                : "False";
    }

    public bool CaptureLegacyVisibleCameraProfile(out string message)
    {
        if (!TryCaptureActiveCameraSnapshot(out var activeCamera, out var activeError))
        {
            message = $"capture failed: {activeError}";
            return false;
        }

        var lobbyCaptured = TryCaptureLobbyCameraSnapshot(out var lobbyCamera, out _);
        var dirH = lobbyCaptured ? lobbyCamera.DirH ?? activeCamera.DirH : activeCamera.DirH;
        var dirV = lobbyCaptured ? lobbyCamera.DirV ?? activeCamera.DirV : activeCamera.DirV;
        var distance = lobbyCaptured ? lobbyCamera.Distance ?? activeCamera.Distance : activeCamera.Distance;
        var capture = TitleBackgroundCapturedCameraProfileLogic.Validate(new TitleBackgroundCapturedCameraProfileInput(
            _configuration.CharaSelectSceneCompositionEnabled,
            _configuration.TitleBackgroundCharacterVisualStatus,
            dirH,
            dirV,
            distance,
            activeCamera.SceneCameraPosition,
            activeCamera.LookAtVector));
        if (!capture.Success)
        {
            message = $"capture failed: {capture.FailureReason}";
            return false;
        }

        _configuration.TitleBackgroundCapturedCameraProfileEnabled = true;
        _configuration.TitleBackgroundCapturedCameraProfileSource = capture.Source;
        _configuration.TitleBackgroundCapturedDirH = capture.DirH;
        _configuration.TitleBackgroundCapturedDirV = capture.DirV;
        _configuration.TitleBackgroundCapturedDistance = capture.Distance;
        _configuration.TitleBackgroundCapturedPositionX = capture.Position.X;
        _configuration.TitleBackgroundCapturedPositionY = capture.Position.Y;
        _configuration.TitleBackgroundCapturedPositionZ = capture.Position.Z;
        _configuration.TitleBackgroundCapturedLookAtX = capture.LookAt.X;
        _configuration.TitleBackgroundCapturedLookAtY = capture.LookAt.Y;
        _configuration.TitleBackgroundCapturedLookAtZ = capture.LookAt.Z;
        _configuration.TitleBackgroundCapturedCameraProfileCapturedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        _configuration.Save();
        message = $"captured {FormatFloat(capture.DirH)} / {FormatFloat(capture.DirV)} / {FormatFloat(capture.Distance)}";
        return true;
    }

    public void ClearLegacyVisibleCameraProfile()
    {
        _configuration.TitleBackgroundCapturedCameraProfileEnabled = false;
        _configuration.TitleBackgroundCapturedCameraProfileSource = string.Empty;
        _configuration.TitleBackgroundCapturedDirH = 0f;
        _configuration.TitleBackgroundCapturedDirV = 0f;
        _configuration.TitleBackgroundCapturedDistance = 0f;
        _configuration.TitleBackgroundCapturedPositionX = 0f;
        _configuration.TitleBackgroundCapturedPositionY = 0f;
        _configuration.TitleBackgroundCapturedPositionZ = 0f;
        _configuration.TitleBackgroundCapturedLookAtX = 0f;
        _configuration.TitleBackgroundCapturedLookAtY = 0f;
        _configuration.TitleBackgroundCapturedLookAtZ = 0f;
        _configuration.TitleBackgroundCapturedCameraProfileCapturedAt = string.Empty;
        _configuration.Save();
    }
}
