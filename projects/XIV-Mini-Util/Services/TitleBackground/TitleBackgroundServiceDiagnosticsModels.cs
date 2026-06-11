// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundServiceDiagnosticsModels.cs
// Description: TitleScreenBackgroundService が使う診断 snapshot / verdict 型を定義する
// Reason: 診断用データ型を TitleScreenBackgroundService の本体ロジックから分離するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundActiveCameraSnapshot(
    Vector3 SceneCameraPosition,
    Vector3 LookAtVector,
    float? DirH,
    float? DirV,
    float? Distance,
    float? InterpDistance,
    float? FovY);

internal readonly record struct TitleBackgroundRuntimeCameraPose(
    float Yaw,
    float Pitch,
    float Distance,
    float LookAtY,
    Vector3 LookAt);

internal readonly record struct TitleBackgroundLobbyCameraSnapshot(
    Vector3 LastLookAtVector,
    float? DirH,
    float? DirV,
    float? Distance,
    float? InterpDistance);

internal readonly record struct TitleBackgroundExpandedLobbyCameraSnapshot(
    bool CameraCurveEnabled,
    TitleBackgroundCurvePointSnapshot LowPoint,
    TitleBackgroundCurvePointSnapshot MidPoint,
    TitleBackgroundCurvePointSnapshot HighPoint);

internal readonly record struct TitleBackgroundCurvePointSnapshot(
    float X,
    float Y);

internal readonly record struct TitleBackgroundCharacterPlacementActorCandidateResult(
    TitleBackgroundCharacterPlacementActorMatchKind MatchKind,
    TitleBackgroundCharacterPlacementActorCandidate? Actor,
    IReadOnlyList<TitleBackgroundCharacterPlacementActorCandidate> Candidates,
    IReadOnlyList<TitleBackgroundCharacterPlacementSourceDiscovery> SourceDiscovery,
    int CandidateCount,
    TitleBackgroundCharacterPlacementObjectTableStats ObjectTableStats,
    string Status,
    string CandidateStatus,
    string CandidateReason,
    string ActorSource,
    string NextNativeSourceToInspect);

internal readonly record struct TitleBackgroundPhase2ECalculateLookAtYCall(
    int CallIndex,
    int? Frame,
    float Distance,
    TitleBackgroundCurvePointSnapshot? LowPoint,
    TitleBackgroundCurvePointSnapshot? MidPoint,
    TitleBackgroundCurvePointSnapshot? HighPoint,
    float? ReturnValue,
    float? ActiveLookAtYBeforeOriginal,
    float? ActiveLookAtYAfterOriginal,
    string Status,
    string Error);

internal readonly record struct TitleBackgroundPhase2FGeneratedCurveCall(
    int CallIndex,
    int? Frame,
    float InputValue,
    float SavedCharacterY,
    float SavedCurveLow,
    float SavedCurveMid,
    float SavedCurveHigh,
    TitleBackgroundExpandedLobbyCameraSnapshot? Before,
    TitleBackgroundExpandedLobbyCameraSnapshot? After,
    float? ActiveDistanceBefore,
    float? ActiveLookAtYBefore,
    float? ActiveDistanceAfter,
    float? ActiveLookAtYAfter,
    string InterestingReason,
    int InterestingPriority,
    string Status,
    string Error);

internal readonly record struct TitleBackgroundPhase2FGeneratedCurveTransitionSummary(
    int Count,
    int? FirstFrame,
    int? LastFrame,
    int SetCameraCurveMidPointCount,
    int CalculateCameraCurveLowAndHighPointCount);

internal readonly record struct TitleBackgroundPhase2CVerdicts(
    string DistancePostRestoreStability,
    string TiltOffsetPostApplyObservableEffect);

internal readonly record struct TitleBackgroundSelfTestVerdict(bool Pass, string Reason)
{
    public static TitleBackgroundSelfTestVerdict Success()
    {
        return new TitleBackgroundSelfTestVerdict(true, string.Empty);
    }

    public static TitleBackgroundSelfTestVerdict Fail(string reason)
    {
        return new TitleBackgroundSelfTestVerdict(false, reason);
    }
}

internal readonly record struct TitleBackgroundPhase2CTimelineSnapshot(
    int Frame,
    bool ActiveCameraCaptured,
    string ActiveCameraError,
    float? DirH,
    float? DirV,
    float? Distance,
    float? InterpDistance,
    Vector3? SceneCameraPosition,
    Vector3? SceneCameraLookAtVector,
    bool LobbyCameraCaptured,
    string LobbyCameraError,
    Vector3? LobbyLastLookAtVector,
    float? LobbyDirH,
    float? LobbyDirV,
    float? LobbyDistance,
    float? LobbyInterpDistance,
    bool ExpandedLobbyCameraCaptured,
    string ExpandedLobbyCameraError,
    bool? CameraCurveEnabled,
    TitleBackgroundCurvePointSnapshot? LowPoint,
    TitleBackgroundCurvePointSnapshot? MidPoint,
    TitleBackgroundCurvePointSnapshot? HighPoint)
{
    public static TitleBackgroundPhase2CTimelineSnapshot Missing(int frame)
    {
        return new TitleBackgroundPhase2CTimelineSnapshot(
            frame,
            false,
            "missing",
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            "missing",
            null,
            null,
            null,
            null,
            null,
            false,
            "missing",
            null,
            null,
            null,
            null);
    }
}

internal readonly record struct TitleBackgroundCameraProbeTimelineSnapshot(
    Vector3? SceneCameraPosition,
    Vector3? LookAtVector,
    string Status,
    string Error);

internal readonly record struct TitleBackgroundCameraProbeLobbyUpdateSnapshot(
    Vector3? PreSceneCameraPosition,
    Vector3? PreLookAtVector,
    string PreStatus,
    string PreError,
    Vector3? PostSceneCameraPosition,
    Vector3? PostLookAtVector,
    string PostStatus,
    string PostError);

internal enum TitleBackgroundCameraProbeTimelineEventKind
{
    FixOn,
    LobbyUpdate,
    LoadLobbyScene,
    CreateScene,
}
