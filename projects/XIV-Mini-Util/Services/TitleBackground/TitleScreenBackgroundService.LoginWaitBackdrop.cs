using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    private readonly TitleBackgroundLoginWaitBackdropState _loginWaitBackdrop = new();
    private IAddonLifecycle? _backdropAddonLifecycle;

    private void InitializeLoginWaitBackdrop(IAddonLifecycle addonLifecycle)
    {
        // Game-owned localized login confirmations: normal, world visit, DC visit, appearance edit.
        foreach (var id in new uint[] { 25, 95, 96, 629 })
        {
            var row = _dataManager.GetExcelSheet<Lobby>().GetRowOrDefault(id);
            if (row is { } entry && TitleBackgroundLoginConfirmationPrompt.Create(entry.Text) is { } prompt)
                _loginWaitBackdrop.ConfirmationPrompts.Add(prompt);
        }
        _backdropAddonLifecycle = addonLifecycle;
        addonLifecycle.RegisterListener(AddonEvent.PreDraw, "Filter", OnLoginWaitBackdropDraw);
        addonLifecycle.RegisterListener(AddonEvent.PreHide, OnLoginWaitDialogClosed);
        addonLifecycle.RegisterListener(AddonEvent.PreClose, OnLoginWaitDialogClosed);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, OnLoginWaitDialogClosed);
        _charaSelectService?.SetLoginWaitDialogObserver(OnLoginWaitDialogOpened);
        _log.Information("[XMU BG] Login backdrop handler loaded. revision=confirm-and-queue-v2, confirmationTemplates={Count}",
            _loginWaitBackdrop.ConfirmationPrompts.Count);
    }

    private void DisposeLoginWaitBackdrop()
    {
        _charaSelectService?.SetLoginWaitDialogObserver(null);
        _backdropAddonLifecycle?.UnregisterListener(OnLoginWaitBackdropDraw, OnLoginWaitDialogClosed);
        _loginWaitBackdrop.Reset();
    }

    private bool CanRemoveLoginWaitBackdrop()
        => !_hookLifecycle.Disposed && !_clientState.IsLoggedIn
            && IsOverrideMutationBranchArmed() && _activeSceneOverride
            && _charaSelectTitleBackgroundSessionActive
            && !IsSavedViewSuppressedByAutomaticRun() && !_automaticCheck.PlacementProofArmed
            && TryReadCurrentLobbyMap(out var map) && map == GameLobbyType.CharaSelect;

    private void OnLoginWaitDialogOpened(uint addonId)
    {
        _loginWaitBackdrop.ObserveDialog(CanRemoveLoginWaitBackdrop() ? addonId : 0);
        if (_loginWaitBackdrop.DialogAddonId != 0)
            _coldStartDiagnostic.RecordLoginWaitBackdrop(suppressed: false);
    }

    private void OnLoginWaitDialogClosed(AddonEvent _, AddonArgs args)
    {
        if (_hookLifecycle.Disposed || _clientState.IsLoggedIn)
        {
            _loginWaitBackdrop.Reset();
            return;
        }
        if (_loginWaitBackdrop.DialogAddonId == 0 || args.Addon.IsNull)
            return;
        var addon = (AtkUnitBase*)args.Addon.Address;
        _loginWaitBackdrop.ForgetDialog(addon->Id);
    }

    private void OnLoginWaitBackdropDraw(AddonEvent _, AddonArgs args)
    {
        if (!CanRemoveLoginWaitBackdrop())
        {
            _loginWaitBackdrop.Reset();
            return;
        }

        var agent = AgentLobby.Instance();
        if (agent == null || agent->IsLoggedIn || agent->DialogAddonId is 0 or > ushort.MaxValue)
        {
            _loginWaitBackdrop.Reset();
            return;
        }

        var stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null || args.Addon.IsNull)
            return;
        var manager = stage->RaptureAtkUnitManager;
        var filter = (AddonFilter*)args.Addon.Address;
        if (filter != manager->AddonFilter)
            return;
        var dialog = manager->GetAddonById((ushort)agent->DialogAddonId);
        if (agent->DialogAddonId != _loginWaitBackdrop.DialogAddonId)
        {
            _loginWaitBackdrop.Reset();
            if (!TryObserveLoginConfirmation(dialog))
                return;
        }
        var suppress = _loginWaitBackdrop.CanSuppress(agent->DialogAddonId,
            dialog != null && dialog->IsVisible, stage->Filter.NumActiveFilters,
            stage->Filter.NumActiveSystemFilters, filter->RequestingAddonIds);
        _coldStartDiagnostic.RecordLoginWaitBackdrop(suppress, _loginWaitBackdrop.Kind);
        if (!suppress)
            return;

        // Skip only the backdrop's draw. Keep modal ownership, collisions and the dialog intact.
        args.PreventOriginal();
        if (!_loginWaitBackdrop.SuppressionObserved)
            _log.Information("[XMU BG] Login backdrop drawing suppressed. kind={Kind}; modal input preserved",
                _loginWaitBackdrop.Kind);
        _loginWaitBackdrop.MarkSuppressed();
    }

    private bool TryObserveLoginConfirmation(AtkUnitBase* dialog)
    {
        if (dialog == null || !dialog->IsVisible || dialog->NameString != "SelectYesno")
            return false;
        var textNode = ((AddonSelectYesno*)dialog)->PromptText;
        if (textNode == null)
            return false;
        var text = textNode->NodeText.AsSpan();
        return text.Length <= 4096 && _loginWaitBackdrop.TryObserveConfirmation(
            dialog->Id, dialog->NameString, new ReadOnlySeString(text.ToArray()).ToString());
    }
}
