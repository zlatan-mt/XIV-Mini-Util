// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectService.SelectedCharacterIdentity.cs
// Description: Character Select の current actor を解決する唯一の canonical resolver。
// Reason: default Character Select では selected fields が unset でも GetCurrentCharacter が有効なため、
//         current pointer と CharacterMapping -> GetObjectByIndex の一致を primary にする。
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace XivMiniUtil.Services.CharaSelect;

public sealed unsafe partial class CharaSelectService
{
    // SelectedCharacterChanged の dedupe は pointer を保持せず、source-backed identity key だけで行う。
    private CharaSelectActorIdentityKey _lastNotifiedSelectedActorKey;

    // 選択 identity または実 actor instance が変わったときに発火する。
    // native write は購読側の Framework tick で canonical context を再解決してから行う。
    internal event Action? SelectedCharacterChanged;

    // canonical resolver。false / context.Valid == false なら capture/write は fail-closed。
    // CharacterAddress は呼び出し frame 内だけで使い、runtime state に保存しない。
    internal bool TryResolveCurrentCharaSelectActor(out CharaSelectResolvedActorContext context)
    {
        context = CharaSelectResolvedActorContext.Unresolved();
        if (_disposed || _clientState.IsLoggedIn)
        {
            return false;
        }

        try
        {
            var agent = AgentLobby.Instance();
            if (agent == null || agent->IsLoggedIn)
            {
                return false;
            }

            var list = CharaSelectCharacterList.Instance();
            var objectManager = ClientObjectManager.Instance();
            var mappingAvailable = list != null && objectManager != null;
            var current = CharaSelectCharacterList.GetCurrentCharacter();
            var currentAvailable = current != null;

            var selectedRawIndex = (int)agent->SelectedCharacterIndex;
            var normalizedIndex = CharaSelectSelectedCharacterIdentityLogic.NormalizeSelectedIndex(selectedRawIndex);
            var entryAvailable = TryGetEntryContentId(agent, normalizedIndex, out var entryContentId);

            var hoveredRawIndex = (int)agent->HoveredCharacterIndex;
            var hoveredNormalizedIndex = CharaSelectSelectedCharacterIdentityLogic.NormalizeSelectedIndex(hoveredRawIndex);
            var hoveredEntryAvailable = TryGetEntryContentId(agent, hoveredNormalizedIndex, out var hoveredEntryContentId);

            var selectedContentId = agent->SelectedCharacterContentId;
            var selectedContentAvailable = selectedContentId != 0;
            var hoveredContentId = agent->HoveredCharacterContentId != 0
                ? agent->HoveredCharacterContentId
                : hoveredEntryContentId;
            var hoveredContentAvailable = hoveredContentId != 0 || hoveredEntryAvailable;

            // Pinned TitleEdit の CurrentCharacter semantics と API15 typed API を組み合わせる。
            // selected index/content が 0xFF/0 でも、current pointer と mapping object が一致すれば有効。
            if (mappingAvailable
                && currentAvailable
                && TryResolveCurrentCharacterMapping(
                    list,
                    objectManager,
                    current,
                    out var currentContentId,
                    out var currentClientObjectIndex,
                    out var currentObjectIndex,
                    out var currentEntityId))
            {
                context = BuildContext(
                    (nint)current,
                    currentContentId,
                    normalizedIndex >= 0 ? normalizedIndex : hoveredNormalizedIndex,
                    currentClientObjectIndex,
                    currentObjectIndex,
                    currentEntityId,
                    CharaSelectIdentityResolveSource.CurrentCharacterMapping,
                    currentAvailable,
                    entryAvailable,
                    selectedContentAvailable,
                    mappingAvailable,
                    identityConsistent: true);
                return context.Valid;
            }

            var order = CharaSelectSelectedCharacterIdentityLogic.BuildResolveOrder(
                hasCurrentCharacterMapping: false,
                hasSelectedIndexContentId: entryAvailable,
                hasSelectedContentId: selectedContentAvailable,
                hasHoveredContentId: hoveredContentAvailable);

            var mappingHitAny = false;
            var clientObjectIndexValidAny = false;
            var objectResolvedAny = false;
            var identityMismatchObserved = false;

            if (mappingAvailable)
            {
                foreach (var source in order)
                {
                    var contentId = source switch
                    {
                        CharaSelectIdentityResolveSource.SelectedCharacterIndex => entryContentId,
                        CharaSelectIdentityResolveSource.SelectedContentId => selectedContentId,
                        CharaSelectIdentityResolveSource.HoveredCharacter => hoveredContentId,
                        _ => 0UL,
                    };
                    if (contentId == 0)
                    {
                        continue;
                    }

                    if (TryResolveActorByContentId(
                            list,
                            objectManager,
                            contentId,
                            out var actor,
                            out var mappingHit,
                            out var indexValid,
                            out var objectResolved,
                            out var clientObjectIndex,
                            out var objectIndex,
                            out var entityId))
                    {
                        // GetCurrentCharacter が存在する frame では、content-id source が別 actor を指す
                        // 状態を current actor として採用しない。
                        var identityConsistent = !currentAvailable || actor == (nint)current;
                        if (identityConsistent)
                        {
                            context = BuildContext(
                                actor,
                                contentId,
                                normalizedIndex >= 0 ? normalizedIndex : hoveredNormalizedIndex,
                                clientObjectIndex,
                                objectIndex,
                                entityId,
                                source,
                                currentAvailable,
                                entryAvailable,
                                selectedContentAvailable,
                                mappingAvailable,
                                identityConsistent: true);
                            return context.Valid;
                        }

                        identityMismatchObserved = true;
                    }

                    mappingHitAny |= mappingHit;
                    clientObjectIndexValidAny |= indexValid;
                    objectResolvedAny |= objectResolved;
                }
            }

            context = CharaSelectResolvedActorContext.Unresolved(
                normalizedIndex,
                currentAvailable,
                entryAvailable,
                selectedContentAvailable,
                mappingAvailable,
                mappingHitAny,
                clientObjectIndexValidAny,
                objectResolvedAny,
                identityConsistent: !identityMismatchObserved && !currentAvailable);
            return false;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to resolve current CharaSelect actor.");
            context = CharaSelectResolvedActorContext.Unresolved();
            return false;
        }
    }

