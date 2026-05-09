// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAddressResolver.cs
// Description: ロビー背景差し替えに必要なnative addressを解決する
// Reason: signature drift時に背景差し替えだけをfail-closedで止めるため
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Runtime.InteropServices;

namespace XivMiniUtil.Services.TitleBackground;

internal sealed unsafe class TitleBackgroundAddressResolver
{
    private const int E8SearchBytesBeforeMatch = 16;
    private const int E8SearchBytesAfterMatch = 64;
    private const int CandidatePreviewBytes = 16;
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

        var allowDirectTextProbeTargets = configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.HookProbe;
        var createSceneResolved = TryResolveE8Call(
            sigScanner,
            configuration.TitleBackgroundCreateSceneSignature,
            nameof(CreateScene),
            configuration.TitleBackgroundCreateSceneResolverMode,
            allowDirectTextProbeTargets,
            out var createSceneMatch,
            out var createScene);
        var lobbyUpdateResolved = TryResolveE8Call(
            sigScanner,
            configuration.TitleBackgroundLobbyUpdateSignature,
            nameof(LobbyUpdate),
            configuration.TitleBackgroundLobbyUpdateResolverMode,
            allowDirectTextProbeTargets,
            out var lobbyUpdateMatch,
            out var lobbyUpdate);
        var loadLobbySceneResolved = TryResolveText(sigScanner, configuration.TitleBackgroundLoadLobbySceneSignature, nameof(LoadLobbyScene), out var loadLobbyScene);
        var currentMapResolved = TryResolveStatic(sigScanner, configuration.TitleBackgroundLobbyCurrentMapSignature, nameof(LobbyCurrentMap), out var lobbyCurrentMap);
        var cameraHookRequired = TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(
            configuration.TitleBackgroundRuntimeMode,
            configuration.TitleBackgroundOverrideEnabled,
            configuration.TitleBackgroundCameraOverrideEnabled);
        var fixOnResolved = TryResolveText(sigScanner, configuration.TitleBackgroundFixOnSignature, "LobbyCameraFixOn", out var fixOn, required: cameraHookRequired);
        _ = TryResolveUpdateLobbyUiStage(sigScanner, out var updateLobbyUiStage);

        CreateSceneMatch = createSceneMatch;
        CreateScene = createScene;
        LobbyUpdateMatch = lobbyUpdateMatch;
        LobbyUpdate = lobbyUpdate;
        LoadLobbyScene = loadLobbyScene;
        LobbyCurrentMap = lobbyCurrentMap;
        FixOn = fixOn;
        UpdateLobbyUIStage = updateLobbyUiStage;
        if (configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.HookProbe)
        {
            return createSceneResolved
                && lobbyUpdateResolved
                && loadLobbySceneResolved;
        }

        return createSceneResolved
            && lobbyUpdateResolved
            && loadLobbySceneResolved
            && currentMapResolved
            && (!cameraHookRequired || fixOnResolved);
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

