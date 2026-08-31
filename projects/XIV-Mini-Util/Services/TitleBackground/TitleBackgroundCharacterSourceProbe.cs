// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharacterSourceProbe.cs
// Description: Character Select 中の current character を read-only で snapshot 化する
// Reason: post-login pointer 再参照や推測 signature を使わず native source を診断するため
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;
using XivMiniUtil.Services.CharaSelect;

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

internal static unsafe class TitleBackgroundCharacterSourceProbe
{
    // Read-only: returns the live CharaSelect character's draw-object world position
    // and facing. Caller must gate on pre-login + CharaSelect; this never writes.
    public static bool TryReadCharaSelectCharacterAim(
        in CharaSelectResolvedActorContext actor,
        out Vector3 position,
        out float rotation)
    {
        position = Vector3.Zero;
        rotation = 0f;
        try
        {
            if (!actor.Valid)
            {
                return false;
            }

            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)actor.CharacterAddress;

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
    public static bool TrySetCurrentCharacterDrawPosition(
        in CharaSelectResolvedActorContext actor,
        Vector3 position)
    {
        try
        {
            if (!actor.Valid || !TitleBackgroundCameraMath.IsFiniteVector(position))
            {
                return false;
            }

            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)actor.CharacterAddress;

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

    // NOTE: actor identity 解決は CharaSelectService.TryResolveCurrentCharaSelectActor() に一本化した。
    // helper は valid context を要求し、raw address だけを受け取る bypass を持たない。

    // Write-only placement through the native GameObject path (TitleEdit-informed): call
    // GameObject.SetPosition(x,y,z) rather than writing DrawObject->Position directly, so the
    // client's own transform bookkeeping stays consistent (matches TitleEdit's SetAllCharacterPostions
    // / CreateBattleCharacterDetour). Takes the character address pre-resolved by
    // CharaSelectService.TryResolveCurrentCharaSelectActor() to avoid a second resolve / TOCTOU.
    // Returns false on any null/failure.
    public static bool TrySetCharaSelectCharacterPosition(
        in CharaSelectResolvedActorContext actor,
        Vector3 position)
        => TrySetCharaSelectCharacterPosition(actor, position, out _, out _);

    public static bool TrySetCharaSelectCharacterPosition(
        in CharaSelectResolvedActorContext actor,
        Vector3 position,
        out Vector3 readBackPosition,
        out bool setterCallCompleted)
    {
        readBackPosition = Vector3.Zero;
        setterCallCompleted = false;
        try
        {
            if (!actor.Valid || !TitleBackgroundCameraMath.IsFiniteVector(position))
            {
                return false;
            }

            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)actor.CharacterAddress;
            character->SetPosition(position.X, position.Y, position.Z);
            setterCallCompleted = true;
            readBackPosition = new Vector3(
                character->Position.X,
                character->Position.Y,
                character->Position.Z);
            return TitleBackgroundCameraMath.IsFiniteVector(readBackPosition);
        }
        catch
        {
            return false;
        }
    }

    // Write-only facing through the actor rotation path (SetRotation(float)); DrawObject quaternion is
    // never synthesized. Takes a pre-resolved character address.
    public static bool TrySetCharaSelectCharacterRotation(
        in CharaSelectResolvedActorContext actor,
        float yaw,
        out float readBackRotation)
        => TrySetCharaSelectCharacterRotation(actor, yaw, out readBackRotation, out _);

    public static bool TrySetCharaSelectCharacterRotation(
        in CharaSelectResolvedActorContext actor,
        float yaw,
        out float readBackRotation,
        out bool setterCallCompleted)
    {
        readBackRotation = 0f;
        setterCallCompleted = false;
        try
        {
            if (!actor.Valid || !float.IsFinite(yaw))
            {
                return false;
            }

            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)actor.CharacterAddress;
            character->SetRotation(yaw);
            setterCallCompleted = true;
            readBackRotation = character->Rotation;
            return float.IsFinite(readBackRotation);
        }
        catch
        {
            return false;
        }
    }

    // Read-only: current scene-local position + facing of a pre-resolved CharaSelect character.
    // Used by the one-click-run evidence capture. Never writes.
    public static bool TryReadCharaSelectCharacterTransform(
        in CharaSelectResolvedActorContext actor,
        out Vector3 position,
        out float rotation)
    {
        position = Vector3.Zero;
        rotation = 0f;
        try
        {
            if (!actor.Valid)
            {
                return false;
            }

            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)actor.CharacterAddress;
            position = new Vector3(character->Position.X, character->Position.Y, character->Position.Z);
            rotation = character->Rotation;
            return TitleBackgroundCameraMath.IsFiniteVector(position) && float.IsFinite(rotation);
        }
        catch
        {
            return false;
        }
    }

    // Write-only facing: use GameObject.SetRotation(float) so the client updates its visual DrawObject
    // rotation through the native actor path. DrawObject.Rotation is a quaternion and is intentionally
    // not synthesized here. Caller must gate this to the existing pre-login placement slot.
    public static bool TrySetCurrentCharacterDrawRotation(
        in CharaSelectResolvedActorContext actor,
        float yaw,
        out float readBackRotation)
    {
        readBackRotation = 0f;
        try
        {
            if (!actor.Valid || !float.IsFinite(yaw))
            {
                return false;
            }

            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)actor.CharacterAddress;
            if (character->DrawObject == null)
            {
                return false;
            }

            character->SetRotation(yaw);
            readBackRotation = character->Rotation;
            return float.IsFinite(readBackRotation);
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
