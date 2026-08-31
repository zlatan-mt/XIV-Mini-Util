// Path: tools/CharaSelectLogicTests/Tests/TitleBackgroundPlacementTestShims.cs
// Description: PR #7 で TitleBackground CharaSelect placement の runtime-state / 純粋ロジック API が
//              raw pointer から pointer-free な CharaSelectActorIdentityKey + canonical resolved context
//              へ移行した際に、既存テストの primitive 引数表現を新 API へ橋渡しする test-only 互換シム。
// Reason: 「同一 / 変化した actor identity」「retry budget」「login freeze」等のテスト意図をそのまま保ち、
//         本体 API の統合（capture / write が唯一の canonical resolver context を共有する）へ追随する。
//         production コードは一切変更しない。
using System;
using System.Numerics;
using XivMiniUtil.Services.CharaSelect;
using XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundPlacementTestShims
{
    // primitive の「pointer 相当」を pointer-free identity key へ。
    // Valid 条件 = ContentId != 0 && ClientObjectIndex >= 0。0 は「未解決 actor」を表す。
    public static CharaSelectActorIdentityKey Key(ulong contentIdLike, short clientObjectIndex = 0)
        => contentIdLike == 0
            ? default
            : new CharaSelectActorIdentityKey(contentIdLike, clientObjectIndex, 0, 0);

    // ---- pure logic（static メソッド）の pointer 引数版 ----
    public static bool ShouldWrite(
        int gateSceneGeneration,
        int lastAppliedSceneGeneration,
        ulong currentActorPtr,
        ulong lastAppliedActorPtr,
        bool captureJustCompleted = false,
        bool selectionChangePending = false)
        => TitleBackgroundCharaSelectPlacementLogic.ShouldWritePlacement(
            gateSceneGeneration,
            lastAppliedSceneGeneration,
            Key(currentActorPtr),
            Key(lastAppliedActorPtr),
            captureJustCompleted,
            selectionChangePending);

    public static TitleBackgroundCharaSelectPlacementTrigger Trigger(
        int gateSceneGeneration,
        int lastAppliedSceneGeneration,
        ulong currentActorPtr,
        ulong lastAppliedActorPtr,
        bool captureJustCompleted,
        bool selectionChangePending)
        => TitleBackgroundCharaSelectPlacementLogic.ResolvePlacementTrigger(
            gateSceneGeneration,
            lastAppliedSceneGeneration,
            Key(currentActorPtr),
            Key(lastAppliedActorPtr),
            captureJustCompleted,
            selectionChangePending);

    // ---- runtime-state instance メソッドの primitive 引数版（同名 extension；名前付き引数で解決される）----
    public static void RecordCharacterResolve(
        this TitleBackgroundCharaSelectPlacementRuntimeState state,
        bool resolved,
        string resolveSource,
        bool entryAvailable,
        bool selectedContentAvailable,
        bool mappingAvailable,
        bool mappingHit,
        bool clientObjectIndexValid,
        bool objectResolved,
        bool drawReady,
        int retryCount,
        bool actorChanged)
    {
        _ = actorChanged;
        state.RecordCharacterResolve(
            BuildContext(
                resolved,
                resolveSource,
                entryAvailable,
                selectedContentAvailable,
                mappingAvailable,
                mappingHit,
                clientObjectIndexValid,
                objectResolved,
                drawReady),
            retryCount);
    }

    public static void RecordPlacementApplied(
        this TitleBackgroundCharaSelectPlacementRuntimeState state,
        int sceneGeneration,
        ulong actorPtr,
        Vector3 position,
        float rotation,
        int frame,
        string trigger = "none",
        short clientObjectIndex = 0)
        => state.RecordPlacementApplied(
            sceneGeneration,
            Key(actorPtr, clientObjectIndex),
            "custom:n4f4",
            position,
            rotation,
            frame,
            trigger);

    public static void RecordCapturePersisted(
        this TitleBackgroundCharaSelectPlacementRuntimeState state,
        int stableSamples,
        bool zeroAccepted,
        string candidateId,
        Vector3 position,
        float rotation)
        => state.RecordCapturePersisted(
            stableSamples,
            zeroAccepted,
            position,
            rotation,
            BuildContext(
                true,
                "SelectedCharacterIndex",
                entryAvailable: true,
                selectedContentAvailable: true,
                mappingAvailable: true,
                mappingHit: true,
                clientObjectIndexValid: true,
                objectResolved: true,
                drawReady: false,
                candidateId: candidateId));

    public static void RecordPlacementWriteAttempt(
        this TitleBackgroundCharaSelectPlacementRuntimeState state,
        int sceneGeneration,
        ulong actorPtr,
        bool setterCallCompleted,
        bool positionReadbackConfirmed,
        bool rotationReadbackConfirmed,
        string status,
        short clientObjectIndex = 0)
        => state.RecordPlacementWriteAttempt(
            sceneGeneration,
            Key(actorPtr, clientObjectIndex),
            "custom:n4f4",
            setterCallCompleted,
            positionReadbackConfirmed,
            rotationReadbackConfirmed,
            status);

    public static void RecordCaptureSampleIdentity(
        this TitleBackgroundCharaSelectPlacementRuntimeState state,
        ulong actorPtr,
        ulong sceneGeneration,
        short clientObjectIndex,
        int resolveSource)
        => state.RecordCaptureSampleIdentity(
            Key(actorPtr, clientObjectIndex),
            (int)sceneGeneration,
            "custom:n4f4",
            resolveSource.ToString());

    public static bool CaptureIdentityMatches(
        this TitleBackgroundCharaSelectPlacementRuntimeState state,
        ulong actorPtr,
        ulong sceneGeneration,
        short clientObjectIndex,
        int resolveSource)
        => state.CaptureIdentityMatches(
            Key(actorPtr, clientObjectIndex),
            (int)sceneGeneration,
            "custom:n4f4",
            resolveSource.ToString());

    public static bool CanAttemptPlacementWrite(
        this TitleBackgroundCharaSelectPlacementRuntimeState state,
        int sceneGeneration,
        ulong actorPtr,
        short clientObjectIndex,
        ulong contentIdOverride = 0)
        => state.CanAttemptPlacementWrite(
            sceneGeneration,
            Key(contentIdOverride != 0 ? contentIdOverride : actorPtr, clientObjectIndex),
            "custom:n4f4");

    public static ulong LastAppliedCharacterPtr(this TitleBackgroundCharaSelectPlacementRuntimeState state)
        => state.LastAppliedContentId;

    private static TitleBackgroundResolvedActorContext BuildContext(
        bool resolved,
        string resolveSource,
        bool entryAvailable,
        bool selectedContentAvailable,
        bool mappingAvailable,
        bool mappingHit,
        bool clientObjectIndexValid,
        bool objectResolved,
        bool drawReady,
        string candidateId = "custom:n4f4",
        int runtimeSceneGeneration = 1,
        ulong contentIdLike = 0x100)
    {
        var source = Enum.TryParse<CharaSelectIdentityResolveSource>(resolveSource, out var parsed)
            ? parsed
            : CharaSelectIdentityResolveSource.None;
        var actor = resolved
            ? new CharaSelectResolvedActorContext(
                (nint)0x1000,
                new CharaSelectActorIdentityKey(contentIdLike, 0, 0, 0),
                0,
                source,
                CurrentCharacterAvailable: true,
                EntryAvailable: entryAvailable,
                SelectedContentAvailable: selectedContentAvailable,
                MappingAvailable: mappingAvailable,
                MappingHit: mappingHit,
                ClientObjectIndexValid: clientObjectIndexValid,
                ObjectResolved: objectResolved,
                IdentityConsistent: true,
                DrawReady: drawReady)
            : CharaSelectResolvedActorContext.Unresolved(
                -1,
                false,
                entryAvailable,
                selectedContentAvailable,
                mappingAvailable,
                mappingHit,
                clientObjectIndexValid,
                objectResolved,
                false);
        return new TitleBackgroundResolvedActorContext(
            actor,
            PlacementPathActive: true,
            PreLogin: true,
            ServiceReady: true,
            HookProbeMode: false,
            CharaSelectSessionActive: true,
            ActiveSceneGeneration: runtimeSceneGeneration,
            RuntimeSceneGeneration: runtimeSceneGeneration,
            IsCharaSelectMap: true,
            CandidateId: candidateId,
            CandidateMatches: true);
    }
}
