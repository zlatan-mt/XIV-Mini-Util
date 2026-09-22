namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundLoginDialogKind { None, Confirmation, Queue }

internal sealed class TitleBackgroundLoginWaitBackdropState
{
    public uint DialogAddonId { get; private set; }
    public TitleBackgroundLoginDialogKind Kind { get; private set; }
    public bool SuppressionObserved { get; private set; }
    public List<TitleBackgroundLoginConfirmationPrompt> ConfirmationPrompts { get; } = [];

    public void ObserveDialog(uint id, TitleBackgroundLoginDialogKind kind = TitleBackgroundLoginDialogKind.Queue)
    {
        DialogAddonId = id is > 0 and <= ushort.MaxValue && kind != TitleBackgroundLoginDialogKind.None ? id : 0;
        Kind = DialogAddonId == 0 ? TitleBackgroundLoginDialogKind.None : kind;
        SuppressionObserved = false;
    }

    public bool TryObserveConfirmation(uint id, string addonName, string prompt)
    {
        if (addonName != "SelectYesno" || !ConfirmationPrompts.Any(template => template.Matches(prompt)))
            return false;
        ObserveDialog(id, TitleBackgroundLoginDialogKind.Confirmation);
        return DialogAddonId != 0;
    }

    public void MarkSuppressed() => SuppressionObserved = true;

    public void ForgetDialog(uint id)
    {
        if (DialogAddonId == id)
            Reset();
    }

    public void Reset() => ObserveDialog(0);

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
