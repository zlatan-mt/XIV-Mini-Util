// Path: projects/XIV-Mini-Util/Services/Common/AddonStateTracker.cs
// Description: AddonLifecycleからアドオンのロード/可視状態を追跡する
// Reason: 精選の調査ログでUI状態を確定させるため
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;

namespace XivMiniUtil.Services.Common;

public sealed class AddonStateTracker : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IPluginLog _pluginLog;
    private readonly Dictionary<string, AddonState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AddonHandlers> _handlers = new(StringComparer.Ordinal);
    private readonly TimeSpan _visibleGrace = TimeSpan.FromSeconds(1);

    public AddonStateTracker(IAddonLifecycle addonLifecycle, IPluginLog pluginLog)
    {
        _addonLifecycle = addonLifecycle;
        _pluginLog = pluginLog;
    }

    public void Register(string addonName)
    {
        if (_states.ContainsKey(addonName))
        {
            return;
        }

        _states[addonName] = new AddonState();

        var handlers = new AddonHandlers(
            (evt, args) => OnAddonEvent(addonName, evt),
            (evt, args) => OnAddonEvent(addonName, evt),
            (evt, args) => OnAddonEvent(addonName, evt));
        _handlers[addonName] = handlers;

        var addonNames = new[] { addonName };
        _addonLifecycle.RegisterListener(AddonEvent.PreUpdate, addonNames, handlers.PreUpdate);
        _addonLifecycle.RegisterListener(AddonEvent.PreDraw, addonNames, handlers.PreDraw);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, addonNames, handlers.PreFinalize);
    }

    public AddonStateSnapshot GetSnapshot(string addonName, DateTime now)
    {
        if (!_states.TryGetValue(addonName, out var state))
        {
            return new AddonStateSnapshot(false, false, null, null);
        }

        var loaded = state.IsLoaded;
        var visible = loaded && state.LastPreDrawAt.HasValue && now - state.LastPreDrawAt.Value <= _visibleGrace;
        return new AddonStateSnapshot(loaded, visible, state.LastPreUpdateAt, state.LastPreDrawAt);
    }

    public void Dispose()
    {
        foreach (var addonName in _states.Keys)
        {
            if (_handlers.TryGetValue(addonName, out var handlers))
            {
                var addonNames = new[] { addonName };
                _addonLifecycle.UnregisterListener(AddonEvent.PreUpdate, addonNames, handlers.PreUpdate);
                _addonLifecycle.UnregisterListener(AddonEvent.PreDraw, addonNames, handlers.PreDraw);
                _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, addonNames, handlers.PreFinalize);
            }
        }

        _states.Clear();
        _handlers.Clear();
    }

    private void OnAddonEvent(string addonName, AddonEvent addonEvent)
    {
        if (!_states.TryGetValue(addonName, out var state))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var changed = false;

        switch (addonEvent)
        {
            case AddonEvent.PreUpdate:
                if (!state.IsLoaded)
                {
                    state.IsLoaded = true;
                    changed = true;
                }

                state.LastPreUpdateAt = now;
                break;
            case AddonEvent.PreDraw:
                state.LastPreDrawAt = now;
                break;
            case AddonEvent.PreFinalize:
                if (state.IsLoaded)
                {
                    state.IsLoaded = false;
                    state.LastPreUpdateAt = null;
                    state.LastPreDrawAt = null;
                    changed = true;
                }

                break;
        }

        if (changed)
        {
            _pluginLog.Debug($"[AddonState] {addonName} loaded={state.IsLoaded}");
        }
    }

    private sealed class AddonState
    {
        public bool IsLoaded { get; set; }
        public DateTime? LastPreUpdateAt { get; set; }
        public DateTime? LastPreDrawAt { get; set; }
    }

    private sealed record AddonHandlers(
        IAddonLifecycle.AddonEventDelegate PreUpdate,
        IAddonLifecycle.AddonEventDelegate PreDraw,
        IAddonLifecycle.AddonEventDelegate PreFinalize);

    public sealed record AddonStateSnapshot(
        bool Loaded,
        bool Visible,
        DateTime? LastPreUpdateAt,
        DateTime? LastPreDrawAt);
}
