namespace XivMiniUtil.Services.TitleBackground;

internal sealed class TitleBackgroundLoginWaitBackdropState
{
    public uint DialogAddonId { get; private set; }

    public void ObserveDialog(uint id) => DialogAddonId = id is > 0 and <= ushort.MaxValue ? id : 0;

    public void ForgetDialog(uint id)
    {
        if (DialogAddonId == id)
            DialogAddonId = 0;
    }

    public void Reset() => DialogAddonId = 0;

    public bool CanSuppress(uint currentDialogId, bool dialogVisible, int activeFilters,
        int activeSystemFilters, ReadOnlySpan<uint> requestingAddonIds)
    {
        if (DialogAddonId == 0 || currentDialogId != DialogAddonId || !dialogVisible
            || activeFilters != 1 || activeSystemFilters != 0)
            return false;

        var matches = 0;
        foreach (var id in requestingAddonIds)
        {
            if (id == 0)
                continue;
            if (id != DialogAddonId)
                return false;
            matches++;
        }

        return matches == 1;
    }
}
