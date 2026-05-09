// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAddressResolver.cs
// Description: ロビー背景差し替えに必要なnative addressを解決する
// Reason: signature drift時に背景差し替えだけをfail-closedで止めるため
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace XivMiniUtil.Services.TitleBackground;

internal sealed unsafe class TitleBackgroundAddressResolver
{
    private readonly List<TitleBackgroundSignatureScanResult> _scanResults = new();

    public nint CreateScene { get; private set; }
    public nint CreateSceneMatch { get; private set; }
    public nint FixOn { get; private set; }
    public nint LobbyUpdate { get; private set; }
    public nint LobbyUpdateMatch { get; private set; }
    public nint LoadLobbyScene { get; private set; }
    public nint LobbyCurrentMap { get; private set; }
    public nint UpdateLobbyUIStage { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public IReadOnlyList<TitleBackgroundSignatureScanResult> ScanResults => _scanResults;

    public bool Resolve(ISigScanner sigScanner, Configuration configuration)
    {
        CreateScene = nint.Zero;
        CreateSceneMatch = nint.Zero;
        FixOn = nint.Zero;
        LobbyUpdate = nint.Zero;
        LobbyUpdateMatch = nint.Zero;
        LoadLobbyScene = nint.Zero;
        LobbyCurrentMap = nint.Zero;
        UpdateLobbyUIStage = nint.Zero;
        LastError = string.Empty;
        _scanResults.Clear();

        var createSceneResolved = TryResolveE8Call(sigScanner, configuration.TitleBackgroundCreateSceneSignature, nameof(CreateScene), out var createSceneMatch, out var createScene);
        var lobbyUpdateResolved = TryResolveE8Call(sigScanner, configuration.TitleBackgroundLobbyUpdateSignature, nameof(LobbyUpdate), out var lobbyUpdateMatch, out var lobbyUpdate);
        var loadLobbySceneResolved = TryResolveText(sigScanner, configuration.TitleBackgroundLoadLobbySceneSignature, nameof(LoadLobbyScene), out var loadLobbyScene);
        var currentMapResolved = TryResolveStatic(sigScanner, configuration.TitleBackgroundLobbyCurrentMapSignature, nameof(LobbyCurrentMap), out var lobbyCurrentMap);
        var fixOnResolved = TryResolveText(sigScanner, configuration.TitleBackgroundFixOnSignature, "LobbyCameraFixOn", out var fixOn, required: configuration.TitleBackgroundCameraOverrideEnabled);
        _ = TryResolveUpdateLobbyUiStage(sigScanner, out var updateLobbyUiStage);

        CreateSceneMatch = createSceneMatch;
        CreateScene = createScene;
        LobbyUpdateMatch = lobbyUpdateMatch;
        LobbyUpdate = lobbyUpdate;
        LoadLobbyScene = loadLobbyScene;
        LobbyCurrentMap = lobbyCurrentMap;
        FixOn = fixOn;
        UpdateLobbyUIStage = updateLobbyUiStage;
        return createSceneResolved
            && lobbyUpdateResolved
            && loadLobbySceneResolved
            && currentMapResolved
            && (!configuration.TitleBackgroundCameraOverrideEnabled || fixOnResolved);
    }

    private bool TryResolveText(ISigScanner sigScanner, string signature, string name, out nint address, bool required = true)
    {
        address = nint.Zero;
        signature = NormalizeSignature(signature);
        if (string.IsNullOrWhiteSpace(signature))
        {
            RecordFailure(name, "TryScanText", "not-configured", $"{name} signature is not configured.", required);
            return false;
        }

        if (!sigScanner.TryScanText(signature, out address) || address == nint.Zero)
        {
            RecordFailure(name, "TryScanText", "not-found", $"{name} signature was not found.", required);
            return false;
        }

        RecordSuccess(name, "TryScanText", address, address, IsWithinText(sigScanner, address), string.Empty);
        return true;
    }

    private bool TryResolveE8Call(ISigScanner sigScanner, string signature, string name, out nint match, out nint target, bool required = true)
    {
        match = nint.Zero;
        target = nint.Zero;
        signature = NormalizeSignature(signature);
        if (string.IsNullOrWhiteSpace(signature))
        {
            RecordFailure(name, "TryScanText+E8Rel32", "not-configured", $"{name} signature is not configured.", required);
            return false;
        }

        if (!sigScanner.TryScanText(signature, out match) || match == nint.Zero)
        {
            RecordFailure(name, "TryScanText+E8Rel32", "not-found", $"{name} signature was not found.", required);
            return false;
        }

        var rel32 = *(int*)(match + 1);
        if (!TryResolveE8CallTarget(*(byte*)match, match, rel32, out target))
        {
            RecordFailure(name, "TryScanText+E8Rel32", "invalid-callsite", $"{name} match does not start with E8.", required);
            return false;
        }

        var targetWithinText = IsWithinText(sigScanner, target);
        var status = targetWithinText ? "resolved" : "target-outside-text";
        var message = targetWithinText ? string.Empty : $"{name} E8 target is outside module text.";
        _scanResults.Add(new TitleBackgroundSignatureScanResult(name, "TryScanText+E8Rel32", status, match, target, true, IsWithinText(sigScanner, match), targetWithinText, message));
        if (!targetWithinText)
        {
            if (required)
            {
                LastError = string.IsNullOrWhiteSpace(LastError) ? message : LastError;
            }
        }

        return targetWithinText;
    }

    private bool TryResolveStatic(ISigScanner sigScanner, string signature, string name, out nint address)
    {
        address = nint.Zero;
        signature = NormalizeSignature(signature);
        if (string.IsNullOrWhiteSpace(signature))
        {
            RecordFailure(name, "TryGetStaticAddressFromSig", "not-configured", $"{name} signature is not configured.");
            return false;
        }

        if (!sigScanner.TryGetStaticAddressFromSig(signature, out address) || address == nint.Zero)
        {
            RecordFailure(name, "TryGetStaticAddressFromSig", "not-found", $"{name} static address was not found.");
            return false;
        }

        RecordSuccess(name, "TryGetStaticAddressFromSig", address, address, true, string.Empty);
        return true;
    }

    private bool TryResolveUpdateLobbyUiStage(ISigScanner sigScanner, out nint address)
    {
        address = nint.Zero;
        var signature = AgentLobby.Addresses.UpdateLobbyUIStage.String;
        if (string.IsNullOrWhiteSpace(signature))
        {
            RecordFailure("UpdateLobbyUIStage", "FFXIVClientStructs", "not-configured", "AgentLobby.UpdateLobbyUIStage signature is unavailable.", required: false);
            return false;
        }

        if (!TryResolveE8Call(sigScanner, signature, "UpdateLobbyUIStage", out _, out address, required: false))
        {
            return false;
        }

        return true;
    }

    private void RecordSuccess(string name, string method, nint address, nint target, bool targetWithinText, string message)
    {
        _scanResults.Add(new TitleBackgroundSignatureScanResult(name, method, "resolved", address, target, false, true, targetWithinText, message));
    }

    private void RecordFailure(string name, string method, string status, string message, bool required = true)
    {
        if (required)
        {
            LastError = string.IsNullOrWhiteSpace(LastError) ? message : LastError;
        }

        _scanResults.Add(new TitleBackgroundSignatureScanResult(name, method, status, nint.Zero, nint.Zero, false, false, false, message));
    }

    internal static bool TryResolveE8CallTarget(byte firstByte, nint match, int rel32, out nint target)
    {
        target = nint.Zero;
        if (firstByte != 0xE8)
        {
            return false;
        }

        target = match + 5 + rel32;
        return true;
    }

    private static bool IsWithinText(ISigScanner sigScanner, nint address)
    {
        var value = address.ToInt64();
        var start = sigScanner.TextSectionBase.ToInt64();
        var end = start + sigScanner.TextSectionSize;
        return value >= start && value < end;
    }

    private static string NormalizeSignature(string? signature)
    {
        return (signature ?? string.Empty).Trim();
    }
}

internal sealed record TitleBackgroundSignatureScanResult(
    string Name,
    string Method,
    string Status,
    nint Address,
    nint ResolvedTarget,
    bool IsE8Callsite,
    bool AddressWithinText,
    bool TargetWithinText,
    string Message);
