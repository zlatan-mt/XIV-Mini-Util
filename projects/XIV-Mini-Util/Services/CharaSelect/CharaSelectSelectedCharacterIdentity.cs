// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectSelectedCharacterIdentity.cs
// Description: Character Select の current actor を canonical に解決した同一フレーム context と、
//              pointer を含まない安定 identity key / 解決順序の純粋ロジック。
// Reason: selected fields が unset の通常状態でも GetCurrentCharacter と CharacterMapping の一致から
//         source-backed に解決し、Capture/Write が別 actor source を探さないようにする。

namespace XivMiniUtil.Services.CharaSelect;

internal enum CharaSelectIdentityResolveSource
{
    None,
    // CharaSelectCharacterList.GetCurrentCharacter() と CharacterMapping -> GetObjectByIndex の pointer 一致。
    CurrentCharacterMapping,
    // AgentLobby.SelectedCharacterIndex -> LobbyData entry -> CharacterMapping -> object。
    SelectedCharacterIndex,
    // AgentLobby.SelectedCharacterContentId -> CharacterMapping -> object。
    SelectedContentId,
    // AgentLobby.HoveredCharacterContentId / HoveredCharacterIndex -> CharacterMapping -> object。
    HoveredCharacter,
}

// runtime state に保持してよい pointer 非依存の actor identity。
// ContentId は内部比較専用で report へ出さない。
internal readonly record struct CharaSelectActorIdentityKey(
    ulong ContentId,
    short ClientObjectIndex,
    ushort ObjectIndex,
    uint EntityId)
{
    public bool Valid => ContentId != 0 && ClientObjectIndex >= 0;
}

// CharacterAddress はこの context を得た frame / bounded operation 内だけで使用する。
// runtime state や report へ保存しない。
internal readonly record struct CharaSelectResolvedActorContext(
    nint CharacterAddress,
    CharaSelectActorIdentityKey IdentityKey,
    int NormalizedIndex,
    CharaSelectIdentityResolveSource Source,
    bool CurrentCharacterAvailable,
    bool EntryAvailable,
    bool SelectedContentAvailable,
    bool MappingAvailable,
    bool MappingHit,
    bool ClientObjectIndexValid,
    bool ObjectResolved,
    bool IdentityConsistent,
    bool DrawReady,
    // Read-only, pointer-free actor visual-state facts (H8 cold-start recorder extension).
    // VisualStateCaptured=false when the actor address was unavailable or the native read failed;
    // callers must not infer "hidden" from an uncaptured state.
    //
    // VisibilityRaw/VisibilityHidden use GameObject.Visibility, whose documented SetVisibility(byte)
    // counterpart is "0 shows the object, 1 hides it" — the only visibility signal with confirmed
    // semantics. VisibilityHidden is null for any raw value other than 0/1 (conservative: unknown, not
    // assumed hidden or visible). RenderFlags is NOT used for visibility/model classification: current
    // FFXIVClientStructs documents it only as "some bits hide, some show" without a confirmed direction
    // for the Model bit, so RenderFlagsModelBitSet is kept as a raw neutral fact only (ChatGPT exact-HEAD
    // review 5118977128 MUST FIX).
    bool VisualStateCaptured = false,
    byte VisibilityRaw = 0,
    bool? VisibilityHidden = null,
    // TargetableStatus & ObjectTargetableFlags.ReadyToDraw, captured as an independent typed
    // observation per the H8 plan (distinct from the existing DrawReady, which is IsReadyToDraw()).
    bool ReadyToDrawFlag = false,
    uint RenderFlagsRaw = 0,
    bool RenderFlagsModelBitSet = false,
    bool DrawObjectPresent = false,
    bool ScaleFinitePositive = false,
    bool DrawOffsetFinite = false,
    bool DrawOffsetNonZero = false)
{
    public ulong ContentId => IdentityKey.ContentId;
    public short ClientObjectIndex => IdentityKey.ClientObjectIndex;
    public ushort ObjectIndex => IdentityKey.ObjectIndex;
    public uint EntityId => IdentityKey.EntityId;

    public bool Valid => CharacterAddress != nint.Zero
        && Source != CharaSelectIdentityResolveSource.None
        && MappingAvailable
        && MappingHit
        && ClientObjectIndexValid
        && ObjectResolved
        && IdentityConsistent
        && IdentityKey.Valid;

    public static CharaSelectResolvedActorContext Unresolved(
        int normalizedIndex = -1,
        bool currentCharacterAvailable = false,
        bool entryAvailable = false,
        bool selectedContentAvailable = false,
        bool mappingAvailable = false,
        bool mappingHit = false,
        bool clientObjectIndexValid = false,
        bool objectResolved = false,
        bool identityConsistent = false)
        => new(
            nint.Zero,
            default,
            normalizedIndex,
            CharaSelectIdentityResolveSource.None,
            currentCharacterAvailable,
            entryAvailable,
            selectedContentAvailable,
            mappingAvailable,
            mappingHit,
            clientObjectIndexValid,
            objectResolved,
            identityConsistent,
            false);

    public string DescribeForDiagnostics()
        => $"source={Source}; currentCharacterAvailable={CurrentCharacterAvailable}; entryAvailable={EntryAvailable}; "
           + $"selectedContentAvailable={SelectedContentAvailable}; mappingAvailable={MappingAvailable}; "
           + $"mappingHit={MappingHit}; clientObjectIndexValid={ClientObjectIndexValid}; "
           + $"objectResolved={ObjectResolved}; identityConsistent={IdentityConsistent}; drawReady={DrawReady}; "
           + $"visualStateCaptured={VisualStateCaptured}; visibilityRaw={VisibilityRaw}; "
           + $"visibilityHidden={(VisibilityHidden.HasValue ? VisibilityHidden.Value.ToString() : "none")}; "
           + $"readyToDrawFlag={ReadyToDrawFlag}; drawObjectPresent={DrawObjectPresent}";
}

internal static class CharaSelectSelectedCharacterIdentityLogic
{
    // TitleEdit の current-character semantics に合わせ、pointer↔mapping 一致を最優先にする。
    // selected fields は明示選択時の strong source、hovered は最後の fallback。
    public static IReadOnlyList<CharaSelectIdentityResolveSource> BuildResolveOrder(
        bool hasCurrentCharacterMapping,
        bool hasSelectedIndexContentId,
        bool hasSelectedContentId,
        bool hasHoveredContentId)
    {
        var order = new List<CharaSelectIdentityResolveSource>(4);
        if (hasCurrentCharacterMapping)
        {
            order.Add(CharaSelectIdentityResolveSource.CurrentCharacterMapping);
        }

        if (hasSelectedIndexContentId)
        {
            order.Add(CharaSelectIdentityResolveSource.SelectedCharacterIndex);
        }

        if (hasSelectedContentId)
        {
            order.Add(CharaSelectIdentityResolveSource.SelectedContentId);
        }

        if (hasHoveredContentId)
        {
            order.Add(CharaSelectIdentityResolveSource.HoveredCharacter);
        }

        return order;
    }

    public static int NormalizeSelectedIndex(int rawIndex)
    {
        // API15 の SelectedCharacterIndex は byte で、0xFF は「未選択」の sentinel。
        if (rawIndex == 0xFF)
        {
            return -1;
        }

        var normalized = rawIndex >= 100 ? rawIndex - 100 : rawIndex;
        return normalized is >= 0 and < 40 ? normalized : -1;
    }
}
