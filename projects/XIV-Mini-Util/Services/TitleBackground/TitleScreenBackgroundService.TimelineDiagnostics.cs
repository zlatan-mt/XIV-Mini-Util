// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.TimelineDiagnostics.cs
// Description: TitleBackground の Phase2 timeline / generated curve 診断ヘルパーを提供する
// Reason: timeline 診断処理を TitleScreenBackgroundService の本体ロジックから分離するため
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    private void StartPhase2CTimelineObservation()
    {
        _probeTimeline.Phase2CTimelineFrameCounter = 0;
        _probeTimeline.Phase2CTimelineStatus = "collecting";
        _probeTimeline.Phase2CTimelineError = string.Empty;
        _probeTimeline.Phase2CTimelineSnapshots.Clear();
        _phaseRecording.Phase2MPlacementFrames.Clear();
        _phaseRecording.Phase2MPlacementCaptureSceneGeneration = 0;
        _phaseRecording.Phase2MPlacementCaptureReason = "reset";
        _phaseRecording.Phase2MPlacementSkippedPostLoginCount = 0;
        _phaseRecording.Phase2MPlacementSkippedInactiveCount = 0;
        _phaseRecording.Phase2MPlacementSkippedSceneGenerationCount = 0;
        _phaseRecording.Phase2MPlacementLastSkipReason = "none";
        _phaseRecording.Phase2MExperimentalWriteCount = 0;
        _cameraRestoreCurve.RuntimeRestoreAppliedFrame = null;
        _cameraRestoreCurve.CurveApplyAppliedFrame = null;
        _cameraRestoreCurve.CurveApplyRequestedMid = null;
        _cameraRestoreCurve.CurveApplyReadBackValueImmediatelyAfterWrite = null;
        _cameraRestoreCurve.CurveApplyImmediateReadBackStatus = "not-run";
        _cameraRestoreCurve.CurveApplyActiveCameraBefore = null;
        _cameraRestoreCurve.CurveApplyActiveCameraAfter = null;
        _cameraRestoreCurve.CurveApplyActiveCameraBeforeStatus = "not-run";
        _cameraRestoreCurve.CurveApplyActiveCameraAfterStatus = "not-run";
    }

    private void CapturePhase2CTimelineOnFrameworkUpdate()
    {
        if (_probeTimeline.Phase2CTimelineFrameCounter < 0)
        {
            return;
        }

        _probeTimeline.Phase2CTimelineFrameCounter++;
        if (Array.IndexOf(CameraProbeTimelineFrames, _probeTimeline.Phase2CTimelineFrameCounter) >= 0)
        {
            CapturePhase2CTimelineFrame(_probeTimeline.Phase2CTimelineFrameCounter);
        }

        if (_probeTimeline.Phase2CTimelineFrameCounter >= CameraProbeTimelineFrames[^1])
        {
            _probeTimeline.Phase2CTimelineFrameCounter = -1;
            _probeTimeline.Phase2CTimelineStatus = "complete";
        }
    }

    private void CapturePhase2CTimelineFrame(int frame)
    {
        if (_probeTimeline.Phase2CTimelineSnapshots.ContainsKey(frame))
        {
            return;
        }

        var activeCaptured = TryCaptureActiveCameraSnapshot(out var activeCamera, out var activeError);
        var lobbyCaptured = TryCaptureLobbyCameraSnapshot(out var lobbyCamera, out var lobbyError);
        var expandedCaptured = TryCaptureExpandedLobbyCameraSnapshot(out var expandedLobbyCamera, out var expandedError);
        _probeTimeline.Phase2CTimelineSnapshots[frame] = new TitleBackgroundPhase2CTimelineSnapshot(
            frame,
            activeCaptured,
            activeCaptured ? string.Empty : activeError,
            activeCaptured ? activeCamera.DirH : null,
            activeCaptured ? activeCamera.DirV : null,
            activeCaptured ? activeCamera.Distance : null,
            activeCaptured ? activeCamera.InterpDistance : null,
            activeCaptured ? activeCamera.SceneCameraPosition : null,
            activeCaptured ? activeCamera.LookAtVector : null,
            lobbyCaptured,
            lobbyCaptured ? string.Empty : lobbyError,
            lobbyCaptured ? lobbyCamera.LastLookAtVector : null,
            lobbyCaptured ? lobbyCamera.DirH : null,
            lobbyCaptured ? lobbyCamera.DirV : null,
            lobbyCaptured ? lobbyCamera.Distance : null,
            lobbyCaptured ? lobbyCamera.InterpDistance : null,
            expandedCaptured,
            expandedCaptured ? string.Empty : expandedError,
            expandedCaptured ? expandedLobbyCamera.CameraCurveEnabled : null,
            expandedCaptured ? expandedLobbyCamera.LowPoint : null,
            expandedCaptured ? expandedLobbyCamera.MidPoint : null,
            expandedCaptured ? expandedLobbyCamera.HighPoint : null);

        _probeTimeline.Phase2CTimelineStatus = frame >= CameraProbeTimelineFrames[^1]
            ? "complete"
            : "collecting";
        _probeTimeline.Phase2CTimelineError = activeCaptured || lobbyCaptured || expandedCaptured
            ? string.Empty
            : $"frame {frame}: active={activeError}; lobby={lobbyError}; expandedLobby={expandedError}";

        CaptureCharacterPlacementPlacementFrame(frame, frame == 0 ? "scene-ready-accepted" : "timeline");
    }

    private void CaptureCharacterPlacementPlacementFrame(int frame, string reason)
    {
        if (!TitleBackgroundCharacterPlacementDiagnostic.ShouldCaptureFrame(frame)
            || _phaseRecording.Phase2MPlacementFrames.ContainsKey(frame))
        {
            return;
        }

        var captureGate = TitleBackgroundCharacterSourceCaptureGate.Evaluate(
            _clientState.IsLoggedIn,
            _charaSelectTitleBackgroundSessionActive,
            _activeCharaSelectSceneGeneration,
            _charaSelectCameraAdapter.RuntimeState.SceneGeneration);
        if (!captureGate.Allowed)
        {
            switch (captureGate.Status)
            {
                case "skipped-post-login":
                    _phaseRecording.Phase2MPlacementSkippedPostLoginCount++;
                    break;
                case "skipped-inactive-chara-select":
                    _phaseRecording.Phase2MPlacementSkippedInactiveCount++;
                    break;
                case "skipped-scene-generation-mismatch":
                    _phaseRecording.Phase2MPlacementSkippedSceneGenerationCount++;
                    break;
            }

            _phaseRecording.Phase2MPlacementLastSkipReason = captureGate.Status;
            return;
        }

        var activeCaptured = TryCaptureActiveCameraSnapshot(out var activeCamera, out _);
        var lobbyCaptured = TryCaptureLobbyCameraSnapshot(out var lobbyCamera, out _);
        var configuredCharacterPosition = _charaSelectCameraAdapter.Input.CharacterPosition;
        var configuredFocus = new Vector3(
            _configuration.TitleBackgroundFocusX,
            _configuration.TitleBackgroundFocusY,
            _configuration.TitleBackgroundFocusZ);
        var nativeLookAtY = activeCaptured
            ? activeCamera.LookAtVector.Y
            : lobbyCaptured
                ? lobbyCamera.LastLookAtVector.Y
                : (float?)null;
        var activeLookAt = activeCaptured ? activeCamera.LookAtVector : (Vector3?)null;
        var activeCameraPosition = activeCaptured ? activeCamera.SceneCameraPosition : (Vector3?)null;

        var nativeCharacterSource = TitleBackgroundCharacterSourceProbe.Capture(frame);
        if (TitleBackgroundCharacterSourceProbe.TryReadCurrentCharacterAim(out var drawPosition, out var drawRotation))
        {
            _characterPlacement.LastPreLoginCharacterDrawPosition = drawPosition;
            _characterPlacement.LastPreLoginCharacterDrawRotation = drawRotation;
            _characterPlacement.PreLoginCharacterDrawObservedCount++;
        }

        var actorResult = CaptureCharacterPlacementActorCandidate(
            configuredCharacterPosition,
            activeLookAt,
            activeCameraPosition,
            nativeCharacterSource);
        var actor = actorResult.Actor;
        var actorPosition = actor?.Position;
        _phaseRecording.Phase2MPlacementCaptureSceneGeneration = _charaSelectCameraAdapter.RuntimeState.SceneGeneration;
        _phaseRecording.Phase2MPlacementCaptureReason = reason;

        _phaseRecording.Phase2MPlacementFrames[frame] = new TitleBackgroundCharacterPlacementFrame(
            frame,
            reason,
            activeCaptured,
            activeCaptured ? activeCamera.SceneCameraPosition : null,
            activeCaptured ? activeCamera.LookAtVector : null,
            activeCaptured ? activeCamera.DirH : null,
            activeCaptured ? activeCamera.DirV : null,
            activeCaptured ? activeCamera.Distance : null,
            lobbyCaptured,
            lobbyCaptured ? lobbyCamera.LastLookAtVector : null,
            lobbyCaptured ? lobbyCamera.DirH : null,
            lobbyCaptured ? lobbyCamera.DirV : null,
            lobbyCaptured ? lobbyCamera.Distance : null,
            lobbyCaptured ? lobbyCamera.InterpDistance : null,
            actorResult.MatchKind,
            actor,
            actorResult.Candidates,
            actorResult.SourceDiscovery,
            actorResult.CandidateCount,
            actorResult.Status,
            actorResult.ObjectTableStats,
            actorResult.CandidateStatus,
            actorResult.CandidateReason,
            actorResult.ActorSource,
            actorResult.NextNativeSourceToInspect,
            "unavailable",
            null,
            actorPosition.HasValue && activeCaptured
                ? Vector3.Distance(actorPosition.Value, activeCamera.SceneCameraPosition)
                : null,
            actorPosition.HasValue && activeCaptured
                ? actorPosition.Value - activeCamera.LookAtVector
                : null,
            configuredCharacterPosition,
            _charaSelectCameraAdapter.Input.CharacterRotation,
            _charaSelectCameraAdapter.Curve.Low,
            _charaSelectCameraAdapter.Curve.Mid,
            _charaSelectCameraAdapter.Curve.High,
            actorPosition.HasValue
                ? actorPosition.Value.Y - configuredCharacterPosition.Y
                : null,
            actorPosition.HasValue
                ? actorPosition.Value.Y - configuredFocus.Y
                : null,
            actorPosition.HasValue && nativeLookAtY.HasValue
                ? actorPosition.Value.Y - nativeLookAtY.Value
                : null,
            nativeCharacterSource);
    }

    private TitleBackgroundCharacterPlacementActorCandidateResult CaptureCharacterPlacementActorCandidate(
        Vector3 configuredCharacterPosition,
        Vector3? activeLookAt,
        Vector3? activeCameraPosition,
        TitleBackgroundCharacterSourceSnapshot nativeCharacterSource)
    {
        try
        {
            var scanned = new List<TitleBackgroundCharacterPlacementActorCandidate>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var sources = new List<TitleBackgroundCharacterPlacementSourceDiscovery>();
            var length = Math.Max(0, _objectTable.Length);
            var beforeCount = scanned.Count;
            for (var index = 0; index < length; index++)
            {
                AddCharacterPlacementScannedObject(scanned, seenKeys, index, "ObjectTable", _objectTable[index], configuredCharacterPosition, activeLookAt, activeCameraPosition);
            }
            sources.Add(BuildCharacterPlacementSourceDiscovery("ObjectTable", true, length, scanned, beforeCount, string.Empty));

            var sourceIndex = 0;
            beforeCount = scanned.Count;
            foreach (var gameObject in _objectTable.PlayerObjects)
            {
                sourceIndex++;
                AddCharacterPlacementScannedObject(scanned, seenKeys, sourceIndex, "PlayerObjects", gameObject, configuredCharacterPosition, activeLookAt, activeCameraPosition);
            }
            sources.Add(BuildCharacterPlacementSourceDiscovery("PlayerObjects", true, sourceIndex, scanned, beforeCount, string.Empty));

            sourceIndex = 0;
            beforeCount = scanned.Count;
            foreach (var gameObject in _objectTable.CharacterManagerObjects)
            {
                sourceIndex++;
                AddCharacterPlacementScannedObject(scanned, seenKeys, sourceIndex, "CharacterManagerObjects", gameObject, configuredCharacterPosition, activeLookAt, activeCameraPosition);
            }
            sources.Add(BuildCharacterPlacementSourceDiscovery("CharacterManagerObjects", true, sourceIndex, scanned, beforeCount, string.Empty));
            var stats = BuildCharacterPlacementObjectTableStats(scanned);

            var nativeCandidate = TryCreateNativeCharacterPlacementActorCandidate(
                nativeCharacterSource,
                configuredCharacterPosition,
                activeLookAt,
                activeCameraPosition);
            if (nativeCandidate.HasValue
                && seenKeys.Add(BuildCharacterPlacementCandidateKey(nativeCandidate.Value)))
            {
                scanned.Add(nativeCandidate.Value);
            }

            sources.Add(new TitleBackgroundCharacterPlacementSourceDiscovery(
                TitleBackgroundCharacterSourceEvaluation.SourceName,
                nativeCharacterSource.ReadStatus == "read",
                nativeCharacterSource.HasCharacter ? 1 : 0,
                nativeCandidate.HasValue ? 1 : 0,
                nativeCharacterSource.Error,
                nativeCharacterSource.HasNonZeroTransform ? 1 : 0,
                nativeCharacterSource.DrawObjectNonNull ? 1 : 0,
                0,
                nativeCharacterSource.ReadStatus,
                nativeCharacterSource.CaptureContext,
                nativeCharacterSource.CharacterAddress));
            sources.Add(new TitleBackgroundCharacterPlacementSourceDiscovery("ClientObjectManager", false, 0, 0, "not-exposed-through-managed-api"));
            sources.Add(new TitleBackgroundCharacterPlacementSourceDiscovery("UIStage CharaSelect model source", false, 0, 0, "native-source-not-resolved"));
            sources.Add(new TitleBackgroundCharacterPlacementSourceDiscovery("DrawObject owner/source", false, 0, 0, "reverse-lookup-not-exposed-through-managed-api"));

            var candidates = scanned
                .Where(candidate => candidate.PlayerLike
                    || candidate.BattleCharacterLike
                    || candidate.EventNpcLike
                    || candidate.CompanionLike
                    || candidate.Named
                    || candidate.NearConfiguredCharacter
                    || candidate.NearCameraLookAt
                    || candidate.NearCameraPosition)
                .OrderByDescending(candidate => candidate.PlayerLike)
                .ThenByDescending(candidate => candidate.BattleCharacterLike)
                .ThenBy(candidate => candidate.DistanceFromConfiguredCharacter ?? float.MaxValue)
                .ThenBy(candidate => candidate.DistanceFromActiveLookAt ?? float.MaxValue)
                .Take(16)
                .ToArray();
            var nativeResolvedCandidates = candidates
                .Where(candidate => candidate.Source == TitleBackgroundCharacterSourceEvaluation.SourceName
                    && !IsZeroCharacterPlacementPosition(candidate.Position))
                .ToArray();
            var matchingCandidates = nativeResolvedCandidates.Length > 0
                ? nativeResolvedCandidates
                : candidates
                    .Where(candidate => candidate.PlayerLike
                        || candidate.BattleCharacterLike
                        || candidate.NearConfiguredCharacter
                        || candidate.NearCameraLookAt)
                    .ToArray();

            if (matchingCandidates.Length == 0)
            {
                var reason = candidates.Length == 0
                    ? "objectTable-unavailable-or-not-exposed"
                    : "no-player-or-near-focus-candidate";
                return new TitleBackgroundCharacterPlacementActorCandidateResult(
                    TitleBackgroundCharacterPlacementActorMatchKind.None,
                    null,
                    candidates,
                    sources,
                    candidates.Length,
                    stats,
                    "not-observed",
                    "none",
                    reason,
                    "objectTable-unavailable-or-not-exposed",
                    "native character-select actor manager or lobby UI character instance");
            }

            var stableCandidates = FilterCharacterPlacementStableCandidates(matchingCandidates);
            if (stableCandidates.Length == 1)
            {
                return new TitleBackgroundCharacterPlacementActorCandidateResult(
                    TitleBackgroundCharacterPlacementActorMatchKind.Single,
                    stableCandidates[0],
                    candidates,
                    sources,
                    candidates.Length,
                    stats,
                    "observed",
                    "single",
                    "single-stable-candidate",
                    stableCandidates[0].Source,
                    "none");
            }

            if (matchingCandidates.Length == 1)
            {
                return new TitleBackgroundCharacterPlacementActorCandidateResult(
                    TitleBackgroundCharacterPlacementActorMatchKind.Single,
                    matchingCandidates[0],
                    candidates,
                    sources,
                    candidates.Length,
                    stats,
                    "observed",
                    "single",
                    "single-candidate",
                    matchingCandidates[0].Source,
                    "none");
            }

            return new TitleBackgroundCharacterPlacementActorCandidateResult(
                TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous,
                matchingCandidates[0],
                candidates,
                sources,
                candidates.Length,
                stats,
                "ambiguous",
                "ambiguous",
                $"multiple-candidates:{matchingCandidates.Length}",
                "ambiguous",
                "CharacterManager");
        }
        catch (Exception ex)
        {
            return new TitleBackgroundCharacterPlacementActorCandidateResult(
                TitleBackgroundCharacterPlacementActorMatchKind.None,
                null,
                [],
                [new TitleBackgroundCharacterPlacementSourceDiscovery("ObjectTable", false, 0, 0, ex.GetType().Name)],
                0,
                default,
                "error",
                "none",
                $"error:{ex.GetType().Name}",
                "objectTable-unavailable-or-not-exposed",
                "native character-select actor manager or lobby UI character instance");
        }
    }

    private static void AddCharacterPlacementScannedObject(
        List<TitleBackgroundCharacterPlacementActorCandidate> candidates,
        HashSet<string> seenKeys,
        int sourceIndex,
        string source,
        IGameObject? gameObject,
        Vector3 configuredCharacterPosition,
        Vector3? activeLookAt,
        Vector3? activeCameraPosition)
    {
        if (gameObject == null)
        {
            return;
        }

        var candidate = TryCreateCharacterPlacementActorCandidate(sourceIndex, source, gameObject, configuredCharacterPosition, activeLookAt, activeCameraPosition);
        if (!candidate.HasValue)
        {
            return;
        }

        if (seenKeys.Add(BuildCharacterPlacementCandidateKey(candidate.Value)))
        {
            candidates.Add(candidate.Value);
        }
    }

    private static TitleBackgroundCharacterPlacementSourceDiscovery BuildCharacterPlacementSourceDiscovery(
        string name,
        bool available,
        int count,
        IReadOnlyList<TitleBackgroundCharacterPlacementActorCandidate> scanned,
        int startIndex,
        string error)
    {
        var localCandidates = scanned
            .Skip(startIndex)
            .Where(candidate => candidate.Source == name)
            .ToArray();
        return new TitleBackgroundCharacterPlacementSourceDiscovery(
            name,
            available,
            count,
            localCandidates.Length,
            error,
            localCandidates.Count(candidate => !IsZeroCharacterPlacementPosition(candidate.Position)),
            localCandidates.Count(candidate => candidate.DrawObjectNonNull),
            localCandidates.Count(candidate => candidate.ModelLikeNonNull));
    }

    private TitleBackgroundCharacterPlacementActorCandidate[] FilterCharacterPlacementStableCandidates(TitleBackgroundCharacterPlacementActorCandidate[] candidates)
    {
        return candidates.Where(IsStableCharacterPlacementCandidate).ToArray();
    }

    private bool IsStableCharacterPlacementCandidate(TitleBackgroundCharacterPlacementActorCandidate candidate)
    {
        var key = BuildCharacterPlacementCandidateKey(candidate);
        var matched = _phaseRecording.Phase2MPlacementFrames.Values
            .SelectMany(frame => frame.ObjectCandidates)
            .Where(existing => BuildCharacterPlacementCandidateKey(existing) == key)
            .ToArray();
        return matched.Length >= 2
            && matched.All(existing => Vector3.Distance(existing.Position, candidate.Position) <= TitleBackgroundCameraProbeReport.StabilizationVectorTolerance);
    }

    private static string BuildCharacterPlacementCandidateKey(TitleBackgroundCharacterPlacementActorCandidate candidate)
    {
        if (candidate.GameObjectId != 0)
        {
            return $"gameObject:{candidate.GameObjectId:X}";
        }

        if (candidate.EntityId != 0)
        {
            return $"entity:{candidate.EntityId:X}";
        }

        if (candidate.Address != nint.Zero)
        {
            return $"address:{candidate.Address.ToInt64():X}";
        }

        return $"{candidate.Source}:{candidate.ObjectIndex}:{candidate.ObjectKind}:{candidate.Name}";
    }

    private static bool IsZeroCharacterPlacementPosition(Vector3 position)
    {
        return Math.Abs(position.X) <= 0.001f
            && Math.Abs(position.Y) <= 0.001f
            && Math.Abs(position.Z) <= 0.001f;
    }

    private static TitleBackgroundCharacterPlacementObjectTableStats BuildCharacterPlacementObjectTableStats(IReadOnlyCollection<TitleBackgroundCharacterPlacementActorCandidate> candidates)
    {
        return new TitleBackgroundCharacterPlacementObjectTableStats(
            candidates.Count,
            candidates.Count(candidate => candidate.Named),
            candidates.Count(candidate => candidate.PlayerLike),
            candidates.Count(candidate => candidate.BattleCharacterLike),
            candidates.Count(candidate => candidate.EventNpcLike),
            candidates.Count(candidate => candidate.CompanionLike),
            candidates.Count(candidate => candidate.NearCameraLookAt || candidate.NearCameraPosition),
            candidates.Count(candidate => candidate.NearConfiguredCharacter));
    }

    private static float? TryReadFloatProperty(object source, string propertyName, List<string> errors)
    {
        try
        {
            var value = source.GetType().GetProperty(propertyName)?.GetValue(source);
            return value switch
            {
                float f when float.IsFinite(f) => f,
                double d when double.IsFinite(d) => (float)d,
                _ => null,
            };
        }
        catch (Exception ex)
        {
            errors.Add($"{propertyName}:{ex.GetType().Name}");
            return null;
        }
    }

    private static uint? TryReadUIntProperty(object source, string propertyName, List<string> errors)
    {
        try
        {
            var value = source.GetType().GetProperty(propertyName)?.GetValue(source);
            return value switch
            {
                uint u => u,
                ushort u16 => u16,
                int i when i >= 0 => (uint)i,
                _ => null,
            };
        }
        catch (Exception ex)
        {
            errors.Add($"{propertyName}:{ex.GetType().Name}");
            return null;
        }
    }

    private static object? TryReadObjectProperty(object source, string propertyName, List<string> errors)
    {
        try
        {
            return source.GetType().GetProperty(propertyName)?.GetValue(source);
        }
        catch (Exception ex)
        {
            errors.Add($"{propertyName}:{ex.GetType().Name}");
            return null;
        }
    }

    private static string FormatReflectObject(object? value)
    {
        if (value == null)
        {
            return "none";
        }

        if (value is nint pointer)
        {
            return pointer == nint.Zero ? "0x0" : FormatAddress(pointer);
        }

        if (value is nuint upointer)
        {
            return upointer == 0 ? "0x0" : $"0x{upointer:X}";
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? "present" : text;
    }

    private TitleBackgroundCharacterPlacementObjectTableStats GetLatestCharacterPlacementObjectTableStats()
    {
        return _phaseRecording.Phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.ObjectTableStats)
            .FirstOrDefault();
    }

    private string GetLatestCharacterPlacementActorCandidateStatus()
    {
        return _phaseRecording.Phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.ActorCandidateStatus)
            .FirstOrDefault() ?? "none";
    }

    private string GetLatestCharacterPlacementActorCandidateReason()
    {
        return _phaseRecording.Phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.ActorCandidateReason)
            .FirstOrDefault() ?? "none";
    }

    private string GetLatestCharacterPlacementActorSource()
    {
        return _phaseRecording.Phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.ActorSource)
            .FirstOrDefault() ?? "none";
    }

    private string GetLatestCharacterPlacementNextNativeSourceToInspect()
    {
        return _phaseRecording.Phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.NextNativeSourceToInspect)
            .FirstOrDefault() ?? "none";
    }

    private IReadOnlyList<TitleBackgroundCharacterPlacementSourceDiscovery> GetLatestCharacterPlacementSourceDiscovery()
    {
        return _phaseRecording.Phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.SourceDiscovery)
            .FirstOrDefault() ?? [];
    }

    private static TitleBackgroundCharacterPlacementActorCandidate? TryCreateCharacterPlacementActorCandidate(
        int sourceIndex,
        string source,
        IGameObject gameObject,
        Vector3 configuredCharacterPosition,
        Vector3? activeLookAt,
        Vector3? activeCameraPosition)
    {
        var position = gameObject.Position;
        if (!TitleBackgroundCameraMath.IsFiniteVector(position))
        {
            return null;
        }

        var kind = gameObject.ObjectKind;
        var name = gameObject.Name.ToString();
        var named = !string.IsNullOrWhiteSpace(name);
        var playerLike = kind == ObjectKind.Pc;
        var eventNpcLike = kind == ObjectKind.EventNpc;
        var companionLike = kind == ObjectKind.Companion
            || kind == ObjectKind.Mount
            || kind == ObjectKind.Ornament;
        var battleCharacterLike = playerLike
            || kind == ObjectKind.BattleNpc
            || kind == ObjectKind.EventNpc;
        var distanceFromConfiguredCharacter = Vector3.Distance(position, configuredCharacterPosition);
        var distanceFromActiveLookAt = activeLookAt.HasValue
            ? Vector3.Distance(position, activeLookAt.Value)
            : (float?)null;
        var distanceFromActiveCameraPosition = activeCameraPosition.HasValue
            ? Vector3.Distance(position, activeCameraPosition.Value)
            : (float?)null;
        var nearConfiguredCharacter = distanceFromConfiguredCharacter <= 100f;
        var nearCameraLookAt = distanceFromActiveLookAt is <= 100f;
        var nearCameraPosition = distanceFromActiveCameraPosition is <= 100f;
        var categoryReasons = new List<string>();
        if (playerLike) categoryReasons.Add("PlayerCharacter");
        if (battleCharacterLike) categoryReasons.Add("BattleChara");
        if (eventNpcLike) categoryReasons.Add("EventNpc");
        if (companionLike) categoryReasons.Add("CompanionLike");
        if (named) categoryReasons.Add("Named");
        if (nearConfiguredCharacter) categoryReasons.Add("NearConfiguredCharacter");
        if (nearCameraLookAt) categoryReasons.Add("NearCameraLookAt");
        if (nearCameraPosition) categoryReasons.Add("NearCameraPosition");
        var safeReadErrors = new List<string>();
        var scale = TryReadFloatProperty(gameObject, "Scale", safeReadErrors);
        var hitboxRadius = TryReadFloatProperty(gameObject, "HitboxRadius", safeReadErrors);
        var currentHp = TryReadUIntProperty(gameObject, "CurrentHp", safeReadErrors);
        var maxHp = TryReadUIntProperty(gameObject, "MaxHp", safeReadErrors);
        var drawObject = TryReadObjectProperty(gameObject, "DrawObject", safeReadErrors);
        var model = TryReadObjectProperty(gameObject, "Model", safeReadErrors);
        var customize = TryReadObjectProperty(gameObject, "Customize", safeReadErrors);
        var statusFlags = TryReadObjectProperty(gameObject, "StatusFlags", safeReadErrors);
        var targetableStatus = gameObject.IsTargetable;
        var drawObjectText = FormatReflectObject(drawObject);
        var modelText = FormatReflectObject(model);
        var customizeText = FormatReflectObject(customize);
        var drawObjectNonNull = drawObject != null && drawObjectText != "0x0" && drawObjectText != "none";
        var modelLikeNonNull = model != null && modelText != "0x0" && modelText != "none";
        var (score, scoreReason) = ScoreCharacterPlacementCandidate(
            position,
            named,
            targetableStatus,
            drawObjectNonNull,
            modelLikeNonNull,
            nearConfiguredCharacter,
            nearCameraLookAt,
            gameObject.GameObjectId,
            gameObject.EntityId,
            source);

        return new TitleBackgroundCharacterPlacementActorCandidate(
            sourceIndex,
            source,
            gameObject.ObjectIndex,
            kind.ToString(),
            name,
            gameObject.GameObjectId,
            gameObject.EntityId,
            gameObject.Address,
            position,
            gameObject.Rotation,
            scale,
            hitboxRadius,
            currentHp,
            maxHp,
            targetableStatus,
            targetableStatus ? "targetable" : "not-targetable",
            targetableStatus ? "selectable-hint" : "unknown",
            FormatNone(statusFlags?.ToString() ?? string.Empty),
            customizeText,
            modelText,
            drawObjectText,
            drawObjectNonNull,
            modelText,
            modelLikeNonNull,
            safeReadErrors.Count == 0 ? "none" : string.Join(",", safeReadErrors.Distinct()),
            named,
            playerLike,
            battleCharacterLike,
            eventNpcLike,
            companionLike,
            gameObject.IsTargetable,
            distanceFromConfiguredCharacter,
            distanceFromActiveLookAt,
            distanceFromActiveCameraPosition,
            position.Y - configuredCharacterPosition.Y,
            nearConfiguredCharacter,
            nearCameraLookAt,
            nearCameraPosition,
            categoryReasons.Count == 0 ? "uncategorized" : string.Join(",", categoryReasons),
            score,
            scoreReason);
    }

    private static TitleBackgroundCharacterPlacementActorCandidate? TryCreateNativeCharacterPlacementActorCandidate(
        TitleBackgroundCharacterSourceSnapshot snapshot,
        Vector3 configuredCharacterPosition,
        Vector3? activeLookAt,
        Vector3? activeCameraPosition)
    {
        if (!snapshot.HasCharacter || !TitleBackgroundCameraMath.IsFiniteVector(snapshot.Position))
        {
            return null;
        }

        var distanceFromConfiguredCharacter = Vector3.Distance(snapshot.Position, configuredCharacterPosition);
        var distanceFromActiveLookAt = activeLookAt.HasValue
            ? Vector3.Distance(snapshot.Position, activeLookAt.Value)
            : (float?)null;
        var distanceFromActiveCameraPosition = activeCameraPosition.HasValue
            ? Vector3.Distance(snapshot.Position, activeCameraPosition.Value)
            : (float?)null;
        var nearConfiguredCharacter = distanceFromConfiguredCharacter <= 100f;
        var nearCameraLookAt = distanceFromActiveLookAt is <= 100f;
        var nearCameraPosition = distanceFromActiveCameraPosition is <= 100f;
        var visibleHint = snapshot.DrawObjectNonNull;
        var (score, scoreReason) = ScoreCharacterPlacementCandidate(
            snapshot.Position,
            named: false,
            visibleHint,
            snapshot.DrawObjectNonNull,
            modelLikeNonNull: false,
            nearConfiguredCharacter,
            nearCameraLookAt,
            gameObjectId: 0,
            snapshot.EntityId,
            TitleBackgroundCharacterSourceEvaluation.SourceName);

        return new TitleBackgroundCharacterPlacementActorCandidate(
            snapshot.ClientObjectIndex >= 0 ? snapshot.ClientObjectIndex : snapshot.ObjectIndex,
            TitleBackgroundCharacterSourceEvaluation.SourceName,
            snapshot.ObjectIndex,
            snapshot.ObjectKind,
            string.Empty,
            0,
            snapshot.EntityId,
            snapshot.CharacterAddress,
            snapshot.Position,
            snapshot.Rotation,
            snapshot.Scale,
            snapshot.HitboxRadius,
            null,
            null,
            false,
            visibleHint ? "draw-object-present" : "draw-object-missing",
            "character-select-current",
            snapshot.ContentId == 0 ? "contentId-missing" : "contentId-present",
            snapshot.Customize,
            "none",
            snapshot.DrawObjectNonNull ? $"0x{snapshot.DrawObjectAddress.ToInt64():X}" : "none",
            snapshot.DrawObjectNonNull,
            "none",
            false,
            snapshot.Error,
            false,
            true,
            true,
            false,
            false,
            visibleHint,
            distanceFromConfiguredCharacter,
            distanceFromActiveLookAt,
            distanceFromActiveCameraPosition,
            snapshot.Position.Y - configuredCharacterPosition.Y,
            nearConfiguredCharacter,
            nearCameraLookAt,
            nearCameraPosition,
            "CharaSelectCurrentCharacter",
            score,
            scoreReason);
    }

    private static (int Score, string Reason) ScoreCharacterPlacementCandidate(
        Vector3 position,
        bool named,
        bool visibleHint,
        bool drawObjectNonNull,
        bool modelLikeNonNull,
        bool nearConfiguredCharacter,
        bool nearCameraLookAt,
        ulong gameObjectId,
        uint entityId,
        string source)
    {
        var score = 0;
        var reasons = new List<string>();
        var zeroTransform = Math.Abs(position.X) <= 0.001f && Math.Abs(position.Y) <= 0.001f && Math.Abs(position.Z) <= 0.001f;
        AddScore(!zeroTransform, 30, "non-zero-world-position");
        AddScore(nearConfiguredCharacter, 20, "near-configured-character");
        AddScore(nearCameraLookAt, 10, "near-active-camera-lookAt");
        AddScore(drawObjectNonNull, 20, "drawObject-non-null");
        AddScore(modelLikeNonNull, 15, "model-like-non-null");
        AddScore(visibleHint, 10, "visible-hint");
        AddScore(named, 8, "named");
        AddScore(gameObjectId != 0, 5, "objectId-valid");
        AddScore(entityId != 0 && entityId != 0xE0000000, 5, "entityId-valid");
        AddScore(source == "CharacterManagerObjects", 3, "source-priority-character-manager");
        AddScore(source == "PlayerObjects", 2, "source-priority-player-objects");
        AddScore(source == TitleBackgroundCharacterSourceEvaluation.SourceName, 50, "source-priority-chara-select-current");

        if (zeroTransform)
        {
            score -= 40;
            reasons.Add("all-zero-transform-penalty:-40");
        }

        return (score, reasons.Count == 0 ? "none" : string.Join(",", reasons));

        void AddScore(bool condition, int value, string reason)
        {
            if (!condition)
            {
                return;
            }

            score += value;
            reasons.Add($"{reason}:+{value}");
        }
    }

    private void EvaluateCharacterPlacementExperimentalApply(TitleBackgroundCharacterPlacementSummary summary)
    {
        var mode = _configuration.TitleBackgroundCharacterPlacementExperimentalApplyMode;
        var status = TitleBackgroundDeliveryDiagnostic.EvaluateExperimentalActorPlacement(
            mode,
            summary,
            _activeCharaSelectSceneGeneration > 0
                && _phaseRecording.Phase2MPlacementCaptureSceneGeneration == _activeCharaSelectSceneGeneration,
            _charaSelectTitleBackgroundSessionActive,
            _clientState.IsLoggedIn);

        if (mode == TitleBackgroundCharacterPlacementExperimentalApplyMode.None)
        {
            _phaseRecording.Phase2MExperimentalLastStatus = status;
            return;
        }

        if (status != "ready")
        {
            _phaseRecording.Phase2MExperimentalSkippedCount++;
            _phaseRecording.Phase2MExperimentalLastStatus = status;
            return;
        }

        _phaseRecording.Phase2MExperimentalSkippedCount++;
        _phaseRecording.Phase2MExperimentalLastStatus = mode switch
        {
            TitleBackgroundCharacterPlacementExperimentalApplyMode.CameraAnchorOnly => "skip:unsupported-camera-anchor-write-not-exposed",
            TitleBackgroundCharacterPlacementExperimentalApplyMode.GeneratedCurvePlusCameraAnchor => "skip:unsupported-camera-anchor-write-not-exposed",
            TitleBackgroundCharacterPlacementExperimentalApplyMode.ActorPlacementPreviewOnly => "preview-only:target-delta-dumped",
            TitleBackgroundCharacterPlacementExperimentalApplyMode.ActorPlacementOneShot => "skip:actor-write-not-implemented-without-validated-native-source",
            TitleBackgroundCharacterPlacementExperimentalApplyMode.VisibilityProbeOnly => "read-only:visibility-probe-dumped",
            _ => "skip:unknown-mode",
        };
    }

    private IReadOnlyList<TitleBackgroundPhase2CTimelineSnapshot> BuildPhase2CTimelineSamples()
    {
        var samples = new List<TitleBackgroundPhase2CTimelineSnapshot>(CameraProbeTimelineFrames.Length);
        foreach (var frame in CameraProbeTimelineFrames)
        {
            samples.Add(_probeTimeline.Phase2CTimelineSnapshots.TryGetValue(frame, out var snapshot)
                ? snapshot
                : TitleBackgroundPhase2CTimelineSnapshot.Missing(frame));
        }

        return samples;
    }

    private IReadOnlyList<TitleBackgroundPhase2EProbeSample> BuildPhase2EProbeSamples()
    {
        return _phaseRecording.Phase2ECalculateLookAtYCalls
            .Select(call => new TitleBackgroundPhase2EProbeSample(
                call.CallIndex,
                call.Frame,
                call.ReturnValue,
                call.ActiveLookAtYAfterOriginal))
            .ToArray();
    }

    private static IReadOnlyList<TitleBackgroundPhase2FCurveTimelineSample> BuildPhase2FCurveTimelineSamples(
        IReadOnlyList<TitleBackgroundPhase2CTimelineSnapshot> samples)
    {
        return samples
            .Select(sample => new TitleBackgroundPhase2FCurveTimelineSample(
                sample.Frame,
                sample.ExpandedLobbyCameraCaptured,
                sample.CameraCurveEnabled,
                sample.LowPoint?.X,
                sample.LowPoint?.Y,
                sample.MidPoint?.X,
                sample.MidPoint?.Y,
                sample.HighPoint?.X,
                sample.HighPoint?.Y))
            .ToArray();
    }

    private void RecordPhase2ECalculateLookAtYCall(TitleBackgroundPhase2ECalculateLookAtYCall call)
    {
        _phaseRecording.Phase2ECalculateLookAtYCalls.Add(call);
        while (_phaseRecording.Phase2ECalculateLookAtYCalls.Count > Phase2EMaxRecordedCalls)
        {
            _phaseRecording.Phase2ECalculateLookAtYCalls.RemoveAt(0);
        }
    }

    private void ResetPhase2ECalculateLookAtYObservation()
    {
        _phaseRecording.Phase2ECalculateLookAtYCallCount = 0;
        _phaseRecording.Phase2ECalculateLookAtYLastError = string.Empty;
        _phaseRecording.Phase2ECalculateLookAtYCalls.Clear();
        _phaseRecording.Phase2FSetCameraCurveMidPointCallCount = 0;
        _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointCallCount = 0;
        _phaseRecording.Phase2FSetCameraCurveMidPointLastError = string.Empty;
        _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointLastError = string.Empty;
        _phaseRecording.Phase2FSetCameraCurveMidPointCalls.Clear();
        _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointCalls.Clear();
        _phaseRecording.Phase2FSetCameraCurveMidPointInterestingCalls.Clear();
        _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointInterestingCalls.Clear();
        _phaseRecording.Phase2FSetCameraCurveMidPointPreviousInputValue = null;
        _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointPreviousInputValue = null;
        _phaseRecording.Phase2GGenerationOverrideSetMidAttemptCount = 0;
        _phaseRecording.Phase2GGenerationOverrideSetMidAppliedCount = 0;
        _phaseRecording.Phase2GGenerationOverrideLowHighAttemptCount = 0;
        _phaseRecording.Phase2GGenerationOverrideLowHighAppliedCount = 0;
        _phaseRecording.Phase2GGenerationOverrideLastAppliedFrame = null;
        _phaseRecording.Phase2GGenerationOverrideLastAppliedSceneGeneration = 0;
        _phaseRecording.Phase2GGenerationOverrideLastStatus = "not-run";
        _phaseRecording.Phase2GGenerationOverrideLastSkippedReason = string.Empty;
    }

    private TitleBackgroundPhase2FGeneratedCurveCall BuildPhase2FGeneratedCurveCall(
        int callIndex,
        int? frame,
        float inputValue,
        TitleBackgroundExpandedLobbyCameraSnapshot? before,
        TitleBackgroundExpandedLobbyCameraSnapshot? after,
        TitleBackgroundActiveCameraSnapshot? activeBefore,
        TitleBackgroundActiveCameraSnapshot? activeAfter,
        string status,
        string error)
    {
        return new TitleBackgroundPhase2FGeneratedCurveCall(
            callIndex,
            frame,
            inputValue,
            _charaSelectCameraAdapter.Input.CharacterPosition.Y,
            _charaSelectCameraAdapter.Curve.Low,
            _charaSelectCameraAdapter.Curve.Mid,
            _charaSelectCameraAdapter.Curve.High,
            before,
            after,
            activeBefore?.Distance,
            activeBefore?.LookAtVector.Y,
            activeAfter?.Distance,
            activeAfter?.LookAtVector.Y,
            string.Empty,
            0,
            status,
            error);
    }

    private void RecordPhase2FSetCameraCurveMidPointCall(TitleBackgroundPhase2FGeneratedCurveCall call)
    {
        RecordPhase2FGeneratedCurveCall(
            _phaseRecording.Phase2FSetCameraCurveMidPointCalls,
            _phaseRecording.Phase2FSetCameraCurveMidPointInterestingCalls,
            call,
            _phaseRecording.Phase2FSetCameraCurveMidPointPreviousInputValue);
        _phaseRecording.Phase2FSetCameraCurveMidPointPreviousInputValue = call.InputValue;
    }

    private void RecordPhase2FCalculateCameraCurveLowAndHighPointCall(TitleBackgroundPhase2FGeneratedCurveCall call)
    {
        RecordPhase2FGeneratedCurveCall(
            _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointCalls,
            _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointInterestingCalls,
            call,
            _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointPreviousInputValue);
        _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointPreviousInputValue = call.InputValue;
    }

    private static void RecordPhase2FGeneratedCurveCall(
        List<TitleBackgroundPhase2FGeneratedCurveCall> calls,
        List<TitleBackgroundPhase2FGeneratedCurveCall> interestingCalls,
        TitleBackgroundPhase2FGeneratedCurveCall call,
        float? previousInputValue)
    {
        calls.Add(call);
        while (calls.Count > Phase2FMaxRecordedGeneratedCurveCalls)
        {
            calls.RemoveAt(0);
        }

        if (!TryClassifyPhase2FGeneratedCurveInterestingCall(call, previousInputValue, out var interestingReason, out var interestingPriority))
        {
            return;
        }

        interestingCalls.Add(call with
        {
            InterestingReason = interestingReason,
            InterestingPriority = interestingPriority,
        });
        while (interestingCalls.Count > Phase2FMaxInterestingGeneratedCurveCalls)
        {
            RemoveLowestPriorityGeneratedCurveCall(interestingCalls);
        }
    }

    private static bool TryClassifyPhase2FGeneratedCurveInterestingCall(
        TitleBackgroundPhase2FGeneratedCurveCall call,
        float? previousInputValue,
        out string reason,
        out int priority)
    {
        reason = string.Empty;
        priority = 0;
        if (!call.Frame.HasValue)
        {
            return false;
        }

        var reasons = new List<string>();
        if (call.Frame.Value >= 0 && call.Frame.Value <= Phase2FGeneratedCurveInterestingMaximumFrame)
        {
            reasons.Add("early-frame");
            priority = Math.Max(priority, 10);
        }

        if (HasGeneratedCurvePointChanged(call))
        {
            reasons.Add("curve-point-changed");
            priority = Math.Max(priority, 100);
        }

        if (HasGeneratedCurveEnabledChanged(call))
        {
            reasons.Add("camera-curve-enabled-changed");
            priority = Math.Max(priority, 90);
        }

        if (previousInputValue.HasValue
            && Math.Abs(call.InputValue - previousInputValue.Value) > TitleBackgroundCameraProbeReport.CurvePointTolerance)
        {
            reasons.Add("input-value-changed");
            priority = Math.Max(priority, 50);
        }

        if (IsNearPhase2FGeneratedCurveSamplingFrame(call.Frame.Value))
        {
            reasons.Add("timeline-sampling-nearby");
            priority = Math.Max(priority, 40);
        }

        if (reasons.Count == 0)
        {
            return false;
        }

        reason = string.Join(",", reasons);
        return true;
    }

    private static void RemoveLowestPriorityGeneratedCurveCall(List<TitleBackgroundPhase2FGeneratedCurveCall> calls)
    {
        var removeIndex = 0;
        for (var i = 1; i < calls.Count; i++)
        {
            if (calls[i].InterestingPriority < calls[removeIndex].InterestingPriority)
            {
                removeIndex = i;
            }
        }

        calls.RemoveAt(removeIndex);
    }

    private static bool IsNearPhase2FGeneratedCurveSamplingFrame(int frame)
    {
        foreach (var samplingFrame in Phase2FGeneratedCurveSamplingFrames)
        {
            if (Math.Abs(frame - samplingFrame) <= Phase2FGeneratedCurveSamplingFrameRange)
            {
                return true;
            }
        }

        return false;
    }

    private TitleBackgroundPhase2FGeneratedCurveTransitionSummary BuildPhase2FGeneratedCurveTransitionSummary()
    {
        var setCameraCurveMidPointCount = _phaseRecording.Phase2FSetCameraCurveMidPointInterestingCalls.Count(HasGeneratedCurvePointChanged);
        var calculateCameraCurveLowAndHighPointCount = _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointInterestingCalls.Count(HasGeneratedCurvePointChanged);
        var transitionFrames = _phaseRecording.Phase2FSetCameraCurveMidPointInterestingCalls
            .Concat(_phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointInterestingCalls)
            .Where(HasGeneratedCurvePointChanged)
            .Select(call => call.Frame)
            .Where(frame => frame.HasValue)
            .Select(frame => frame!.Value)
            .Order()
            .ToArray();

        return new TitleBackgroundPhase2FGeneratedCurveTransitionSummary(
            setCameraCurveMidPointCount + calculateCameraCurveLowAndHighPointCount,
            transitionFrames.Length == 0 ? null : transitionFrames[0],
            transitionFrames.Length == 0 ? null : transitionFrames[^1],
            setCameraCurveMidPointCount,
            calculateCameraCurveLowAndHighPointCount);
    }

    private string BuildPhase2GGeneratedCurveOverrideEffectiveVerdict(
        IReadOnlyList<TitleBackgroundPhase2FCurveTimelineSample> samples)
    {
        return BuildPhase2GGeneratedCurveOverrideEffectiveVerdict(samples, null);
    }

    private string BuildPhase2GGeneratedCurveOverrideEffectiveVerdict(
        IReadOnlyList<TitleBackgroundPhase2FCurveTimelineSample> samples,
        TitleBackgroundCharaSelectCameraCurve? expectedCurve)
    {
        if (_phaseRecording.Phase2GGenerationOverrideSetMidAppliedCount == 0
            && _phaseRecording.Phase2GGenerationOverrideLowHighAppliedCount == 0)
        {
            return "inconclusive";
        }

        var curve = expectedCurve ?? _charaSelectCameraAdapter.RuntimeState.CurveAtRecord ?? _charaSelectCameraAdapter.Curve;
        var latest = samples
            .Where(sample => sample.Captured)
            .OrderByDescending(sample => sample.Frame)
            .FirstOrDefault();
        if (!latest.Captured
            || !latest.LowValue.HasValue
            || !latest.MidValue.HasValue
            || !latest.HighValue.HasValue)
        {
            return "inconclusive";
        }

        return IsNear(latest.LowValue.Value, curve.Low)
            && IsNear(latest.MidValue.Value, curve.Mid)
            && IsNear(latest.HighValue.Value, curve.High)
            ? "observed"
            : "not-observed";
    }

    private string BuildPhase2GFinalLookAtYMatchesGeneratedCurveVerdict(TitleBackgroundPhase2CTimelineSnapshot latestSample)
    {
        if (_phaseRecording.Phase2GGenerationOverrideSetMidAppliedCount == 0
            && _phaseRecording.Phase2GGenerationOverrideLowHighAppliedCount == 0)
        {
            return "inconclusive";
        }

        var target = GetLatestCalculateLobbyCameraLookAtYReturnValue();
        var actual = latestSample.SceneCameraLookAtVector?.Y;
        if (!target.HasValue || !actual.HasValue)
        {
            return "inconclusive";
        }

        return Math.Abs(actual.Value - target.Value) <= TitleBackgroundCameraProbeReport.StabilizationVectorTolerance
            ? "observed"
            : "not-observed";
    }

    private string BuildPhase2GFinalCameraStateMatchesPresetVerdict(TitleBackgroundPhase2CTimelineSnapshot latestSample)
    {
        if (_cameraRestoreCurve.RuntimeRestoreSuccessCount == 0)
        {
            return "inconclusive";
        }

        if (!_cameraRestoreCurve.RuntimeRestoreLastRestoredYaw.HasValue
            || !_cameraRestoreCurve.RuntimeRestoreLastRestoredPitch.HasValue
            || !_cameraRestoreCurve.RuntimeRestoreLastRestoredDistance.HasValue
            || !latestSample.LobbyDirH.HasValue
            || !latestSample.LobbyDirV.HasValue
            || !latestSample.LobbyDistance.HasValue
            || !latestSample.LobbyInterpDistance.HasValue)
        {
            return "inconclusive";
        }

        return IsNear(latestSample.LobbyDirH.Value, _cameraRestoreCurve.RuntimeRestoreLastRestoredYaw.Value)
            && IsNear(latestSample.LobbyDirV.Value, _cameraRestoreCurve.RuntimeRestoreLastRestoredPitch.Value)
            && IsNear(latestSample.LobbyDistance.Value, _cameraRestoreCurve.RuntimeRestoreLastRestoredDistance.Value)
            && IsNear(latestSample.LobbyInterpDistance.Value, _cameraRestoreCurve.RuntimeRestoreLastRestoredDistance.Value)
            ? "observed"
            : "not-observed";
    }

    private float? GetLatestCalculateLobbyCameraLookAtYReturnValue()
    {
        return _phaseRecording.Phase2ECalculateLookAtYCalls
            .Where(call => call.ReturnValue.HasValue)
            .OrderByDescending(call => call.Frame ?? -1)
            .ThenByDescending(call => call.CallIndex)
            .Select(call => call.ReturnValue)
            .FirstOrDefault();
    }

    private bool IsPhase2GGenerationOverrideConfigured()
    {
        return _configuration.TitleBackgroundOverrideEnabled
            && _configuration.TitleBackgroundCameraOverrideEnabled
            && _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly;
    }

    private static bool HasGeneratedCurveEnabledChanged(TitleBackgroundPhase2FGeneratedCurveCall call)
    {
        return call.Before.HasValue
            && call.After.HasValue
            && call.Before.Value.CameraCurveEnabled != call.After.Value.CameraCurveEnabled;
    }

    private static bool HasGeneratedCurvePointChanged(TitleBackgroundPhase2FGeneratedCurveCall call)
    {
        return call.Before.HasValue
            && call.After.HasValue
            && (!AreCurvePointsEqual(call.Before.Value.LowPoint, call.After.Value.LowPoint)
                || !AreCurvePointsEqual(call.Before.Value.MidPoint, call.After.Value.MidPoint)
                || !AreCurvePointsEqual(call.Before.Value.HighPoint, call.After.Value.HighPoint));
    }

    private static bool AreCurvePointsEqual(TitleBackgroundCurvePointSnapshot left, TitleBackgroundCurvePointSnapshot right)
    {
        return IsNear(left.X, right.X) && IsNear(left.Y, right.Y);
    }

    private static bool IsNear(float left, float right)
    {
        return Math.Abs(left - right) <= TitleBackgroundCameraProbeReport.CurvePointTolerance;
    }

    private static TitleBackgroundCurvePointSnapshot? ReadCurvePoint(CurvePoint* point)
    {
        if (point == null)
        {
            return null;
        }

        var snapshot = new TitleBackgroundCurvePointSnapshot(point->X, point->Y);
        return float.IsFinite(snapshot.X) && float.IsFinite(snapshot.Y)
            ? snapshot
            : null;
    }

    private bool TryCaptureLobbyCameraSnapshot(out TitleBackgroundLobbyCameraSnapshot snapshot, out string errorMessage)
    {
        snapshot = default;
        errorMessage = string.Empty;
        try
        {
            var cameraManager = CameraManager.Instance();
            if (cameraManager == null)
            {
                errorMessage = "CameraManager.Instance() unavailable";
                return false;
            }

            var lobbyCamera = cameraManager->LobbyCamera;
            if (lobbyCamera == null)
            {
                errorMessage = "LobbyCamera unavailable";
                return false;
            }

            var lastLookAtVector = ToNumerics(lobbyCamera->LastLookAtVector);
            if (!TitleBackgroundCameraMath.IsFiniteVector(lastLookAtVector))
            {
                errorMessage = "LobbyCamera.LastLookAtVector contains non-finite values";
                return false;
            }

            snapshot = new TitleBackgroundLobbyCameraSnapshot(
                lastLookAtVector,
                float.IsFinite(lobbyCamera->DirH) ? lobbyCamera->DirH : null,
                float.IsFinite(lobbyCamera->DirV) ? lobbyCamera->DirV : null,
                float.IsFinite(lobbyCamera->Distance) ? lobbyCamera->Distance : null,
                float.IsFinite(lobbyCamera->InterpDistance) ? lobbyCamera->InterpDistance : null);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _log.Warning(ex, "TitleBackground lobby camera capture failed.");
            return false;
        }
    }

    private bool TryCaptureExpandedLobbyCameraSnapshot(out TitleBackgroundExpandedLobbyCameraSnapshot snapshot, out string errorMessage)
    {
        snapshot = default;
        errorMessage = string.Empty;
        try
        {
            var cameraManager = CameraManager.Instance();
            if (cameraManager == null)
            {
                errorMessage = "CameraManager.Instance() unavailable";
                return false;
            }

            var lobbyCamera = cameraManager->LobbyCamera;
            if (lobbyCamera == null)
            {
                errorMessage = "LobbyCamera unavailable";
                return false;
            }

            var baseAddress = (byte*)lobbyCamera;
            return TryCaptureExpandedLobbyCameraSnapshot((nint)baseAddress, out snapshot, out errorMessage);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _log.Warning(ex, "TitleBackground expanded lobby camera capture failed.");
            return false;
        }
    }

    private static bool TryCaptureExpandedLobbyCameraSnapshot(
        nint lobbyCameraAddress,
        out TitleBackgroundExpandedLobbyCameraSnapshot snapshot,
        out string errorMessage)
    {
        snapshot = default;
        errorMessage = string.Empty;
        if (lobbyCameraAddress == nint.Zero)
        {
            errorMessage = "LobbyCamera address unavailable";
            return false;
        }

        var baseAddress = (byte*)lobbyCameraAddress;
        var cameraCurveEnabled = *(byte*)(baseAddress + LobbyCameraExpandedCameraCurveEnabledOffset) != 0;
        var lowPoint = ReadCurvePoint((CurvePoint*)(baseAddress + LobbyCameraExpandedLowPointOffset));
        var midPoint = ReadCurvePoint((CurvePoint*)(baseAddress + LobbyCameraExpandedMidPointOffset));
        var highPoint = ReadCurvePoint((CurvePoint*)(baseAddress + LobbyCameraExpandedHighPointOffset));
        if (!lowPoint.HasValue || !midPoint.HasValue || !highPoint.HasValue)
        {
            errorMessage = "LobbyCameraExpanded curve point contains non-finite values";
            return false;
        }

        snapshot = new TitleBackgroundExpandedLobbyCameraSnapshot(
            cameraCurveEnabled,
            lowPoint.Value,
            midPoint.Value,
            highPoint.Value);
        return true;
    }

    private int? GetCurrentPhase2CFrame()
    {
        return _probeTimeline.Phase2CTimelineFrameCounter >= 0
            ? _probeTimeline.Phase2CTimelineFrameCounter
            : null;
    }

    // R 実験用の「最後の pre-login CharaSelect カメラ」を read-only で保持する（カメラには書き込まない）。
    // pre-login + session active かつ active generation 正値・adapter generation 一致のフレームのみ採用し、
    // 古い/遷移中の値や別ロードの値を混ぜない。診断は post-login で呼ばれるが、この保持値は最後の整合
    // pre-login フレームを指す。「安定後」は名乗らず generation と captured frame を併記して判断材料を出す。
    private void CapturePreLoginCameraOnFrameworkUpdate()
    {
        if (_clientState.IsLoggedIn || !_charaSelectTitleBackgroundSessionActive)
        {
            return;
        }

        if (_activeCharaSelectSceneGeneration <= 0
            || _charaSelectCameraAdapter.RuntimeState.SceneGeneration != _activeCharaSelectSceneGeneration)
        {
            return;
        }

        if (!TryCaptureActiveCameraSnapshot(out var snapshot, out _))
        {
            return;
        }

        _cameraObservation.LastPreLoginSceneCameraPosition = snapshot.SceneCameraPosition;
        _cameraObservation.LastPreLoginSceneCameraLookAt = snapshot.LookAtVector;
        _cameraObservation.LastPreLoginSceneCameraDistance = snapshot.Distance;
        _cameraObservation.LastPreLoginSceneCameraFovY = snapshot.FovY;
        _cameraObservation.LastPreLoginSceneCameraGeneration = _activeCharaSelectSceneGeneration;
        _cameraObservation.LastPreLoginSceneCameraFrame = GetCurrentPhase2CFrame();
    }

    private void CapturePostFixOnCameraState()
    {
        ClearPostFixOnCameraObservation();
        if (!TryCaptureActiveCameraSnapshot(out var snapshot, out var errorMessage))
        {
            MarkPostFixOnCameraCaptureFailed(errorMessage);
            return;
        }

        _cameraObservation.LastPostFixOnSceneCameraPosition = snapshot.SceneCameraPosition;
        _cameraObservation.LastPostFixOnLookAtVector = snapshot.LookAtVector;
        _cameraObservation.LastPostFixOnDistance = snapshot.Distance;
        _cameraObservation.LastPostFixOnFovY = snapshot.FovY;
        _cameraObservation.LastPostFixOnCameraCaptureStatus = "success";
        _cameraObservation.LastPostFixOnCameraCaptureError = string.Empty;
    }

    // B (independent compositing): place the character at the point the engine's
    // camera is already looking at, every frame. The camera is NEVER touched, so there
    // is no fight with the engine's camera solve -> no jitter. Only this code writes the
    // character draw position, so it is stable. Tightly gated + guarded. Actor write is
    // used here under explicit user direction.
    private void MaintainCharaSelectCharacterPlacement()
    {
        try
        {
            if (!IsCharaSelectCharacterCompositionActive())
            {
                return;
            }

            if (!TryCaptureActiveCameraSnapshot(out var camera, out var error))
            {
                _characterPlacement.CharaSelectCharacterPlacementLastError = string.IsNullOrWhiteSpace(error) ? "camera-unavailable" : error;
                return;
            }

            var lookAt = camera.LookAtVector;
            var activeCandidate = ResolveCurrentOverrideCandidate();
            var supportedAnchor = BuildSupportedFrameAnchor();
            // 問題4: experimental world は別 gate で territory/candidate を厳格照合してから許可。
            var worldPlacement = ResolveExperimentalWorldPlacement(activeCandidate);
            // World も既存アンカーも無い場合は camera focus フォールバックを使うので lookAt の有限性が必要。
            if (!worldPlacement.Eligible
                && !supportedAnchor.HasUsableAnchor
                && !TitleBackgroundCameraMath.IsFiniteVector(lookAt))
            {
                _characterPlacement.CharaSelectCharacterPlacementLastError = "lookat-non-finite";
                return;
            }

            // 優先順位: 1) experimental World → 2) 既存 placement-supported アンカー → 3) camera-focus。
            // いずれの場合もカメラ・背景には一切書き込まない（キャラ DrawObject 座標のみ）。
            var decision = TitleBackgroundCharaSelectAnchorLogic.ResolvePlacementWithExperimentalWorld(
                worldPlacement.Eligible,
                worldPlacement.Position,
                supportedAnchor,
                _configuration.TitleBackgroundCharaSelectAnchorFrame,
                activeCandidate.Id,
                lookAt,
                CharaSelectCharacterFocusBodyDrop);
            var target = decision.Target;
            if (TitleBackgroundCharacterSourceProbe.TrySetCurrentCharacterDrawPosition(target))
            {
                _characterPlacement.CharaSelectCharacterPlacementCount++;
                _characterPlacement.LastCharaSelectCharacterPlacementTarget = target;
                _characterPlacement.LastCharaSelectCharacterPlacementSource = decision.Source;
                // 実際に選択したアンカーの effective frame を記録する（config からではない）。
                // World/probe 適用時に config frame と取り違えないことが provenance 判定の前提。
                _characterPlacement.LastCharaSelectCharacterPlacementAnchorFrame = decision.EffectiveFrame;
                _characterPlacement.CharaSelectCharacterPlacementLastError = "none";
            }
            else
            {
                _characterPlacement.CharaSelectCharacterPlacementLastError = "draw-position-write-failed";
            }
        }
        catch (Exception ex)
        {
            _characterPlacement.CharaSelectCharacterPlacementLastError = ex.GetType().Name;
            MarkRuntimeError(ex, nameof(MaintainCharaSelectCharacterPlacement));
        }
    }

    private const float CharaSelectCharacterFocusBodyDrop = 0.9f;

    // ログイン画面（Title Background）が実時刻・天候をそのまま反映して暗くなる問題への対策。
    // 背景セッション中（pre-login + CharaSelectセッション中 + hook Ready）に限り、EnvManager の
    // 時刻を毎フレームエオルゼア正午へ上書きする。IsLoggedIn は最重要ゲート：ログイン中は絶対に
    // 書き込まない（ログインした瞬間にゲートが外れ、post-login へは書込がリークしない）。
    // 天候・露出・環境光には一切触れない（このメソッドの唯一の責務は時刻上書き）。
    private void MaintainCharaSelectEnvironmentNoon()
    {
        if (!_configuration.TitleBackgroundEnvironmentNoonEnabled
            || _clientState.IsLoggedIn
            || !_charaSelectTitleBackgroundSessionActive
            || _hookLifecycle.State != TitleBackgroundServiceState.Ready)
        {
            return;
        }

        try
        {
            if (TitleBackgroundEnvironmentNoonWriter.TryApplyNoon())
            {
                _environmentNoon.AppliedFrameCount++;
                _environmentNoon.LastStatus = "applied";
            }
            else
            {
                _environmentNoon.LastStatus = "env-manager-unavailable";
            }
        }
        catch (Exception ex)
        {
            _environmentNoon.LastStatus = ex.GetType().Name;
            _log.Warning(ex, "TitleBackground environment noon override failed.");
        }
    }

    // placement-supported（LobbyNative / CharaSelectFallback）frame のときだけ有効なアンカー。
    // World 実験座標はここでは決して有効化しない（World は別 gate の experimental 経路だけが扱う）。
    private TitleBackgroundCharaSelectAnchor BuildSupportedFrameAnchor()
    {
        return new TitleBackgroundCharaSelectAnchor(
            _configuration.TitleBackgroundCharaSelectAnchorEnabled
                && TitleBackgroundCharaSelectAnchorFrame.IsPlacementSupported(
                    _configuration.TitleBackgroundCharaSelectAnchorFrame),
            _configuration.TitleBackgroundCharaSelectAnchorCandidateId,
            new Vector3(
                _configuration.TitleBackgroundCharaSelectAnchorX,
                _configuration.TitleBackgroundCharaSelectAnchorY,
                _configuration.TitleBackgroundCharaSelectAnchorZ),
            _configuration.TitleBackgroundCharaSelectAnchorRotation);
    }

    // FixOn 焦点 override 専用アンカー。FixOn はカメラ焦点へ書き込むため、未検証の World 座標を
    // 絶対に流さない。placement-supported frame のみ（= BuildSupportedFrameAnchor と同一ポリシー）。
    private TitleBackgroundCharaSelectAnchor BuildFixOnFocusAnchor()
    {
        return BuildSupportedFrameAnchor();
    }

    // experimental world placement の最終決定。選択元（probe/config/none）と、その選択元の
    // 実効値一式（候補・保存 territory・enabled・gate）を返す。診断はこの戻り値だけを使うことで、
    // 「適用元は config なのに表示は probe」のような混在を防ぐ。
    // 優先順位: 1) セッション限定 probe（Phase 0A・config 非書き込み）、2) 永続 config world アンカー。
    // 永続 config 経路は PersistentApplyEnabled が false の間は無効（Phase 0B 成立まで適用しない）。
    private readonly record struct WorldExperimentalResolution(
        bool Eligible,
        Vector3 Position,
        TitleBackgroundExperimentalWorldPlacementGate Gate,
        string Source,
        string AnchorCandidateId,
        uint SavedTerritoryTypeId,
        // ExperimentalEnabled は gate に渡したのと同じ「実効値」。config 経路では
        // PersistentApplyEnabled を加味した値（gate=disabled と整合する）。
        bool ExperimentalEnabled,
        // ConfiguredEnabled はユーザー設定の「生値」。リリースゲートで実効が落ちても保持する。
        bool ConfiguredEnabled);

    private const string WorldExperimentalSourceProbe = "probe";
    private const string WorldExperimentalSourceConfig = "config";

    private WorldExperimentalResolution ResolveExperimentalWorldPlacement(
        TitleBackgroundCharacterSelectOverrideCandidate activeCandidate)
    {
        var probeGate = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
            _worldProbeState.Enabled,
            _worldProbeState.HasValue,
            _worldProbeState.Position,
            TitleBackgroundCharaSelectAnchorFrame.World,
            _worldProbeState.CandidateId,
            _worldProbeState.TerritoryTypeId,
            activeCandidate.Id,
            activeCandidate.TerritoryId);
        if (TitleBackgroundExperimentalWorldPlacementLogic.IsEligible(probeGate))
        {
            return new WorldExperimentalResolution(
                true,
                _worldProbeState.Position,
                probeGate,
                WorldExperimentalSourceProbe,
                _worldProbeState.CandidateId,
                _worldProbeState.TerritoryTypeId,
                _worldProbeState.Enabled,
                _worldProbeState.Enabled);
        }

        var configPosition = new Vector3(
            _configuration.TitleBackgroundCharaSelectAnchorX,
            _configuration.TitleBackgroundCharaSelectAnchorY,
            _configuration.TitleBackgroundCharaSelectAnchorZ);
        // PersistentApplyEnabled が false の間は永続経路を experimentalEnabled=false 扱いにし、
        // 適用も「applicable=True」表示も出さない（gate は disabled になる）。
        var configExperimentalEffective =
            _configuration.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled
            && TitleBackgroundExperimentalWorldPlacementLogic.PersistentApplyEnabled;
        var configGate = TitleBackgroundExperimentalWorldPlacementLogic.Evaluate(
            configExperimentalEffective,
            _configuration.TitleBackgroundCharaSelectAnchorEnabled,
            configPosition,
            _configuration.TitleBackgroundCharaSelectAnchorFrame,
            _configuration.TitleBackgroundCharaSelectAnchorCandidateId,
            _configuration.TitleBackgroundCharaSelectAnchorTerritoryTypeId,
            activeCandidate.Id,
            activeCandidate.TerritoryId);
        if (TitleBackgroundExperimentalWorldPlacementLogic.IsEligible(configGate))
        {
            return new WorldExperimentalResolution(
                true,
                configPosition,
                configGate,
                WorldExperimentalSourceConfig,
                _configuration.TitleBackgroundCharaSelectAnchorCandidateId,
                _configuration.TitleBackgroundCharaSelectAnchorTerritoryTypeId,
                configExperimentalEffective,
                _configuration.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled);
        }

        // 不可: 実際に選ばれ得る源で報告する（probe 有効なら probe、なければ config）。
        // gate と候補/territory を同一源から取り、診断の矛盾を防ぐ。
        if (_worldProbeState.Enabled)
        {
            return new WorldExperimentalResolution(
                false,
                Vector3.Zero,
                probeGate,
                WorldExperimentalSourceProbe,
                _worldProbeState.CandidateId,
                _worldProbeState.TerritoryTypeId,
                _worldProbeState.Enabled,
                _worldProbeState.Enabled);
        }

        return new WorldExperimentalResolution(
            false,
            Vector3.Zero,
            configGate,
            WorldExperimentalSourceConfig,
            _configuration.TitleBackgroundCharaSelectAnchorCandidateId,
            _configuration.TitleBackgroundCharaSelectAnchorTerritoryTypeId,
            configExperimentalEffective,
            _configuration.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled);
    }

    internal bool IsConfiguredCharaSelectAnchorPlacementSupported()
    {
        return !_configuration.TitleBackgroundCharaSelectAnchorEnabled
            || TitleBackgroundCharaSelectAnchorFrame.IsPlacementSupported(
                _configuration.TitleBackgroundCharaSelectAnchorFrame);
    }

    private TitleBackgroundCharaSelectView BuildCharaSelectView()
    {
        return new TitleBackgroundCharaSelectView(
            _configuration.TitleBackgroundCharaSelectViewEnabled,
            _configuration.TitleBackgroundCharaSelectViewCandidateId,
            new Vector3(
                _configuration.TitleBackgroundCharaSelectViewCameraX,
                _configuration.TitleBackgroundCharaSelectViewCameraY,
                _configuration.TitleBackgroundCharaSelectViewCameraZ),
            new Vector3(
                _configuration.TitleBackgroundCharaSelectViewFocusX,
                _configuration.TitleBackgroundCharaSelectViewFocusY,
                _configuration.TitleBackgroundCharaSelectViewFocusZ),
            _configuration.TitleBackgroundCharaSelectViewFovY);
    }

    private void StoreCharaSelectView(TitleBackgroundCharaSelectView view)
    {
        _configuration.TitleBackgroundCharaSelectViewEnabled = view.Enabled;
        _configuration.TitleBackgroundCharaSelectViewCandidateId = view.CandidateId;
        _configuration.TitleBackgroundCharaSelectViewCameraX = view.Camera.X;
        _configuration.TitleBackgroundCharaSelectViewCameraY = view.Camera.Y;
        _configuration.TitleBackgroundCharaSelectViewCameraZ = view.Camera.Z;
        _configuration.TitleBackgroundCharaSelectViewFocusX = view.Focus.X;
        _configuration.TitleBackgroundCharaSelectViewFocusY = view.Focus.Y;
        _configuration.TitleBackgroundCharaSelectViewFocusZ = view.Focus.Z;
        _configuration.TitleBackgroundCharaSelectViewFovY = view.FovY;
        _configuration.Save();
    }

    internal bool IsCharaSelectViewCaptureAvailable()
    {
        return !_clientState.IsLoggedIn
            && TryReadCurrentLobbyMap(out var lobbyMap)
            && lobbyMap == GameLobbyType.CharaSelect;
    }

    // ゲーム内 capture:「今の見え方を保存」。CharaSelect 中の現在 SceneCamera（位置/注視点/FovY）を
    // scene-local 絶対値で確定する。pre-login + CharaSelect ロビー限定。カメラには書き込まない（read-only）。
    public bool TryCaptureCharaSelectViewFromCurrentCamera(out string status)
    {
        if (_clientState.IsLoggedIn)
        {
            status = "skipped-post-login";
            return false;
        }

        if (!TryReadCurrentLobbyMap(out var lobbyMap) || lobbyMap != GameLobbyType.CharaSelect)
        {
            status = "skipped-not-chara-select";
            return false;
        }

        if (!TryCaptureActiveCameraSnapshot(out var snapshot, out var error))
        {
            status = string.IsNullOrWhiteSpace(error) ? "skipped-camera-unavailable" : error;
            return false;
        }

        var fovY = snapshot.FovY ?? _configuration.TitleBackgroundCharaSelectViewFovY;
        var view = new TitleBackgroundCharaSelectView(
            true,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(
                ResolveCurrentOverrideCandidate().Id),
            snapshot.SceneCameraPosition,
            snapshot.LookAtVector,
            fovY);
        // 候補 ID が未設定の場合は MatchesCandidateStrict が空 ID を拒否するため、
        // 保存しても FixOn で適用されない。保存前に弾いて silent failure を防ぐ。
        if (string.IsNullOrEmpty(view.CandidateId))
        {
            status = "skipped-empty-candidate";
            return false;
        }

        if (!view.HasUsableView)
        {
            status = "skipped-non-finite";
            return false;
        }

        // passive 観測 ON のままだと ShouldConsiderFocusOverride が抑止し view が適用されない。
        // 「保存＝この構図を適用したい」意図なので、保存時に passive 観測を解除して整合させる。
        _configuration.TitleBackgroundFixOnPassiveObservationEnabled = false;
        StoreCharaSelectView(view);
        // view を有効化したので FixOn フック装着状態を設定に合わせる（毎フレーム書きはしない）。
        ReloadNativeIntegration();
        status = "captured";
        return true;
    }

    public void ClearCharaSelectView()
    {
        StoreCharaSelectView(TitleBackgroundCharaSelectView.None);
        _cameraObservation.FixOnViewOverrideAppliedCount = 0;
        _cameraObservation.LastFixOnViewOverrideSource = "not-run";
        _cameraObservation.LastViewOverrideAppliedGeneration = 0;
        ReloadNativeIntegration();
    }

    private void StoreCharaSelectAnchor(TitleBackgroundCharaSelectAnchor anchor)
    {
        _configuration.TitleBackgroundCharaSelectAnchorEnabled = anchor.Enabled;
        _configuration.TitleBackgroundCharaSelectAnchorCandidateId = anchor.CandidateId;
        _configuration.TitleBackgroundCharaSelectAnchorX = anchor.Position.X;
        _configuration.TitleBackgroundCharaSelectAnchorY = anchor.Position.Y;
        _configuration.TitleBackgroundCharaSelectAnchorZ = anchor.Position.Z;
        _configuration.TitleBackgroundCharaSelectAnchorRotation = anchor.Rotation;
        _configuration.Save();
    }

    // ゲーム内 capture: CharaSelect 中の現在キャラの draw 座標をアンカーとして確定する。
    // pre-login + CharaSelect ロビーに限定し、いずれの場合もカメラには書き込まない。
    public bool TryCaptureCharaSelectAnchorFromCurrentCharacter(out string status)
    {
        if (_clientState.IsLoggedIn)
        {
            status = "skipped-post-login";
            return false;
        }

        if (!TryReadCurrentLobbyMap(out var lobbyMap) || lobbyMap != GameLobbyType.CharaSelect)
        {
            status = "skipped-not-chara-select";
            return false;
        }

        if (!TitleBackgroundCharacterSourceProbe.TryReadCurrentCharacterAim(out var position, out var rotation))
        {
            status = "skipped-character-unavailable";
            return false;
        }

        var anchor = TitleBackgroundCharaSelectAnchorLogic.CaptureFromDrawPosition(
            ResolveCurrentOverrideCandidate().Id,
            position,
            rotation);
        if (!anchor.HasUsableAnchor)
        {
            status = "skipped-non-finite";
            return false;
        }

        // placement が毎フレーム DrawObject を fallback/anchor へ強制配置しているため、ここで読む値は
        // native の自然立ち位置ではなく合成 fallback（camera focus - bodyDrop）になり得る。別種として記録する。
        _configuration.TitleBackgroundCharaSelectAnchorFrame = TitleBackgroundCharaSelectAnchorFrame.CharaSelectFallback;
        // World 専用フィールドは CharaSelectFallback では無効化する（territory provenance は world のみ）。
        _configuration.TitleBackgroundCharaSelectAnchorTerritoryTypeId = 0;
        _configuration.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = false;
        StoreCharaSelectAnchor(anchor);
        status = "captured";
        return true;
    }

    // 微調整。設定値のみを書き換えるので、いつ呼んでもカメラ・キャラには触れない
    // （次の placement フレームで反映される）。capture 前は何もしない。
    public void NudgeCharaSelectAnchor(TitleBackgroundCharaSelectAnchorAxis axis, float delta)
    {
        var anchor = BuildSupportedFrameAnchor();
        if (!anchor.Enabled)
        {
            return;
        }

        StoreCharaSelectAnchor(TitleBackgroundCharaSelectAnchorLogic.ApplyNudge(anchor, axis, delta));
    }

    public void ClearCharaSelectAnchor()
    {
        _configuration.TitleBackgroundCharaSelectAnchorFrame = string.Empty;
        _configuration.TitleBackgroundCharaSelectAnchorTerritoryTypeId = 0;
        _configuration.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = false;
        StoreCharaSelectAnchor(TitleBackgroundCharaSelectAnchor.None);
    }

    // ログイン中の現在地（プレイヤー座標）を候補の立ち位置アンカーとして保存する。
    // CharaSelect の native read ではなく managed API（IClientState.LocalPlayer）を使う別経路。
    // territory が候補と一致する時のみ許可（座標系一致は実機検証が前提）。
    public bool TryCaptureLoggedInPositionAsAnchor(out string status)
    {
        if (!_clientState.IsLoggedIn)
        {
            status = "skipped-not-logged-in";
            return false;
        }

        var player = _objectTable.LocalPlayer;
        if (player == null)
        {
            status = "skipped-no-local-player";
            return false;
        }

        var candidate = ResolveCurrentOverrideCandidate();
        if (candidate.TerritoryId != 0 && _clientState.TerritoryType != candidate.TerritoryId)
        {
            status = $"skipped-territory-mismatch:{_clientState.TerritoryType}!={candidate.TerritoryId}";
            return false;
        }

        var anchor = TitleBackgroundCharaSelectAnchorLogic.CaptureFromDrawPosition(
            candidate.Id,
            player.Position,
            player.Rotation);
        if (!anchor.HasUsableAnchor)
        {
            status = "skipped-non-finite";
            return false;
        }

        _configuration.TitleBackgroundCharaSelectAnchorFrame = TitleBackgroundCharaSelectAnchorFrame.World;
        // 問題4: territory は candidate.TerritoryId ではなく「実際に取得した場所」を保存する（provenance）。
        _configuration.TitleBackgroundCharaSelectAnchorTerritoryTypeId = _clientState.TerritoryType;
        StoreCharaSelectAnchor(anchor);
        status = "captured-logged-in";
        return true;
    }

    // Simple UI 用ラッパー: ログイン中の現在地を world アンカーとして保存し、保存=適用の意図に合わせて
    // experimental 適用フラグを ON にする（候補/territory が一致するときのみ実際に適用される）。
    // 低レベル capture 名を UI 本体へ露出させないための薄いラッパー。
    public bool SaveStandingPositionExperimentalAnchor(out string status)
    {
        if (!TryCaptureLoggedInPositionAsAnchor(out status))
        {
            return false;
        }

        _configuration.TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = true;
        _configuration.Save();
        return true;
    }

    // Phase 0A: Developer 限定の非永続 probe。Configuration / StoreCharaSelectAnchor / Save を呼ばず、
    // セッション限定の in-memory フィールドにのみ書く（プラグイン再起動で消える）。
    public bool CaptureWorldProbeAnchorInMemory(out string status)
    {
        if (!_clientState.IsLoggedIn)
        {
            status = "skipped-not-logged-in";
            return false;
        }

        var player = _objectTable.LocalPlayer;
        if (player == null)
        {
            status = "skipped-no-local-player";
            return false;
        }

        var candidate = ResolveCurrentOverrideCandidate();
        if (candidate.TerritoryId != 0 && _clientState.TerritoryType != candidate.TerritoryId)
        {
            status = $"skipped-territory-mismatch:{_clientState.TerritoryType}!={candidate.TerritoryId}";
            return false;
        }

        var position = player.Position;
        if (!TitleBackgroundCameraMath.IsFiniteVector(position))
        {
            status = "skipped-non-finite";
            return false;
        }

        _worldProbeState.CandidateId =
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(candidate.Id);
        _worldProbeState.Position = position;
        _worldProbeState.TerritoryTypeId = _clientState.TerritoryType;
        _worldProbeState.HasValue = true;
        _worldProbeState.Enabled = true;
        status = "captured-probe";
        return true;
    }

    public void SetWorldProbeAnchorEnabled(bool enabled)
    {
        _worldProbeState.Enabled = enabled;
    }

    public void ClearWorldProbeAnchor()
    {
        _worldProbeState.Enabled = false;
        _worldProbeState.HasValue = false;
        _worldProbeState.CandidateId = string.Empty;
        _worldProbeState.Position = Vector3.Zero;
        _worldProbeState.TerritoryTypeId = 0;
    }

    internal bool IsWorldProbeAnchorEnabled => _worldProbeState.Enabled;
    internal bool HasWorldProbeAnchor => _worldProbeState.HasValue;
    internal int WorldCoordinateSampleCount => _worldProbeState.Samples.Count;

    // Phase 0C: 自動確認完了時点の run-scoped 値から「有効な probe run」だけを 1 サンプル採取する。
    // config は一切書かない（セッション限定）。採用条件は IsAcceptableRun（純粋ゲート）に委譲。
    public bool TryAddWorldCoordinateSampleFromRun(string runId, string completedAt)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(runId)
                || string.IsNullOrWhiteSpace(completedAt)
                || _worldProbeState.Samples.Any(
                    sample => string.Equals(sample.RunId, runId, StringComparison.Ordinal)))
            {
                return false;
            }

            var active = ResolveCurrentOverrideCandidate();
            var worldRes = ResolveExperimentalWorldPlacement(active);
            var runActive = IsRunScopedQuickCheckActive();
            var runApplied = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedPlacementCount(
                runActive,
                _characterPlacement.CharaSelectCharacterPlacementCount,
                _quickCheckState.CharacterPlacementCountStart);
            var runSource = runApplied > 0 ? _characterPlacement.LastCharaSelectCharacterPlacementSource : "none";
            var runTarget = _characterPlacement.LastCharaSelectCharacterPlacementTarget ?? Vector3.Zero;
            var runAnchorFrame = runApplied > 0 ? _characterPlacement.LastCharaSelectCharacterPlacementAnchorFrame : "none";
            var generationMatched = _cameraObservation.LastPreLoginSceneCameraGeneration > 0
                && _cameraObservation.LastPreLoginSceneCameraGeneration == _cameraObservation.FixOnExperimentSceneGeneration;

            // 観測値が欠けている run は採用しない（zero 埋めで偽サンプルを作らない）。
            if (!_cameraObservation.LastObservedFixOnFocus.HasValue
                || !_cameraObservation.LastPreLoginSceneCameraPosition.HasValue
                || !_cameraObservation.LastPreLoginSceneCameraLookAt.HasValue)
            {
                return false;
            }

            // 検証済み world 座標は probe の生値ではなく resolver が gate を通した値を使う。
            var worldPosition = worldRes.Position;
            var fixOnObservedFocus = _cameraObservation.LastObservedFixOnFocus.Value;
            var preLoginCamera = _cameraObservation.LastPreLoginSceneCameraPosition.Value;
            var preLoginLookAt = _cameraObservation.LastPreLoginSceneCameraLookAt.Value;

            if (!TitleBackgroundWorldCoordinateCorrespondenceLogic.IsAcceptableRun(
                    worldRes.Eligible,
                    worldRes.Source,
                    runSource,
                    runApplied,
                    generationMatched,
                    worldRes.AnchorCandidateId,
                    active.Id,
                    worldRes.SavedTerritoryTypeId,
                    active.TerritoryId,
                    runAnchorFrame,
                    worldPosition,
                    runTarget,
                    fixOnObservedFocus,
                    preLoginCamera,
                    preLoginLookAt))
            {
                return false;
            }

            var sample = new TitleBackgroundWorldCoordinateSample(
                _worldProbeState.SampleIndex++,
                runId,
                completedAt,
                active.Id,
                worldRes.SavedTerritoryTypeId,
                active.TerritoryId,
                worldPosition,
                runTarget,
                runSource,
                runAnchorFrame,
                runApplied,
                fixOnObservedFocus,
                preLoginCamera,
                preLoginLookAt,
                _cameraObservation.FixOnExperimentSceneGeneration,
                generationMatched,
                _cameraObservation.FixOnExperimentCaptureContext);
            _worldProbeState.Samples.Add(sample);
            PersistWorldCoordinateCorrespondenceReport();
            return true;
        }
        catch (Exception ex)
        {
            MarkRuntimeError(ex, nameof(TryAddWorldCoordinateSampleFromRun));
            return false;
        }
    }

    public string BuildWorldCoordinateCorrespondenceReportText()
    {
        return string.Join(
            Environment.NewLine,
            TitleBackgroundWorldCoordinateCorrespondenceLogic.BuildReport(_worldProbeState.Samples));
    }

    public void ClearWorldCoordinateSamples()
    {
        _worldProbeState.Samples.Clear();
        _worldProbeState.SampleIndex = 0;
        try
        {
            var path = Path.Combine(
                _configDirectory,
                TitleBackgroundWorldCoordinateCorrespondenceLogic.ReportFileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] Failed to delete world coordinate correspondence report.");
        }
    }

    private void PersistWorldCoordinateCorrespondenceReport()
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            var text = BuildWorldCoordinateCorrespondenceReportText();
            File.WriteAllText(
                Path.Combine(_configDirectory, TitleBackgroundWorldCoordinateCorrespondenceLogic.ReportFileName),
                text);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] Failed to write world coordinate correspondence report.");
        }
    }

    // Simple「現在地を保存（実験）」ボタンの押下可否。ログイン中・候補確定・territory 一致が条件。
    internal TitleBackgroundStandingCaptureAvailability GetStandingPositionCaptureAvailability()
    {
        if (!_clientState.IsLoggedIn)
        {
            return TitleBackgroundStandingCaptureAvailability.NotLoggedIn;
        }

        var candidate = ResolveCurrentOverrideCandidate();
        if (string.IsNullOrEmpty(TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(candidate.Id))
            || candidate.TerritoryId == 0)
        {
            return TitleBackgroundStandingCaptureAvailability.NoCandidate;
        }

        if (_clientState.TerritoryType != candidate.TerritoryId)
        {
            return TitleBackgroundStandingCaptureAvailability.TerritoryMismatch;
        }

        return TitleBackgroundStandingCaptureAvailability.Available;
    }

    // capture ボタンの押下可否（UI が enable/無効理由に使う）。
    internal TitleBackgroundAnchorCaptureAvailability GetAnchorCaptureAvailability()
    {
        var isCharaSelect = !_clientState.IsLoggedIn
            && TryReadCurrentLobbyMap(out var lobbyMap)
            && lobbyMap == GameLobbyType.CharaSelect;
        return TitleBackgroundAnchorCaptureGate.Evaluate(_clientState.IsLoggedIn, isCharaSelect);
    }

    // 手動候補の layerFilterKey を ±1 順送りし、その候補が有効なら即時再適用する。
    // layer 一覧が取得できない環境向けの探索フォールバック。書き込みは config と背景差し替えのみ。
    public uint StepManualCandidateLayerFilterKey(int direction)
    {
        var stepped = TitleBackgroundLayerStepLogic.Step(
            _configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey,
            direction);
        if (stepped == _configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey)
        {
            return stepped;
        }

        _configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey = stepped;
        // 現在の有効候補が手動候補なら、layer 変更を背景へ反映する。
        if (string.Equals(
                _configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.ManualSlot1CandidateId,
                StringComparison.Ordinal))
        {
            _configuration.TitleBackgroundLayoutLayerFilterKey = stepped;
        }

        _configuration.Save();
        ApplyFromConfiguration();
        return stepped;
    }

    private bool TryCaptureActiveCameraSnapshot(out TitleBackgroundActiveCameraSnapshot snapshot, out string errorMessage)
    {
        snapshot = default;
        errorMessage = string.Empty;
        try
        {
            var cameraManager = CameraManager.Instance();
            if (cameraManager == null)
            {
                errorMessage = "CameraManager.Instance() unavailable";
                return false;
            }

            var activeCamera = cameraManager->GetActiveCamera();
            if (activeCamera == null)
            {
                errorMessage = "active camera unavailable";
                return false;
            }

            var sceneCameraPosition = ToNumerics(activeCamera->CameraBase.SceneCamera.Position);
            if (!TitleBackgroundCameraMath.IsFiniteVector(sceneCameraPosition))
            {
                errorMessage = "SceneCamera.Position contains non-finite values";
                return false;
            }

            var lookAtVector = ToNumerics(activeCamera->CameraBase.SceneCamera.LookAtVector);
            if (!TitleBackgroundCameraMath.IsFiniteVector(lookAtVector))
            {
                errorMessage = "SceneCamera.LookAtVector contains non-finite values";
                return false;
            }

            snapshot = new TitleBackgroundActiveCameraSnapshot(
                sceneCameraPosition,
                lookAtVector,
                float.IsFinite(activeCamera->DirH) ? activeCamera->DirH : null,
                float.IsFinite(activeCamera->DirV) ? activeCamera->DirV : null,
                float.IsFinite(activeCamera->Distance) ? activeCamera->Distance : null,
                float.IsFinite(activeCamera->InterpDistance) ? activeCamera->InterpDistance : null,
                float.IsFinite(activeCamera->FoV) ? activeCamera->FoV : null);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _log.Warning(ex, "TitleBackground active camera capture failed.");
            return false;
        }
    }

    private void MarkPostFixOnCameraCaptureFailed(string reason)
    {
        _cameraObservation.LastPostFixOnCameraCaptureStatus = "failed";
        _cameraObservation.LastPostFixOnCameraCaptureError = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
    }

    private void ClearPostFixOnCameraObservation()
    {
        _cameraObservation.LastPostFixOnCameraCaptureStatus = "unavailable";
        _cameraObservation.LastPostFixOnCameraCaptureError = string.Empty;
        _cameraObservation.LastPostFixOnSceneCameraPosition = null;
        _cameraObservation.LastPostFixOnLookAtVector = null;
        _cameraObservation.LastPostFixOnDistance = null;
        _cameraObservation.LastPostFixOnFovY = null;
    }
}