    private static bool TryGetEntryContentId(AgentLobby* agent, int normalizedIndex, out ulong contentId)
    {
        contentId = 0;
        if (agent == null || normalizedIndex < 0)
        {
            return false;
        }

        var entry = agent->LobbyData.GetCharacterEntryByIndex(0, agent->WorldIndex, normalizedIndex);
        if (entry == null || entry->ContentId == 0)
        {
            return false;
        }

        contentId = entry->ContentId;
        return true;
    }

    private static bool TryResolveCurrentCharacterMapping(
        CharaSelectCharacterList* list,
        ClientObjectManager* objectManager,
        Character* current,
        out ulong contentId,
        out short clientObjectIndex,
        out ushort objectIndex,
        out uint entityId)
    {
        contentId = 0;
        clientObjectIndex = -1;
        objectIndex = 0;
        entityId = 0;
        if (list == null || objectManager == null || current == null)
        {
            return false;
        }

        var mappings = list->CharacterMapping;
        for (var i = 0; i < mappings.Length; i++)
        {
            var mapping = mappings[i];
            if (mapping.ContentId == 0 || mapping.ClientObjectIndex < 0)
            {
                continue;
            }

            var mapped = objectManager->GetObjectByIndex((ushort)mapping.ClientObjectIndex);
            if (mapped == null || mapped != current)
            {
                continue;
            }

            contentId = mapping.ContentId;
            clientObjectIndex = mapping.ClientObjectIndex;
            objectIndex = mapped->ObjectIndex;
            entityId = mapped->EntityId;
            return true;
        }

        return false;
    }

    private static bool TryResolveActorByContentId(
        CharaSelectCharacterList* list,
        ClientObjectManager* objectManager,
        ulong contentId,
        out nint actor,
        out bool mappingHit,
        out bool clientObjectIndexValid,
        out bool objectResolved,
        out short clientObjectIndex,
        out ushort objectIndex,
        out uint entityId)
    {
        actor = nint.Zero;
        mappingHit = false;
        clientObjectIndexValid = false;
        objectResolved = false;
        clientObjectIndex = -1;
        objectIndex = 0;
        entityId = 0;

        var mappings = list->CharacterMapping;
        for (var i = 0; i < mappings.Length; i++)
        {
            if (mappings[i].ContentId != contentId)
            {
                continue;
            }

            mappingHit = true;
            clientObjectIndex = mappings[i].ClientObjectIndex;
            if (clientObjectIndex < 0)
            {
                return false;
            }

            clientObjectIndexValid = true;
            var obj = objectManager->GetObjectByIndex((ushort)clientObjectIndex);
            if (obj == null)
            {
                return false;
            }

            objectResolved = true;
            actor = (nint)obj;
            objectIndex = obj->ObjectIndex;
            entityId = obj->EntityId;
            return true;
        }

        return false;
    }

    private static CharaSelectResolvedActorContext BuildContext(
        nint actor,
        ulong contentId,
        int normalizedIndex,
        short clientObjectIndex,
        ushort objectIndex,
        uint entityId,
        CharaSelectIdentityResolveSource source,
        bool currentCharacterAvailable,
        bool entryAvailable,
        bool selectedContentAvailable,
        bool mappingAvailable,
        bool identityConsistent)
        => new(
            actor,
            new CharaSelectActorIdentityKey(contentId, clientObjectIndex, objectIndex, entityId),
            normalizedIndex,
            source,
            currentCharacterAvailable,
            entryAvailable,
            selectedContentAvailable,
            mappingAvailable,
            MappingHit: true,
            ClientObjectIndexValid: true,
            ObjectResolved: true,
            IdentityConsistent: identityConsistent,
            DrawReady: TryReadDrawReady(actor));

    // detour / poll から呼ぶ。pointer change は detour 内の pre/post 比較だけを受け取り、保存しない。
    private void NotifySelectedCharacterObserved(sbyte index, bool actorRecreated = false)
    {
        _ = index;
        if (_clientState.IsLoggedIn || SelectedCharacterChanged == null)
        {
            return;
        }

        try
        {
            if (!TryResolveCurrentCharaSelectActor(out var context) || !context.Valid)
            {
                return;
            }

            if (!actorRecreated && context.IdentityKey == _lastNotifiedSelectedActorKey)
            {
                return;
            }

            _lastNotifiedSelectedActorKey = context.IdentityKey;
            SelectedCharacterChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to raise SelectedCharacterChanged.");
        }
    }

    private void ClearSelectedCharacterIdentityState()
    {
        _lastNotifiedSelectedActorKey = default;
    }

    private static bool TryReadDrawReady(nint actor)
    {
        if (actor == nint.Zero)
        {
            return false;
        }

        try
        {
            var character = (Character*)actor;
            return character->DrawObject != null && character->IsReadyToDraw();
        }
        catch
        {
            return false;
        }
    }
}
