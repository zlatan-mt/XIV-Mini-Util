// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectEmotePresetStore.cs
// Description: キャラ選択エモートpresetの純粋な更新ロジックを提供する
// Reason: UI/unsafe hookから切り離してゲーム不要テストで退行を検出するため
namespace XivMiniUtil.Services.CharaSelect;

internal static class CharaSelectEmotePresetStore
{
    public static uint? GetActiveEmoteId(
        Dictionary<ulong, List<uint>> presets,
        Dictionary<ulong, int> activeIndexes,
        ulong contentId,
        Dictionary<ulong, uint>? legacySelectedEmotes = null)
    {
        if (contentId == 0)
        {
            return null;
        }

        if (presets.TryGetValue(contentId, out var emotes) && emotes.Count > 0)
        {
            var index = GetActiveIndex(presets, activeIndexes, contentId);
            return emotes[index];
        }

        if (legacySelectedEmotes != null
            && legacySelectedEmotes.TryGetValue(contentId, out var legacyEmoteId)
            && legacyEmoteId != 0)
        {
            return legacyEmoteId;
        }

        return null;
    }

    public static int GetActiveIndex(
        Dictionary<ulong, List<uint>> presets,
        Dictionary<ulong, int> activeIndexes,
        ulong contentId)
    {
        if (!presets.TryGetValue(contentId, out var emotes) || emotes.Count == 0)
        {
            return 0;
        }

        return activeIndexes.TryGetValue(contentId, out var index)
            ? Math.Clamp(index, 0, emotes.Count - 1)
            : 0;
    }

    public static IReadOnlyList<uint> GetEmotes(Dictionary<ulong, List<uint>> presets, ulong contentId)
    {
        return presets.TryGetValue(contentId, out var emotes) ? emotes : [];
    }

    public static bool SelectPrevious(
        Dictionary<ulong, List<uint>> presets,
        Dictionary<ulong, int> activeIndexes,
        ulong contentId)
    {
        if (!presets.TryGetValue(contentId, out var emotes) || emotes.Count == 0)
        {
            return false;
        }

        var current = GetActiveIndex(presets, activeIndexes, contentId);
        activeIndexes[contentId] = (current - 1 + emotes.Count) % emotes.Count;
        return activeIndexes[contentId] != current;
    }

    public static bool SelectNext(
        Dictionary<ulong, List<uint>> presets,
        Dictionary<ulong, int> activeIndexes,
        ulong contentId)
    {
        if (!presets.TryGetValue(contentId, out var emotes) || emotes.Count == 0)
        {
            return false;
        }

        var current = GetActiveIndex(presets, activeIndexes, contentId);
        activeIndexes[contentId] = (current + 1) % emotes.Count;
        return activeIndexes[contentId] != current;
    }

    public static bool SaveToActiveSlot(
        Dictionary<ulong, List<uint>> presets,
        Dictionary<ulong, int> activeIndexes,
        ulong contentId,
        uint emoteId)
    {
        if (contentId == 0 || emoteId == 0)
        {
            return false;
        }

        if (!presets.TryGetValue(contentId, out var emotes) || emotes.Count == 0)
        {
            presets[contentId] = [emoteId];
            activeIndexes[contentId] = 0;
            return true;
        }

        var activeIndex = GetActiveIndex(presets, activeIndexes, contentId);
        var changed = emotes[activeIndex] != emoteId;
        emotes[activeIndex] = emoteId;
        RemoveDuplicateEmotes(emotes, activeIndexes, contentId, activeIndex);
        return changed;
    }

    public static bool Append(
        Dictionary<ulong, List<uint>> presets,
        Dictionary<ulong, int> activeIndexes,
        ulong contentId,
        uint emoteId)
    {
        if (contentId == 0 || emoteId == 0)
        {
            return false;
        }

        if (!presets.TryGetValue(contentId, out var emotes))
        {
            emotes = [];
            presets[contentId] = emotes;
        }

        var existingIndex = emotes.IndexOf(emoteId);
        if (existingIndex >= 0)
        {
            activeIndexes[contentId] = existingIndex;
            return false;
        }

        emotes.Add(emoteId);
        activeIndexes[contentId] = emotes.Count - 1;
        return true;
    }

    public static bool RemoveActive(
        Dictionary<ulong, List<uint>> presets,
        Dictionary<ulong, int> activeIndexes,
        ulong contentId)
    {
        if (!presets.TryGetValue(contentId, out var emotes) || emotes.Count == 0)
        {
            return false;
        }

        var activeIndex = GetActiveIndex(presets, activeIndexes, contentId);
        emotes.RemoveAt(activeIndex);
        if (emotes.Count == 0)
        {
            presets.Remove(contentId);
            activeIndexes.Remove(contentId);
            return true;
        }

        activeIndexes[contentId] = Math.Min(activeIndex, emotes.Count - 1);
        return true;
    }

    public static void Normalize(
        Dictionary<ulong, List<uint>> presets,
        Dictionary<ulong, int> activeIndexes)
    {
        foreach (var key in presets.Keys.ToList())
        {
            var emotes = presets[key]
                .Where(emoteId => emoteId != 0)
                .Distinct()
                .ToList();

            if (emotes.Count == 0)
            {
                presets.Remove(key);
                activeIndexes.Remove(key);
                continue;
            }

            presets[key] = emotes;
            activeIndexes[key] = GetActiveIndex(presets, activeIndexes, key);
        }
    }

    private static void RemoveDuplicateEmotes(List<uint> emotes, Dictionary<ulong, int> activeIndexes, ulong contentId, int activeIndex)
    {
        var activeEmote = emotes[activeIndex];
        for (var index = emotes.Count - 1; index >= 0; index--)
        {
            if (index != activeIndex && emotes[index] == activeEmote)
            {
                emotes.RemoveAt(index);
                if (index < activeIndex)
                {
                    activeIndex--;
                }
            }
        }

        activeIndexes[contentId] = Math.Clamp(activeIndex, 0, emotes.Count - 1);
    }
}
