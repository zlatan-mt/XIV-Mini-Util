// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharacterSourceProbe.cs
// Description: Character Select 中の current character を read-only で snapshot 化する
// Reason: post-login pointer 再参照や推測 signature を使わず native source を診断するため
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundCharacterSourceCaptureGate(
    bool Allowed,
    string Status)
{
    public static TitleBackgroundCharacterSourceCaptureGate Evaluate(
        bool isLoggedIn,
        bool isCharaSelectActive,
        int activeSceneGeneration,
        int runtimeSceneGeneration)
    {
        if (isLoggedIn)
        {
            return new(false, "skipped-post-login");
        }

        if (!isCharaSelectActive)
        {
            return new(false, "skipped-inactive-chara-select");
        }

        if (activeSceneGeneration <= 0 || activeSceneGeneration != runtimeSceneGeneration)
        {
            return new(false, "skipped-scene-generation-mismatch");
        }

        return new(true, "pre-login");
    }
}

internal readonly record struct TitleBackgroundCharacterSourceSnapshot(
    int Frame,
    string CaptureContext,
    string ReadStatus,
    nint CharacterAddress,
    nint ListAddress,
    ulong ContentId,
    short ClientObjectIndex,
    ushort ObjectIndex,
    uint EntityId,
    string ObjectKind,
    Vector3 Position,
    float Rotation,
    float Scale,
    float HitboxRadius,
    nint DrawObjectAddress,
    string Customize,
    string Error)
{
    public bool HasCharacter => CharacterAddress != nint.Zero;
    public bool HasNonZeroTransform => HasCharacter && !TitleBackgroundCharacterSourceEvaluation.IsZeroPosition(Position);
    public bool DrawObjectNonNull => DrawObjectAddress != nint.Zero;
}

internal readonly record struct TitleBackgroundCharacterSourceSummary(
    string CaptureContext,
    string ReadStatus,
    int ObservedFrameCount,
    string AddressStable,
    bool PostLoginReadAttempted,
    string BestSource,
    string Resolution,
    string Blocker);

internal static class TitleBackgroundCharacterSourceEvaluation
{
    public const string SourceName = "CharaSelectCharacterManager";

    public static TitleBackgroundCharacterSourceSummary Evaluate(
        IEnumerable<TitleBackgroundCharacterSourceSnapshot> snapshots)
    {
        var ordered = snapshots.OrderBy(snapshot => snapshot.Frame).ToArray();
        var readable = ordered
            .Where(snapshot => snapshot.ReadStatus == "read" && snapshot.HasCharacter)
            .ToArray();
        var distinctAddresses = readable.Select(snapshot => snapshot.CharacterAddress).Distinct().Count();
        var addressStable = readable.Length switch
        {
            0 => "not-observed",
            1 => "single-sample",
            _ when distinctAddresses == 1 => "true",
            _ => "false",
        };
        var postLoginReadAttempted = ordered.Any(snapshot =>
            snapshot.CaptureContext == "post-login" && snapshot.ReadStatus == "read");
        var hasNonZeroTransform = readable.Any(snapshot => snapshot.HasNonZeroTransform);
        var hasDrawObject = readable.Any(snapshot => snapshot.DrawObjectNonNull);
        var resolution = readable.Length == 0
            ? "not-found"
            : !hasNonZeroTransform
                ? "found-but-no-transform"
                : distinctAddresses > 1
                    ? "found-ambiguous"
                    : "found-single";
        var blocker = resolution switch
        {
            "found-single" => "none",
            "found-ambiguous" => "current-character-address-changed-across-frames",
            "found-but-no-transform" => "current-character-transform-is-zero",
            _ => ordered.LastOrDefault().Error is { Length: > 0 } error ? error : "current-character-not-found",
        };
        var readStatus = readable.Length > 0
            ? hasDrawObject ? "read-with-draw-object" : "read"
            : ordered.LastOrDefault().ReadStatus is { Length: > 0 } status ? status : "not-run";

        return new TitleBackgroundCharacterSourceSummary(
            ordered.Any(snapshot => snapshot.CaptureContext == "pre-login") ? "pre-login" : "not-observed",
            readStatus,
            readable.Select(snapshot => snapshot.Frame).Distinct().Count(),
            addressStable,
            postLoginReadAttempted,
            readable.Length > 0 ? SourceName : "none",
            resolution,
            blocker);
    }

    public static bool IsZeroPosition(Vector3 position)
    {
        return Math.Abs(position.X) <= 0.001f
            && Math.Abs(position.Y) <= 0.001f
            && Math.Abs(position.Z) <= 0.001f;
    }
}

internal readonly record struct TitleBackgroundCharaSelectCameraAimResult(
    bool HasAim,
    Vector3 Camera,
    Vector3 Focus,
    float FovY);

internal static class TitleBackgroundCharaSelectCameraAim
{
    // Iteration 1 framing defaults: frontal portrait, slightly above eye level.
    // Tunable from in-game feedback; kept here so the math stays pure/testable.
    public const float DefaultDistance = 2.5f;
    public const float DefaultFocusHeight = 1.3f;
    public const float DefaultCameraHeight = 1.5f;

