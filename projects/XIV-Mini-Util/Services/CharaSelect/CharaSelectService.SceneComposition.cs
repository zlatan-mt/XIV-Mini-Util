// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectService.SceneComposition.cs
// Description: CharaSelect の scene composition / TitleBackground bridge 操作を提供する
// Reason: CharaSelectService 本体から planner 連携を分離するため

namespace XivMiniUtil.Services.CharaSelect;

public sealed unsafe partial class CharaSelectService
{
    public IReadOnlyList<CharaSelectSceneProfile> SceneProfiles => CharaSelectSceneProfileRegistry.All;

    public CharaSelectSceneProfile CurrentSceneProfile => CharaSelectSceneCompositionPlanner.ResolveProfile(_configuration);

    internal TitleBackgroundCharacterCompositionBridgeSnapshot GetTitleBackgroundCharacterCompositionBridgeSnapshot()
    {
        var enabled = CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeEnabled(_configuration);
        var required = CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeRequired(_configuration);
        if (!enabled)
        {
            return TitleBackgroundCharacterCompositionBridgeSnapshot.Empty with
            {
                Reason = _configuration.CharaSelectSceneCompositionEnabled
                    ? "legacy shooting composition enabled"
                    : "disabled",
            };
        }

        return _lastTitleBackgroundBridgeSnapshot with
        {
            Enabled = true,
            Required = required,
            Reason = string.IsNullOrWhiteSpace(_lastTitleBackgroundBridgeSnapshot.Reason)
                ? "not-run"
                : _lastTitleBackgroundBridgeSnapshot.Reason,
            Source = string.IsNullOrWhiteSpace(_lastTitleBackgroundBridgeSnapshot.Source)
                ? "none"
                : _lastTitleBackgroundBridgeSnapshot.Source,
            CharacterVisualExpected = required,
        };
    }

    internal CharaSelectCompositionRouteRuntimeSnapshot GetLegacyCompositionRouteSnapshot()
    {
        return _lastLegacyCompositionRouteSnapshot with
        {
            LegacyEnabled = _configuration.CharaSelectSceneCompositionEnabled,
            BridgeEnabled = false,
        };
    }

    internal CharaSelectCompositionRouteRuntimeSnapshot GetTitleBackgroundBridgeRouteSnapshot()
    {
        return _lastTitleBackgroundBridgeRouteSnapshot with
        {
            LegacyEnabled = false,
            BridgeEnabled = CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeEnabled(_configuration),
        };
    }

    public string GetCurrentSceneProfileLabel()
    {
        return CharaSelectSceneProfileRegistry.BuildLabel(CurrentSceneProfile);
    }

    public void SetSceneCompositionEnabled(bool enabled)
    {
        CharaSelectSceneCompositionPlanner.SetFinalCompositionEnabled(_configuration, enabled);
        if (enabled)
        {
            ApplyCurrentSceneProfileToConfiguration();
        }
        else
        {
            ClearCurrentSceneProfileRuntimeSettings();
        }

        _configuration.Save();
        ApplySceneCompositionRuntimeState();
    }

    public void SetSceneProfileId(string profileId)
    {
        _configuration.CharaSelectSceneProfileId = CharaSelectSceneProfileRegistry.NormalizeId(profileId);
        ApplyCurrentSceneProfileToConfiguration();
        _configuration.Save();
        ApplySceneCompositionRuntimeState();
    }

    public void SetSceneUseProfileTerritory(bool enabled)
    {
        _configuration.CharaSelectSceneUseProfileTerritory = enabled;
        if (enabled)
        {
            ApplyCurrentSceneProfileToConfiguration();
        }

        _configuration.Save();
        ApplySceneCompositionRuntimeState();
    }

    public void SetSceneUseProfilePosition(bool enabled)
    {
        _configuration.CharaSelectSceneUseProfilePosition = enabled;
        if (enabled)
        {
            ApplyCurrentSceneProfileToConfiguration();
        }

        _configuration.Save();
        ApplySceneCompositionRuntimeState();
    }

    public void SetSceneUseSavedEmote(bool enabled)
    {
        _configuration.CharaSelectSceneUseSavedEmote = enabled;
        if (enabled)
        {
            ApplyCurrentSceneProfileToConfiguration();
        }

        _configuration.Save();
        ApplySceneCompositionRuntimeState();
    }

    public void SetScenePlacementMode(CharaSelectScenePlacementMode mode)
    {
        _configuration.CharaSelectScenePlacementMode = Enum.IsDefined(typeof(CharaSelectScenePlacementMode), mode)
            ? mode
            : CharaSelectScenePlacementMode.ObserveOnly;
        _configuration.Save();
    }