        RecordSuccess(name, "TryScanText", address, address, true, IsWithinText(sigScanner, address), "TryScanTextDirect", "direct signature is configured as hook target");
        return true;
    }

    private bool TryResolveE8Call(
        ISigScanner sigScanner,
        string signature,
        string name,
        TitleBackgroundResolverMode resolverMode,
        bool allowDirectTextProbeTarget,
        out nint match,
        out nint hookTarget,
        bool required = true)
    {
        match = nint.Zero;
        hookTarget = nint.Zero;
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
            if (ShouldPromoteDirectTextCandidateForProbe(match, resolverMode, allowDirectTextProbeTarget))
            {
                var candidateDiagnostics = BuildCandidateDiagnostics(match);
                hookTarget = match;
                _scanResults.Add(new TitleBackgroundSignatureScanResult(
                    name,
                    "TryScanText+ManualDirectTextProbe",
                    "resolved-probe",
                    match,
                    match,
                    hookTarget,
                    false,
                    true,
                    "ManualDirectTextProbe",
                    IsWithinText(sigScanner, match),
                    IsWithinText(sigScanner, match),
                    IsWithinText(sigScanner, match),
                    $"{name} manual DirectText probe target is enabled; hook may observe calls but detours must not mutate state.",
                    candidateDiagnostics));
                return true;
            }

            var message = ShouldRecordDirectTextCandidate(match)
                ? $"{name} has a direct TryScanText candidate but no verified hook target; refusing to hook an unverified function offset."
                : $"{name} match does not contain a nearby E8 callsite.";
            RecordCandidateOnly(
                sigScanner,
                name,
                "TryScanText+DirectTextCandidate",
                "candidate-unverified",
                match,
                "TryScanTextDirectCandidate",
                message,
                required);
            return false;
        }

        var rel32 = *(int*)(callsite + 1);
        if (!TryResolveE8CallTarget(*(byte*)callsite, callsite, rel32, out hookTarget) || hookTarget == nint.Zero)
        {
            var message = $"{name} E8 callsite could not be decoded.";
            if (required)
            {
                LastError = string.IsNullOrWhiteSpace(LastError) ? message : LastError;
            }

            _scanResults.Add(new TitleBackgroundSignatureScanResult(
                name,
                "TryScanText+NearbyE8Rel32",
                "invalid-callsite",
                callsite,
                callsite,
                nint.Zero,
                false,
                false,
                "E8Rel32",
                IsWithinText(sigScanner, callsite),
                false,
                false,
                message,
                TitleBackgroundCandidateDiagnostics.Unavailable));
            return false;
        }

        var targetWithinText = IsWithinText(sigScanner, hookTarget);
        var safetyNote = targetWithinText
            ? "verified E8 rel32 hook target"
            : "verified E8 rel32 hook target; text section check is informational in this runtime";
        _scanResults.Add(new TitleBackgroundSignatureScanResult(
            name,
            "TryScanText+NearbyE8Rel32",
            "resolved",
            callsite,
            hookTarget,
            hookTarget,
            true,
            true,
            "E8Rel32",
            IsWithinText(sigScanner, callsite),
            targetWithinText,
            true,
            safetyNote,
            TitleBackgroundCandidateDiagnostics.Unavailable));
        return true;
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

        RecordSuccess(name, "TryGetStaticAddressFromSig", address, address, true, true, "StaticAddressFromSig", "static address resolved from signature");
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

        if (!TryResolveE8Call(
            sigScanner,
            signature,
            "UpdateLobbyUIStage",
            TitleBackgroundResolverMode.AutoDiagnosticOnly,
            allowDirectTextProbeTarget: false,
            out _,
            out address,
            required: false))
        {
            return false;
        }

        return true;
    }

    private void RecordSuccess(string name, string method, nint address, nint hookTarget, bool hookTargetVerified, bool targetWithinText, string addressSource, string safetyNote)
    {
        _scanResults.Add(new TitleBackgroundSignatureScanResult(
            name,
            method,
            "resolved",
            address,
            hookTarget,
            hookTarget,
            false,
            hookTargetVerified,
            addressSource,
            true,
            targetWithinText,
            hookTargetVerified,
            safetyNote,
            TitleBackgroundCandidateDiagnostics.Unavailable));
    }

    private void RecordCandidateOnly(ISigScanner sigScanner, string name, string method, string status, nint candidate, string addressSource, string message, bool required)
    {
        if (required)
        {
            LastError = string.IsNullOrWhiteSpace(LastError) ? message : LastError;
        }

        _scanResults.Add(new TitleBackgroundSignatureScanResult(
            name,
            method,
            status,
            candidate,
            candidate,
            nint.Zero,
            false,
            false,
            addressSource,
            IsWithinText(sigScanner, candidate),
            IsWithinText(sigScanner, candidate),
            false,
            message,
            BuildCandidateDiagnostics(candidate)));
    }

    private void RecordFailure(string name, string method, string status, string message, bool required = true)
    {
        if (required)
        {
            LastError = string.IsNullOrWhiteSpace(LastError) ? message : LastError;
        }

        _scanResults.Add(new TitleBackgroundSignatureScanResult(
            name,
            method,
            status,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            false,
            false,
            "none",
            false,
            false,
            false,
            message,
            TitleBackgroundCandidateDiagnostics.Unavailable));
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

    internal static bool ShouldRecordDirectTextCandidate(nint match)
    {
        return match != nint.Zero;
    }

    internal static bool ShouldPromoteDirectTextCandidateForProbe(
        nint match,
        TitleBackgroundResolverMode resolverMode,
        bool allowDirectTextProbeTarget)
    {
        return match != nint.Zero
            && allowDirectTextProbeTarget
            && resolverMode == TitleBackgroundResolverMode.ManualDirectTextProbe;
    }

    internal static string ClassifyFunctionPrologue(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
        {
            return "insufficient-bytes";
        }

        if (bytes[0] == 0x48 && bytes[1] == 0x89 && bytes[2] == 0x5C && bytes[3] == 0x24)
        {
            return "likely-msvc-prologue";
        }

        if (bytes[0] == 0x40 && bytes[1] == 0x53)
        {
            return "likely-msvc-prologue";
        }

        if (bytes[0] == 0x48 && bytes[1] == 0x83 && bytes[2] == 0xEC)
        {
            return "likely-stack-prologue";
        }

        if (bytes[0] == 0xE9 || bytes[0] == 0xE8)
        {
            return "branch-or-call";
        }

        if (bytes[0] == 0xCC || bytes[0] == 0xC3)
        {
            return "unlikely-function-start";
        }

        return "unknown";
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

    private static TitleBackgroundCandidateDiagnostics BuildCandidateDiagnostics(nint candidate)
    {
        if (candidate == nint.Zero)
        {
            return TitleBackgroundCandidateDiagnostics.Unavailable;
        }

        if (!TryReadBytes(candidate, CandidatePreviewBytes, out var bytes))
        {
            return new TitleBackgroundCandidateDiagnostics(false, "unreadable", string.Empty, "unreadable");
        }

        return new TitleBackgroundCandidateDiagnostics(
            true,
            ClassifyFunctionPrologue(bytes),
            Convert.ToHexString(bytes),
            "candidate bytes only; not treated as a verified hook target");
    }

    private static bool TryReadBytes(nint address, int length, out byte[] bytes)
    {
        bytes = [];
        if (length <= 0 || !IsReadableExecutableMemory(address, length))
        {
            return false;
        }

        bytes = new byte[length];
        Marshal.Copy(address, bytes, 0, length);
        return true;
    }

    private static bool IsReadableExecutableMemory(nint address, int length)
    {
        if (address == nint.Zero || length <= 0)
        {
            return false;
        }

        if (VirtualQuery(address, out var info, (nuint)Marshal.SizeOf<MemoryBasicInformation>()) == 0)
        {
            return false;
        }

        var start = address.ToInt64();
        var regionStart = info.BaseAddress.ToInt64();
        var regionEnd = checked(regionStart + (long)info.RegionSize);
        return info.State == MemoryState.Commit
            && start >= regionStart
            && checked(start + length) <= regionEnd
            && IsReadableProtection(info.Protect);
    }

    private static bool IsReadableProtection(MemoryProtection protect)
    {
        var normalized = protect & ~(MemoryProtection.Guard | MemoryProtection.NoCache | MemoryProtection.WriteCombine);
        return normalized is MemoryProtection.ReadOnly
            or MemoryProtection.ReadWrite
            or MemoryProtection.WriteCopy
            or MemoryProtection.ExecuteRead
            or MemoryProtection.ExecuteReadWrite
            or MemoryProtection.ExecuteWriteCopy;
    }

    private static string NormalizeSignature(string? signature)
    {
        return (signature ?? string.Empty).Trim();
    }

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern nuint VirtualQuery(nint lpAddress, out MemoryBasicInformation lpBuffer, nuint dwLength);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MemoryBasicInformation
    {
        public readonly nint BaseAddress;
        public readonly nint AllocationBase;
        public readonly MemoryProtection AllocationProtect;
        public readonly ushort PartitionId;
        public readonly nuint RegionSize;
        public readonly MemoryState State;
        public readonly MemoryProtection Protect;
        public readonly uint Type;
    }

    private enum MemoryState : uint
    {
        Commit = 0x1000,
    }

    [Flags]
    private enum MemoryProtection : uint
    {
        NoAccess = 0x01,
        ReadOnly = 0x02,
        ReadWrite = 0x04,
        WriteCopy = 0x08,
        Execute = 0x10,
        ExecuteRead = 0x20,
        ExecuteReadWrite = 0x40,
        ExecuteWriteCopy = 0x80,
        Guard = 0x100,
        NoCache = 0x200,
        WriteCombine = 0x400,
    }
}

internal sealed record TitleBackgroundSignatureScanResult(
    string Name,
    string Method,
    string Status,
    nint Address,
    nint ResolvedCandidate,
    nint HookTarget,
    bool IsE8Callsite,
    bool HookTargetVerified,
    string AddressSource,
    bool AddressWithinText,
    bool TargetWithinText,
    bool HookTargetWithinText,
    string SafetyNote,
    TitleBackgroundCandidateDiagnostics CandidateDiagnostics);

internal sealed record TitleBackgroundCandidateDiagnostics(
    bool Readable,
    string PrologueHint,
    string FirstBytesHex,
    string Note)
{
    public static readonly TitleBackgroundCandidateDiagnostics Unavailable = new(false, "unavailable", string.Empty, "unavailable");
}