    public static TitleBackgroundCharaSelectCameraAimResult Compute(
        Vector3 characterPosition,
        float characterRotation,
        float distance,
        float focusHeight,
        float cameraHeight,
        float fovY)
    {
        if (!TitleBackgroundCameraMath.IsFiniteVector(characterPosition)
            || !float.IsFinite(characterRotation)
            || !(distance > 0f))
        {
            return new TitleBackgroundCharaSelectCameraAimResult(false, default, default, 0f);
        }

        // FFXIV yaw: forward vector for a facing of `rot` is (sin, 0, cos).
        // Place the camera in front of the character so we see the face.
        var forwardX = MathF.Sin(characterRotation);
        var forwardZ = MathF.Cos(characterRotation);
        var camera = new Vector3(
            characterPosition.X + forwardX * distance,
            characterPosition.Y + cameraHeight,
            characterPosition.Z + forwardZ * distance);
        var focus = new Vector3(
            characterPosition.X,
            characterPosition.Y + focusHeight,
            characterPosition.Z);
        return new TitleBackgroundCharaSelectCameraAimResult(
            true,
            camera,
            focus,
            TitleBackgroundPreset.ClampFovY(fovY));
    }
}

internal static unsafe class TitleBackgroundCharacterSourceProbe
{
    // Read-only: returns the live CharaSelect character's draw-object world position
    // and facing. Caller must gate on pre-login + CharaSelect; this never writes.
    public static bool TryReadCurrentCharacterAim(out Vector3 position, out float rotation)
    {
        position = Vector3.Zero;
        rotation = 0f;
        try
        {
            var character = CharaSelectCharacterList.GetCurrentCharacter();
            if (character == null)
            {
                return false;
            }

            var drawObject = character->DrawObject;
            if (drawObject == null)
            {
                return false;
            }

            var drawPosition = drawObject->Position;
            position = new Vector3(drawPosition.X, drawPosition.Y, drawPosition.Z);
            rotation = character->Rotation;
            return TitleBackgroundCameraMath.IsFiniteVector(position) && float.IsFinite(rotation);
        }
        catch
        {
            return false;
        }
    }

    // Write-only placement: move the live CharaSelect character's draw object to a
    // world position. Caller must gate on pre-login + CharaSelect. Used by the
    // "place character at the camera focus" compositing path so the camera is never
    // fought (no jitter). Returns false on any null/failure.
    public static bool TrySetCurrentCharacterDrawPosition(Vector3 position)
    {
        try
        {
            if (!TitleBackgroundCameraMath.IsFiniteVector(position))
            {
                return false;
            }

            var character = CharaSelectCharacterList.GetCurrentCharacter();
            if (character == null)
            {
                return false;
            }

            var drawObject = character->DrawObject;
            if (drawObject == null)
            {
                return false;
            }

            drawObject->Position = new FFXIVClientStructs.FFXIV.Common.Math.Vector3(position.X, position.Y, position.Z);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static TitleBackgroundCharacterSourceSnapshot Capture(int frame)
    {
        try
        {
            var list = CharaSelectCharacterList.Instance();
            var character = CharaSelectCharacterList.GetCurrentCharacter();
            if (character == null)
            {
                return new TitleBackgroundCharacterSourceSnapshot(
                    frame,
                    "pre-login",
                    "character-null",
                    nint.Zero,
                    (nint)list,
                    0,
                    -1,
                    0,
                    0,
                    "none",
                    Vector3.Zero,
                    0,
                    0,
                    0,
                    nint.Zero,
                    "none",
                    list == null ? "character-list-null" : "current-character-null");
            }

            ulong contentId = 0;
            short clientObjectIndex = -1;
            if (list != null)
            {
                var mappings = list->CharacterMapping;
                for (var i = 0; i < mappings.Length; i++)
                {
                    var mapping = mappings[i];
                    if (mapping.ClientObjectIndex < 0
                        || mapping.ClientObjectIndex != (short)character->ObjectIndex)
                    {
                        continue;
                    }

                    contentId = mapping.ContentId;
                    clientObjectIndex = mapping.ClientObjectIndex;
                    break;
                }
            }

            var customize = character->DrawData.CustomizeData;
            return new TitleBackgroundCharacterSourceSnapshot(
                frame,
                "pre-login",
                "read",
                (nint)character,
                (nint)list,
                contentId,
                clientObjectIndex,
                character->ObjectIndex,
                character->EntityId,
                character->ObjectKind.ToString(),
                new Vector3(character->Position.X, character->Position.Y, character->Position.Z),
                character->Rotation,
                character->Scale,
                character->HitboxRadius,
                (nint)character->DrawObject,
                $"race={customize.Race};tribe={customize.Tribe};sex={customize.Sex}",
                "none");
        }
        catch (Exception ex)
        {
            return new TitleBackgroundCharacterSourceSnapshot(
                frame,
                "pre-login",
                "read-error",
                nint.Zero,
                nint.Zero,
                0,
                -1,
                0,
                0,
                "none",
                Vector3.Zero,
                0,
                0,
                0,
                nint.Zero,
                "none",
                ex.GetType().Name);
        }
    }
}