    public void SetSceneStageStrategy(CharaSelectStageStrategy strategy)
    {
        _configuration.CharaSelectSceneStageStrategy = Enum.IsDefined(typeof(CharaSelectStageStrategy), strategy)
            ? strategy
            : CharaSelectStageStrategy.ObserveOnly;
        ApplyCurrentSceneProfileToConfiguration();
        _configuration.Save();
        ApplySceneCompositionRuntimeState();
    }

    public void ApplyCurrentSceneProfile()
    {
        ApplyCurrentSceneProfileToConfiguration();
        _configuration.Save();
        ApplySceneCompositionRuntimeState();
    }

    private void ApplyCurrentSceneProfileToConfiguration()
    {
        CharaSelectSceneCompositionPlanner.ApplyProfileToConfiguration(_configuration, CurrentSceneProfile);
    }

    public void DisableSceneCompositionForTitleBackgroundRoute()
    {
        CharaSelectSceneCompositionPlanner.SetTitleBackgroundRouteEnabled(_configuration, true);
        _configuration.Save();
        ApplySceneCompositionRuntimeState();
    }

    internal void ApplyTitleBackgroundCharacterCompositionBridgeRuntimeState()
    {
        if (!CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeEnabled(_configuration))
        {
            _lastTitleBackgroundBridgeSnapshot = GetTitleBackgroundCharacterCompositionBridgeSnapshot();
            return;
        }

        ApplyLoginWaitHookState();
        ApplyTitleBackgroundBridgePrefetch();
        MarkTitleBackgroundBridge(
            invoked: true,
            reason: "runtime-state-requested",
            appliedStage: false,
            appliedCharacter: _currentEntry != null && _currentEntry.Character != null);
        RefreshCharaSelectDisplay();
    }

    internal void ReapplyCompositionRuntimeStateFromConfiguration()
    {
        ApplySceneCompositionRuntimeState();
        if (CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeEnabled(_configuration))
        {
            ApplyTitleBackgroundCharacterCompositionBridgeRuntimeState();
        }
    }

    internal void ResetTitleBackgroundCharacterCompositionBridgeSnapshot()
    {
        _lastTitleBackgroundBridgeSnapshot = TitleBackgroundCharacterCompositionBridgeSnapshot.Empty;
    }

    internal void MarkTitleBackgroundCharacterCompositionBridgeCameraApplied()
    {
        if (!CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeEnabled(_configuration))
        {
            return;
        }

        var current = GetTitleBackgroundCharacterCompositionBridgeSnapshot();
        _lastTitleBackgroundBridgeSnapshot = current with
        {
            AppliedCamera = true,
            CharacterVisualKnownByBridge = current.AppliedStage && current.AppliedCharacter,
        };
    }

    public void SetSceneCharacterVisibleResult(CharaSelectSceneBinaryResult result)
    {
        _configuration.LastSceneProfileCharacterVisibleResult = result;
        _configuration.Save();
    }

    public void SetSceneLocationChangedResult(CharaSelectSceneBinaryResult result)
    {
        _configuration.LastSceneProfileLocationChangedResult = result;
        _configuration.Save();
    }

    public void SetSceneEmotePlayedResult(CharaSelectSceneBinaryResult result)
    {
        _configuration.LastSceneProfileEmotePlayedResult = result;
        _configuration.Save();
    }

    public void SetSceneBrightnessResult(CharaSelectSceneBrightnessResult result)
    {
        _configuration.LastSceneProfileBrightnessResult = result;
        _configuration.Save();
    }

    private void ClearCurrentSceneProfileRuntimeSettings()
    {
        var profile = CurrentSceneProfile;
        if (_configuration.CharaSelectSceneUseProfileTerritory
            && _configuration.CharaSelectOverrideTerritoryEnabled
            && _configuration.CharaSelectOverrideTerritoryTypeId == profile.TerritoryTypeId)
        {
            _configuration.CharaSelectOverrideTerritoryEnabled = false;
            _configuration.CharaSelectOverrideTerritoryTypeId = 0;
            TryUnloadPrefetchLayout(CharaSelectPrefetchOwner.OverrideDisplay);
        }
    }

    private void ApplySceneCompositionRuntimeState()
    {
        ApplyLoginWaitHookState();
        if (!_configuration.CharaSelectSceneCompositionEnabled)
        {
            RefreshCharaSelectDisplay();
            return;
        }

        if (_clientState.IsLoggedIn)
        {
            return;
        }

        ApplyOverrideTerritoryPrefetch();
        RefreshCharaSelectDisplay();
        if (_configuration.CharaSelectSceneCompositionEnabled
            && _configuration.CharaSelectSceneUseSavedEmote)
        {
            ReplaySelectedEmote();
        }
    }
}
