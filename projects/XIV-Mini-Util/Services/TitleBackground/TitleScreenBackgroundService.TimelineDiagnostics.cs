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
        _phase2CTimelineFrameCounter = 0;
        _phase2CTimelineStatus = "collecting";
        _phase2CTimelineError = string.Empty;
        _phase2CTimelineSnapshots.Clear();
        _phase2MPlacementFrames.Clear();
        _phase2MPlacementCaptureSceneGeneration = 0;
        _phase2MPlacementCaptureReason = "reset";
        _phase2MExperimentalWriteCount = 0;
        _runtimeRestoreAppliedFrame = null;
        _curveApplyAppliedFrame = null;
        _curveApplyRequestedMid = null;
        _curveApplyReadBackValueImmediatelyAfterWrite = null;
        _curveApplyImmediateReadBackStatus = "not-run";
        _curveApplyActiveCameraBefore = null;
        _curveApplyActiveCameraAfter = null;
        _curveApplyActiveCameraBeforeStatus = "not-run";
        _curveApplyActiveCameraAfterStatus = "not-run";
    }

    private void CapturePhase2CTimelineOnFrameworkUpdate()
    {
        if (_phase2CTimelineFrameCounter < 0)
        {
            return;
        }

        _phase2CTimelineFrameCounter++;
        if (Array.IndexOf(CameraProbeTimelineFrames, _phase2CTimelineFrameCounter) >= 0)
        {
            CapturePhase2CTimelineFrame(_phase2CTimelineFrameCounter);
        }

        if (_phase2CTimelineFrameCounter >= CameraProbeTimelineFrames[^1])
        {
            _phase2CTimelineFrameCounter = -1;
            _phase2CTimelineStatus = "complete";
        }
    }

    private void CapturePhase2CTimelineFrame(int frame)
    {
        if (_phase2CTimelineSnapshots.ContainsKey(frame))
        {
            return;
        }

        var activeCaptured = TryCaptureActiveCameraSnapshot(out var activeCamera, out var activeError);
        var lobbyCaptured = TryCaptureLobbyCameraSnapshot(out var lobbyCamera, out var lobbyError);
        var expandedCaptured = TryCaptureExpandedLobbyCameraSnapshot(out var expandedLobbyCamera, out var expandedError);
        _phase2CTimelineSnapshots[frame] = new TitleBackgroundPhase2CTimelineSnapshot(
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

        _phase2CTimelineStatus = frame >= CameraProbeTimelineFrames[^1]
            ? "complete"
            : "collecting";
        _phase2CTimelineError = activeCaptured || lobbyCaptured || expandedCaptured
            ? string.Empty
            : $"frame {frame}: active={activeError}; lobby={lobbyError}; expandedLobby={expandedError}";

        CapturePhase2MPlacementFrame(frame, frame == 0 ? "scene-ready-accepted" : "timeline");
    }

    private void CapturePhase2MPlacementFrame(int frame, string reason)
    {
        if (!TitleBackgroundPhase2MPlacementDiagnostic.ShouldCaptureFrame(frame)
            || _phase2MPlacementFrames.ContainsKey(frame))
        {
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

        var actorResult = CapturePhase2MActorCandidate(configuredCharacterPosition, activeLookAt, activeCameraPosition);
        var actor = actorResult.Actor;
        var actorPosition = actor?.Position;
        _phase2MPlacementCaptureSceneGeneration = _charaSelectCameraAdapter.RuntimeState.SceneGeneration;
        _phase2MPlacementCaptureReason = reason;

        _phase2MPlacementFrames[frame] = new TitleBackgroundPhase2MPlacementFrame(
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
                : null);
    }

    private TitleBackgroundPhase2MActorCandidateResult CapturePhase2MActorCandidate(
        Vector3 configuredCharacterPosition,
        Vector3? activeLookAt,
        Vector3? activeCameraPosition)
    {
        try
        {
            var scanned = new List<TitleBackgroundPhase2MActorCandidate>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var sources = new List<TitleBackgroundPhase2MSourceDiscovery>();
            var length = Math.Max(0, _objectTable.Length);
            var beforeCount = scanned.Count;
            for (var index = 0; index < length; index++)
            {
                AddPhase2MScannedObject(scanned, seenKeys, index, "ObjectTable", _objectTable[index], configuredCharacterPosition, activeLookAt, activeCameraPosition);
            }
            sources.Add(BuildPhase2MSourceDiscovery("ObjectTable", true, length, scanned, beforeCount, string.Empty));

            var sourceIndex = 0;
            beforeCount = scanned.Count;
            foreach (var gameObject in _objectTable.PlayerObjects)
            {
                sourceIndex++;
                AddPhase2MScannedObject(scanned, seenKeys, sourceIndex, "PlayerObjects", gameObject, configuredCharacterPosition, activeLookAt, activeCameraPosition);
            }
            sources.Add(BuildPhase2MSourceDiscovery("PlayerObjects", true, sourceIndex, scanned, beforeCount, string.Empty));

            sourceIndex = 0;
            beforeCount = scanned.Count;
            foreach (var gameObject in _objectTable.CharacterManagerObjects)
            {
                sourceIndex++;
                AddPhase2MScannedObject(scanned, seenKeys, sourceIndex, "CharacterManagerObjects", gameObject, configuredCharacterPosition, activeLookAt, activeCameraPosition);
            }
            sources.Add(BuildPhase2MSourceDiscovery("CharacterManagerObjects", true, sourceIndex, scanned, beforeCount, string.Empty));
            sources.Add(new TitleBackgroundPhase2MSourceDiscovery("ClientObjectManager", false, 0, 0, "not-exposed-through-managed-api"));
            sources.Add(new TitleBackgroundPhase2MSourceDiscovery("CharaSelectCharacterManager", false, 0, 0, "native-source-not-resolved"));
            sources.Add(new TitleBackgroundPhase2MSourceDiscovery("UIStage CharaSelect model source", false, 0, 0, "native-source-not-resolved"));
            sources.Add(new TitleBackgroundPhase2MSourceDiscovery("DrawObject owner/source", false, 0, 0, "reverse-lookup-not-exposed-through-managed-api"));

            var stats = BuildPhase2MObjectTableStats(scanned);
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
            var matchingCandidates = candidates
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
                return new TitleBackgroundPhase2MActorCandidateResult(
                    TitleBackgroundPhase2MActorMatchKind.None,
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

            var stableCandidates = FilterPhase2MStableCandidates(matchingCandidates);
            if (stableCandidates.Length == 1)
            {
                return new TitleBackgroundPhase2MActorCandidateResult(
                    TitleBackgroundPhase2MActorMatchKind.Single,
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
                return new TitleBackgroundPhase2MActorCandidateResult(
                    TitleBackgroundPhase2MActorMatchKind.Single,
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

            return new TitleBackgroundPhase2MActorCandidateResult(
                TitleBackgroundPhase2MActorMatchKind.Ambiguous,
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
            return new TitleBackgroundPhase2MActorCandidateResult(
                TitleBackgroundPhase2MActorMatchKind.None,
                null,
                [],
                [new TitleBackgroundPhase2MSourceDiscovery("ObjectTable", false, 0, 0, ex.GetType().Name)],
                0,
                default,
                "error",
                "none",
                $"error:{ex.GetType().Name}",
                "objectTable-unavailable-or-not-exposed",
                "native character-select actor manager or lobby UI character instance");
        }
    }

    private static void AddPhase2MScannedObject(
        List<TitleBackgroundPhase2MActorCandidate> candidates,
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

        var candidate = TryCreatePhase2MActorCandidate(sourceIndex, source, gameObject, configuredCharacterPosition, activeLookAt, activeCameraPosition);
        if (!candidate.HasValue)
        {
            return;
        }

        if (seenKeys.Add(BuildPhase2MCandidateKey(candidate.Value)))
        {
            candidates.Add(candidate.Value);
        }
    }

    private static TitleBackgroundPhase2MSourceDiscovery BuildPhase2MSourceDiscovery(
        string name,
        bool available,
        int count,
        IReadOnlyList<TitleBackgroundPhase2MActorCandidate> scanned,
        int startIndex,
        string error)
    {
        var localCandidates = scanned
            .Skip(startIndex)
            .Where(candidate => candidate.Source == name)
            .ToArray();
        return new TitleBackgroundPhase2MSourceDiscovery(
            name,
            available,
            count,
            localCandidates.Length,
            error,
            localCandidates.Count(candidate => !IsZeroPhase2MPosition(candidate.Position)),
            localCandidates.Count(candidate => candidate.DrawObjectNonNull),
            localCandidates.Count(candidate => candidate.ModelLikeNonNull));
    }

    private TitleBackgroundPhase2MActorCandidate[] FilterPhase2MStableCandidates(TitleBackgroundPhase2MActorCandidate[] candidates)
    {
        return candidates.Where(IsStablePhase2MCandidate).ToArray();
    }

    private bool IsStablePhase2MCandidate(TitleBackgroundPhase2MActorCandidate candidate)
    {
        var key = BuildPhase2MCandidateKey(candidate);
        var matched = _phase2MPlacementFrames.Values
            .SelectMany(frame => frame.ObjectCandidates)
            .Where(existing => BuildPhase2MCandidateKey(existing) == key)
            .ToArray();
        return matched.Length >= 2
            && matched.All(existing => Vector3.Distance(existing.Position, candidate.Position) <= TitleBackgroundCameraProbeReport.StabilizationVectorTolerance);
    }

    private static string BuildPhase2MCandidateKey(TitleBackgroundPhase2MActorCandidate candidate)
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

    private static bool IsZeroPhase2MPosition(Vector3 position)
    {
        return Math.Abs(position.X) <= 0.001f
            && Math.Abs(position.Y) <= 0.001f
            && Math.Abs(position.Z) <= 0.001f;
    }

    private static TitleBackgroundPhase2MObjectTableStats BuildPhase2MObjectTableStats(IReadOnlyCollection<TitleBackgroundPhase2MActorCandidate> candidates)
    {
        return new TitleBackgroundPhase2MObjectTableStats(
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

    private TitleBackgroundPhase2MObjectTableStats GetLatestPhase2MObjectTableStats()
    {
        return _phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.ObjectTableStats)
            .FirstOrDefault();
    }

    private string GetLatestPhase2MActorCandidateStatus()
    {
        return _phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.ActorCandidateStatus)
            .FirstOrDefault() ?? "none";
    }

    private string GetLatestPhase2MActorCandidateReason()
    {
        return _phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.ActorCandidateReason)
            .FirstOrDefault() ?? "none";
    }

    private string GetLatestPhase2MActorSource()
    {
        return _phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.ActorSource)
            .FirstOrDefault() ?? "none";
    }

    private string GetLatestPhase2MNextNativeSourceToInspect()
    {
        return _phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.NextNativeSourceToInspect)
            .FirstOrDefault() ?? "none";
    }

    private IReadOnlyList<TitleBackgroundPhase2MSourceDiscovery> GetLatestPhase2MSourceDiscovery()
    {
        return _phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .Select(frame => frame.SourceDiscovery)
            .FirstOrDefault() ?? [];
    }

    private static TitleBackgroundPhase2MActorCandidate? TryCreatePhase2MActorCandidate(
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
        var (score, scoreReason) = ScorePhase2MCandidate(
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

        return new TitleBackgroundPhase2MActorCandidate(
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

    private static (int Score, string Reason) ScorePhase2MCandidate(
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

    private void EvaluatePhase2MExperimentalApply(TitleBackgroundPhase2MSummary summary)
    {
        var mode = _configuration.TitleBackgroundPhase2MExperimentalApplyMode;
        var status = TitleBackgroundPhase2NDeliveryDiagnostic.EvaluateExperimentalActorPlacement(
            mode,
            summary,
            _activeCharaSelectSceneGeneration > 0
                && _phase2MPlacementCaptureSceneGeneration == _activeCharaSelectSceneGeneration,
            _charaSelectTitleBackgroundSessionActive,
            _clientState.IsLoggedIn);

        if (mode == TitleBackgroundPhase2MExperimentalApplyMode.None)
        {
            _phase2MExperimentalLastStatus = status;
            return;
        }

        if (status != "ready")
        {
            _phase2MExperimentalSkippedCount++;
            _phase2MExperimentalLastStatus = status;
            return;
        }

        _phase2MExperimentalSkippedCount++;
        _phase2MExperimentalLastStatus = mode switch
        {
            TitleBackgroundPhase2MExperimentalApplyMode.CameraAnchorOnly => "skip:unsupported-camera-anchor-write-not-exposed",
            TitleBackgroundPhase2MExperimentalApplyMode.GeneratedCurvePlusCameraAnchor => "skip:unsupported-camera-anchor-write-not-exposed",
            TitleBackgroundPhase2MExperimentalApplyMode.ActorPlacementPreviewOnly => "preview-only:target-delta-dumped",
            TitleBackgroundPhase2MExperimentalApplyMode.ActorPlacementOneShot => "skip:actor-write-not-implemented-without-validated-native-source",
            TitleBackgroundPhase2MExperimentalApplyMode.VisibilityProbeOnly => "read-only:visibility-probe-dumped",
            _ => "skip:unknown-mode",
        };
    }

    private IReadOnlyList<TitleBackgroundPhase2CTimelineSnapshot> BuildPhase2CTimelineSamples()
    {
        var samples = new List<TitleBackgroundPhase2CTimelineSnapshot>(CameraProbeTimelineFrames.Length);
        foreach (var frame in CameraProbeTimelineFrames)
        {
            samples.Add(_phase2CTimelineSnapshots.TryGetValue(frame, out var snapshot)
                ? snapshot
                : TitleBackgroundPhase2CTimelineSnapshot.Missing(frame));
        }

        return samples;
    }

    private IReadOnlyList<TitleBackgroundPhase2EProbeSample> BuildPhase2EProbeSamples()
    {
        return _phase2ECalculateLookAtYCalls
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
        _phase2ECalculateLookAtYCalls.Add(call);
        while (_phase2ECalculateLookAtYCalls.Count > Phase2EMaxRecordedCalls)
        {
            _phase2ECalculateLookAtYCalls.RemoveAt(0);
        }
    }

    private void ResetPhase2ECalculateLookAtYObservation()
    {
        _phase2ECalculateLookAtYCallCount = 0;
        _phase2ECalculateLookAtYLastError = string.Empty;
        _phase2ECalculateLookAtYCalls.Clear();
        _phase2FSetCameraCurveMidPointCallCount = 0;
        _phase2FCalculateCameraCurveLowAndHighPointCallCount = 0;
        _phase2FSetCameraCurveMidPointLastError = string.Empty;
        _phase2FCalculateCameraCurveLowAndHighPointLastError = string.Empty;
        _phase2FSetCameraCurveMidPointCalls.Clear();
        _phase2FCalculateCameraCurveLowAndHighPointCalls.Clear();
        _phase2FSetCameraCurveMidPointInterestingCalls.Clear();
        _phase2FCalculateCameraCurveLowAndHighPointInterestingCalls.Clear();
        _phase2FSetCameraCurveMidPointPreviousInputValue = null;
        _phase2FCalculateCameraCurveLowAndHighPointPreviousInputValue = null;
        _phase2GGenerationOverrideSetMidAttemptCount = 0;
        _phase2GGenerationOverrideSetMidAppliedCount = 0;
        _phase2GGenerationOverrideLowHighAttemptCount = 0;
        _phase2GGenerationOverrideLowHighAppliedCount = 0;
        _phase2GGenerationOverrideLastAppliedFrame = null;
        _phase2GGenerationOverrideLastAppliedSceneGeneration = 0;
        _phase2GGenerationOverrideLastStatus = "not-run";
        _phase2GGenerationOverrideLastSkippedReason = string.Empty;
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
            _phase2FSetCameraCurveMidPointCalls,
            _phase2FSetCameraCurveMidPointInterestingCalls,
            call,
            _phase2FSetCameraCurveMidPointPreviousInputValue);
        _phase2FSetCameraCurveMidPointPreviousInputValue = call.InputValue;
    }

    private void RecordPhase2FCalculateCameraCurveLowAndHighPointCall(TitleBackgroundPhase2FGeneratedCurveCall call)
    {
        RecordPhase2FGeneratedCurveCall(
            _phase2FCalculateCameraCurveLowAndHighPointCalls,
            _phase2FCalculateCameraCurveLowAndHighPointInterestingCalls,
            call,
            _phase2FCalculateCameraCurveLowAndHighPointPreviousInputValue);
        _phase2FCalculateCameraCurveLowAndHighPointPreviousInputValue = call.InputValue;
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
        var setCameraCurveMidPointCount = _phase2FSetCameraCurveMidPointInterestingCalls.Count(HasGeneratedCurvePointChanged);
        var calculateCameraCurveLowAndHighPointCount = _phase2FCalculateCameraCurveLowAndHighPointInterestingCalls.Count(HasGeneratedCurvePointChanged);
        var transitionFrames = _phase2FSetCameraCurveMidPointInterestingCalls
            .Concat(_phase2FCalculateCameraCurveLowAndHighPointInterestingCalls)
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
        if (_phase2GGenerationOverrideSetMidAppliedCount == 0
            && _phase2GGenerationOverrideLowHighAppliedCount == 0)
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
        if (_phase2GGenerationOverrideSetMidAppliedCount == 0
            && _phase2GGenerationOverrideLowHighAppliedCount == 0)
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
        if (_runtimeRestoreSuccessCount == 0)
        {
            return "inconclusive";
        }

        if (!_runtimeRestoreLastRestoredYaw.HasValue
            || !_runtimeRestoreLastRestoredPitch.HasValue
            || !_runtimeRestoreLastRestoredDistance.HasValue
            || !latestSample.LobbyDirH.HasValue
            || !latestSample.LobbyDirV.HasValue
            || !latestSample.LobbyDistance.HasValue
            || !latestSample.LobbyInterpDistance.HasValue)
        {
            return "inconclusive";
        }

        return IsNear(latestSample.LobbyDirH.Value, _runtimeRestoreLastRestoredYaw.Value)
            && IsNear(latestSample.LobbyDirV.Value, _runtimeRestoreLastRestoredPitch.Value)
            && IsNear(latestSample.LobbyDistance.Value, _runtimeRestoreLastRestoredDistance.Value)
            && IsNear(latestSample.LobbyInterpDistance.Value, _runtimeRestoreLastRestoredDistance.Value)
            ? "observed"
            : "not-observed";
    }

    private float? GetLatestCalculateLobbyCameraLookAtYReturnValue()
    {
        return _phase2ECalculateLookAtYCalls
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
        return _phase2CTimelineFrameCounter >= 0
            ? _phase2CTimelineFrameCounter
            : null;
    }

    private void CapturePostFixOnCameraState()
    {
        ClearPostFixOnCameraObservation();
        if (!TryCaptureActiveCameraSnapshot(out var snapshot, out var errorMessage))
        {
            MarkPostFixOnCameraCaptureFailed(errorMessage);
            return;
        }

        _lastPostFixOnSceneCameraPosition = snapshot.SceneCameraPosition;
        _lastPostFixOnLookAtVector = snapshot.LookAtVector;
        _lastPostFixOnDistance = snapshot.Distance;
        _lastPostFixOnFovY = snapshot.FovY;
        _lastPostFixOnCameraCaptureStatus = "success";
        _lastPostFixOnCameraCaptureError = string.Empty;
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
        _lastPostFixOnCameraCaptureStatus = "failed";
        _lastPostFixOnCameraCaptureError = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
    }

    private void ClearPostFixOnCameraObservation()
    {
        _lastPostFixOnCameraCaptureStatus = "unavailable";
        _lastPostFixOnCameraCaptureError = string.Empty;
        _lastPostFixOnSceneCameraPosition = null;
        _lastPostFixOnLookAtVector = null;
        _lastPostFixOnDistance = null;
        _lastPostFixOnFovY = null;
    }
}
