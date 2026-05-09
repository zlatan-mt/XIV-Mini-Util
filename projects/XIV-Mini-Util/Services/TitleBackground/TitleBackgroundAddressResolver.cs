// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAddressResolver.cs
// Description: ロビー背景差し替えに必要なnative addressを解決する
// Reason: signature drift時に背景差し替えだけをfail-closedで止めるため
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace XivMiniUtil.Services.TitleBackground;

internal sealed unsafe class TitleBackgroundAddressResolver
{
    private const int E8SearchBytesBeforeMatch = 16;
    private const int E8SearchBytesAfterMatch = 64;
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

        if (!TryFindE8Callsite(sigScanner, match, out var callsite))
        {
            RecordFailure(name, "TryScanText+NearbyE8Rel32", "invalid-callsite", $"{name} match does not contain a nearby E8 callsite.", required);
            return false;
        }

        var rel32 = *(int*)(callsite + 1);
        _ = TryResolveE8CallTarget(*(byte*)callsite, callsite, rel32, out target);
        var targetWithinText = IsWithinText(sigScanner, target);
        var status = targetWithinText ? "resolved" : "target-outside-text";
        var message = targetWithinText ? string.Empty : $"{name} E8 target is outside module text.";
        _scanResults.Add(new TitleBackgroundSignatureScanResult(name, "TryScanText+NearbyE8Rel32", status, callsite, target, true, IsWithinText(sigScanner, callsite), targetWithinText, message));
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

    internal static bool TryFindNearbyE8Callsite(ReadOnlySpan<byte> bytes, int matchOffset, out int callsiteOffset)
    {
        callsiteOffset = -1;
        if (bytes.Length < 5 || matchOffset < 0 || matchOffset >= bytes.Length)
        {
            return false;
        }

        if (IsE8Callsite(bytes, matchOffset))
        {
            callsiteOffset = matchOffset;
            return true;
        }

        var maxDistance = Math.Max(E8SearchBytesBeforeMatch, E8SearchBytesAfterMatch);
        for (var distance = 1; distance <= maxDistance; distance++)
        {
            var forward = matchOffset + distance;
            if (distance <= E8SearchBytesAfterMatch && IsE8Callsite(bytes, forward))
            {
                callsiteOffset = forward;
                return true;
            }

            var backward = matchOffset - distance;
            if (distance <= E8SearchBytesBeforeMatch && IsE8Callsite(bytes, backward))
            {
                callsiteOffset = backward;
                return true;
            }
        }

        return false;
    }

    private static bool IsE8Callsite(ReadOnlySpan<byte> bytes, int offset)
    {
        return offset >= 0
            && offset <= bytes.Length - 5
            && bytes[offset] == 0xE8;
    }

    private bool TryFindE8Callsite(ISigScanner sigScanner, nint match, out nint callsite)
    {
        callsite = nint.Zero;
        if (!IsWithinText(sigScanner, match))
        {
            return false;
        }

        if (IsSafeE8Read(sigScanner, match) && *(byte*)match == 0xE8)
        {
            callsite = match;
            return true;
        }

        var start = match - E8SearchBytesBeforeMatch;
        var end = match + E8SearchBytesAfterMatch;
        for (var address = match + 1; address <= end; address++)
        {
            if (IsSafeE8Read(sigScanner, address) && *(byte*)address == 0xE8)
            {
                callsite = address;
                return true;
            }
        }

        for (var address = match - 1; address >= start; address--)
        {
            if (IsSafeE8Read(sigScanner, address) && *(byte*)address == 0xE8)
            {
                callsite = address;
                return true;
            }
        }

        return false;
    }

    private static bool IsSafeE8Read(ISigScanner sigScanner, nint address)
    {
        var value = address.ToInt64();
        var start = sigScanner.TextSectionBase.ToInt64();
        var end = start + sigScanner.TextSectionSize;
        return value >= start && value + 5 <= end;
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
