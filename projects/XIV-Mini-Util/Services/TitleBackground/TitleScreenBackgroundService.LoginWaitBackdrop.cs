using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    private readonly TitleBackgroundLoginWaitBackdropState _loginWaitBackdrop = new();
    private IAddonLifecycle? _backdropAddonLifecycle;

    private void InitializeLoginWaitBackdrop(IAddonLifecycle addonLifecycle)
    {
        _backdropAddonLifecycle = addonLifecycle;
        addonLifecycle.RegisterListener(AddonEvent.PreDraw, "Filter", OnLoginWaitBackdropDraw);
        addonLifecycle.RegisterListener(AddonEvent.PreHide, OnLoginWaitDialogClosed);
        addonLifecycle.RegisterListener(AddonEvent.PreClose, OnLoginWaitDialogClosed);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, OnLoginWaitDialogClosed);
        _charaSelectService?.SetLoginWaitDialogObserver(OnLoginWaitDialogOpened);
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
        if (_loginWaitBackdrop.DialogAddonId == 0)
            return;
        if (!CanRemoveLoginWaitBackdrop())
        {
            _loginWaitBackdrop.Reset();
            return;
        }

        var agent = AgentLobby.Instance();
        if (agent == null || agent->IsLoggedIn
            || agent->DialogAddonId != _loginWaitBackdrop.DialogAddonId)
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
        var dialog = manager->GetAddonById((ushort)_loginWaitBackdrop.DialogAddonId);
        var suppress = _loginWaitBackdrop.CanSuppress(agent->DialogAddonId,
            dialog != null && dialog->IsVisible, stage->Filter.NumActiveFilters,
            stage->Filter.NumActiveSystemFilters, filter->RequestingAddonIds);
        _coldStartDiagnostic.RecordLoginWaitBackdrop(suppress);
        if (!suppress)
            return;

        // Skip only the backdrop's draw. Keep modal ownership, collisions and the dialog intact.
        args.PreventOriginal();
    }
}
