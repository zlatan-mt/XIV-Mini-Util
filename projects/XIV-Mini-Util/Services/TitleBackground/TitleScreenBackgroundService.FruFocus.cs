using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using XivMiniUtil.Services.CharaSelect;
using ClientVector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;

namespace XivMiniUtil.Services.TitleBackground;

// One bounded synchronization per confirmed placement; holds no native pointers.
internal sealed class TitleBackgroundFruFocusState
{
    internal const int AttemptBudget = 120;
    public bool Pending { get; private set; }
    public int SceneGeneration { get; private set; }
    public int PlacementApplyCount { get; private set; }
    public int Attempts { get; private set; }
    public string Status { get; private set; } = "not-armed";
    public Vector3? FocusBefore { get; private set; }
    public Vector3? FocusAfter { get; private set; }
    public Vector3? DrawPosition { get; private set; }
    private CharaSelectActorIdentityKey _actorKey;

    public void Arm(int sceneGeneration, int placementApplyCount, CharaSelectActorIdentityKey actorKey)
    {
        SceneGeneration = sceneGeneration;
        PlacementApplyCount = placementApplyCount;
        _actorKey = actorKey;
        Pending = true;
        Attempts = 0;
        Status = "pending";
        FocusBefore = FocusAfter = DrawPosition = null;
    }

    public void Stop()
    {
        Pending = false;
        Status = "stopped";
    }

    public bool IsEligible(
        in TitleBackgroundResolvedActorContext context,
        TitleBackgroundCharaSelectPlacementRuntimeState placement,
        bool automaticRun)
        => Pending && !automaticRun && context.Valid
            && context.CandidateId == TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId
            && context.Actor.IdentityKey == placement.LastAppliedActorKey
            && context.Actor.IdentityKey == _actorKey
            && context.ActiveSceneGeneration == SceneGeneration
            && placement.LastAppliedSceneGeneration == SceneGeneration
            && placement.PlacementApplyCount == PlacementApplyCount
            && placement.LastAppliedCandidateId == context.CandidateId
            && placement.LastWriteReadbackConfirmed
            && !placement.LoginStopped;

    public bool TrySynchronize(ref ClientVector3 focus, Vector3? drawPosition, Vector3 placedPosition)
    {
        if (!Pending)
            return false;

        Attempts++;
        FocusBefore = new Vector3(focus.X, focus.Y, focus.Z);
        DrawPosition = drawPosition;
        var epsilon = TitleBackgroundCharaSelectPlacementLogic.CapturePositionEpsilon;
        if (!TitleBackgroundCameraMath.IsFiniteVector(FocusBefore.Value)
            || !TitleBackgroundCameraMath.IsFiniteVector(placedPosition)
            || !drawPosition.HasValue
            || !TitleBackgroundCameraMath.IsFiniteVector(drawPosition.Value)
            || Math.Abs(drawPosition.Value.X - placedPosition.X) > epsilon
            || Math.Abs(drawPosition.Value.Z - placedPosition.Z) > epsilon)
        {
            Pending = Attempts < AttemptBudget;
            Status = Pending ? "waiting-for-draw-or-focus" : "retry-exhausted";
            return false;
        }

        var changed = Math.Abs(focus.X - drawPosition.Value.X) > epsilon
            || Math.Abs(focus.Z - drawPosition.Value.Z) > epsilon;
        if (changed)
        {
            // User-approved FRU exception: only horizontal focus follows the confirmed actor.
            // Native height curves, camera position, angles, distance and FoV retain ownership.
            focus.X = drawPosition.Value.X;
            focus.Z = drawPosition.Value.Z;
        }

        FocusAfter = new Vector3(focus.X, focus.Y, focus.Z);
        Pending = false;
        Status = changed ? "applied" : "already-aligned";
        return changed;
    }
}

public sealed unsafe partial class TitleScreenBackgroundService
{
    private TitleBackgroundFruFocusState FruFocus => _charaSelectPlacement.FruFocus;

    private void TrySynchronizeFruFocusAfterCurveOriginal(nint self)
    {
        // Stop before resolving actors or accessing CameraManager on every forbidden path.
        if (!FruFocus.Pending || _hookLifecycle.Disposed || _clientState.IsLoggedIn
            || _hookLifecycle.State != TitleBackgroundServiceState.Ready || IsHookProbeMode()
            || !IsCharaSelectPlacementActive || !_charaSelectTitleBackgroundSessionActive
            || IsSavedViewSuppressedByAutomaticRun() || _automaticCheck.PlacementProofArmed
            || !TryReadCurrentLobbyMap(out var map) || map != GameLobbyType.CharaSelect)
            return;

        var candidate = ResolveCurrentOverrideCandidate().Id;
        if (candidate != TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId
            || !TryResolveCharaSelectActorContext(out var actor))
            return;

        var context = new TitleBackgroundResolvedActorContext(
            actor, true, true, true, false, true,
            _activeCharaSelectSceneGeneration, _charaSelectPlacement.SceneGeneration,
            true, candidate,
            candidate == _configuration.TitleBackgroundCharaSelectPlacementCandidateId);
        if (!FruFocus.IsEligible(context, _charaSelectPlacement, automaticRun: false))
            return;

        var manager = CameraManager.Instance();
        if (manager == null || manager->LobbyCamera == null || (nint)manager->LobbyCamera != self)
            return;

        Vector3? drawPosition = actor.DrawReady
            && TitleBackgroundCharacterSourceProbe.TryReadCharaSelectCharacterAim(actor, out var position, out _)
                ? position : null;
        // This existing hook runs before camera update snapshots focus X/Z into native locals.
        // CalculateLobbyCameraLookAtY is too late: its X/Z writes would be overwritten afterwards.
        FruFocus.TrySynchronize(
            ref manager->LobbyCamera->Camera.CameraBase.SceneCamera.LookAtVector,
            drawPosition, _charaSelectPlacement.LastAppliedPosition);
        _coldStartDiagnostic.RecordFruFocusEvidence(FruFocus);
    }
}
