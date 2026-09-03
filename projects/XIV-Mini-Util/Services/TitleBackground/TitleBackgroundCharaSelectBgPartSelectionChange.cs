// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectBgPartSelectionChange.cs
// Description: FRU selection-change 時の InstanceType.BgPart を read-only で観測する、
//              bounded・run-scoped な TitleEdit parity 診断。
// Reason: TitleEdit の TE_Futures Rewritten が保存する BgPart state と、XMU の同一
//         n4gw / territory 1238 / layer 0 の selection-change 時状態を、少数の UUID へ
//         絞って offline 相関できるようにする。pre-event baseline は要求せず、
//         first valid post-event snapshot を source of truth とする。native write は行わない。
namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundCharaSelectBgPartSelectionChangeLogic
{
    // 1 pass の native map 走査上限。既存の typed VFX inventory と同じ安全スケール。
    public const int MaxScanPerPass = 4096;

    // 1 run で保持する unique TitleEdit UUID の上限。
    public const int MaxTrackedUuids = 4096;

    // report に出す first-pass active / changed 行の上限。
    public const int MaxReportedFirstPassActive = 1024;

    public const int MaxReportedChanged = 256;

    public static readonly string[] DiagnosticKeys = BuildDiagnosticKeys();

    private static string[] BuildDiagnosticKeys()
    {
        var keys = new List<string>
        {
            "fru.bgpart.complete",
            "fru.bgpart.firstPassCaptured",
            "fru.bgpart.firstPassTotalCount",
            "fru.bgpart.firstPassActiveCount",
            "fru.bgpart.firstPassActiveReportedCount",
            "fru.bgpart.changedCount",
            "fru.bgpart.changedReportedCount",
            "fru.bgpart.validPassCount",
            "fru.bgpart.readFailureCount",
            "fru.bgpart.gateBlockedPassCount",
            "fru.bgpart.truncated",
            "fru.bgpart.windowComplete",
        };

        for (var i = 0; i < MaxReportedFirstPassActive; i++)
        {
            keys.Add($"fru.bgpart.active{i}");
        }

        for (var i = 0; i < MaxReportedChanged; i++)
        {
            keys.Add($"fru.bgpart.changed{i}");
        }

        return [.. keys];
    }

    public static string FormatActiveRow(in TitleBackgroundBgPartObservation observation)
    {
        return $"uuid={observation.TitleEditUuid} instanceKey={observation.InstanceKey} "
            + $"subId={observation.SubId} active=true loaded={Bool(observation.IsPrimaryLoaded)} "
            + $"gfx={Bool(observation.HasGraphicsObject)} path={NormalizePath(observation.PrimaryPath)}";
    }

    public static string Bool(bool value) => value ? "true" : "false";

    public static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "none" : path.Trim();
    }

    public static string SanitizePath(string? path)
    {
        return TitleBackgroundCharaSelectSceneObjectSuppressionLogic
            .SanitizeGameAssetPathForDiagnostics(path);
    }
}

// BgPart 1 件分の safe typed read。path は既に bg/ または bgcommon/ に限定した managed 値。
internal readonly record struct TitleBackgroundBgPartObservation(
    ulong TitleEditUuid,
    uint InstanceKey,
    uint SubId,
    bool IsActive,
    string PrimaryPath,
    bool IsPrimaryLoaded,
    bool HasGraphicsObject);

// 1 UUID の first/last state と、complete valid pass 間の presence/state 遷移。
// native pointer / address は保持しない。
internal sealed class TitleBackgroundBgPartAccumulator
{
    public ulong TitleEditUuid { get; }

    public uint InstanceKey { get; }

    public uint SubId { get; }

    public bool FirstPresent { get; }

    public bool FinalPresent { get; private set; }

    public bool Appeared { get; private set; }

    public bool Disappeared { get; private set; }

    public bool StateChanged { get; private set; }

    public bool FirstActive { get; }

    public bool FinalActive { get; private set; }

    public bool EverActive { get; private set; }

    public bool EverInactive { get; private set; }

    public long FirstChangeElapsedMs { get; private set; } = -1;

    public string FirstPath { get; }

    public string FinalPath { get; private set; }

    public bool FirstLoaded { get; }

    public bool FinalLoaded { get; private set; }

    public bool FirstGfx { get; }

    public bool FinalGfx { get; private set; }

    public int ValidPassCount { get; private set; } = 1;

    public bool Changed => Appeared
        || Disappeared
        || StateChanged
        || FirstActive != FinalActive
        || FirstLoaded != FinalLoaded
        || FirstGfx != FinalGfx
        || !string.Equals(FirstPath, FinalPath, StringComparison.Ordinal);

    public TitleBackgroundBgPartAccumulator(
        in TitleBackgroundBgPartObservation observation,
        bool firstPresent,
        long elapsedMs)
    {
        TitleEditUuid = observation.TitleEditUuid;
        InstanceKey = observation.InstanceKey;
        SubId = observation.SubId;
        FirstPresent = firstPresent;
        FinalPresent = true;
        Appeared = !firstPresent;
        StateChanged = Appeared;
        FirstActive = observation.IsActive;
        FinalActive = observation.IsActive;
        EverActive = observation.IsActive;
        EverInactive = !observation.IsActive;
        FirstPath = observation.PrimaryPath;
        FinalPath = observation.PrimaryPath;
        FirstLoaded = observation.IsPrimaryLoaded;
        FinalLoaded = observation.IsPrimaryLoaded;
        FirstGfx = observation.HasGraphicsObject;
        FinalGfx = observation.HasGraphicsObject;
        if (Appeared)
        {
            FirstChangeElapsedMs = elapsedMs;
        }
    }

    public void ObservePresent(in TitleBackgroundBgPartObservation observation, long elapsedMs)
    {
        var changed = !FinalPresent
            || FinalActive != observation.IsActive
            || FinalLoaded != observation.IsPrimaryLoaded
            || FinalGfx != observation.HasGraphicsObject
            || !string.Equals(FinalPath, observation.PrimaryPath, StringComparison.Ordinal);

        if (!FinalPresent)
        {
            Appeared = true;
        }

        StateChanged |= changed;

        if (changed && FirstChangeElapsedMs < 0)
        {
            FirstChangeElapsedMs = elapsedMs;
        }

        FinalPresent = true;
        FinalActive = observation.IsActive;
        FinalPath = observation.PrimaryPath;
        FinalLoaded = observation.IsPrimaryLoaded;
        FinalGfx = observation.HasGraphicsObject;
        EverActive |= observation.IsActive;
        EverInactive |= !observation.IsActive;
        ValidPassCount++;
    }

    public void ObserveAbsent(long elapsedMs)
    {
        if (FinalPresent)
        {
            Disappeared = true;
            StateChanged = true;
            if (FirstChangeElapsedMs < 0)
            {
                FirstChangeElapsedMs = elapsedMs;
            }
        }

        FinalPresent = false;
        ValidPassCount++;
    }

    public string FormatFirstPassActive()
    {
        return TitleBackgroundCharaSelectBgPartSelectionChangeLogic.FormatActiveRow(
            new TitleBackgroundBgPartObservation(
                TitleEditUuid,
                InstanceKey,
                SubId,
                FirstActive,
                FirstPath,
                FirstLoaded,
                FirstGfx));
    }

    public string FormatChanged()
    {
        return $"uuid={TitleEditUuid} instanceKey={InstanceKey} subId={SubId} "
            + $"firstPresent={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(FirstPresent)} "
            + $"finalPresent={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(FinalPresent)} "
            + $"appeared={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(Appeared)} "
            + $"disappeared={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(Disappeared)} "
            + $"firstActive={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(FirstActive)} "
            + $"finalActive={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(FinalActive)} "
            + $"everActive={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(EverActive)} "
            + $"everInactive={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(EverInactive)} "
            + $"changedAtMs={FirstChangeElapsedMs} validPasses={ValidPassCount} "
            + $"firstLoaded={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(FirstLoaded)} "
            + $"finalLoaded={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(FinalLoaded)} "
            + $"firstGfx={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(FirstGfx)} "
            + $"finalGfx={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.Bool(FinalGfx)} "
            + $"firstPath={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.NormalizePath(FirstPath)} "
            + $"finalPath={TitleBackgroundCharaSelectBgPartSelectionChangeLogic.NormalizePath(FinalPath)}";
    }

}

// 1 回の selection-change に紐づく bounded BgPart 観測。pre-event baseline は要求しない。
// lifecycle は既存の final selection-change delta state から駆動される。
internal sealed class TitleBackgroundCharaSelectBgPartSelectionChangeRuntimeState
{
    public bool Armed { get; private set; }

    public bool WindowComplete { get; private set; }

    public bool FirstPassCaptured { get; private set; }

    public int FirstPassTotalCount { get; private set; }

    public int FirstPassActiveCount { get; private set; }

    public int FirstPassActiveReportedCount => Math.Min(
        FirstPassActiveCount,
        TitleBackgroundCharaSelectBgPartSelectionChangeLogic.MaxReportedFirstPassActive);

    public int ChangedCount
    {
        get
        {
            var count = 0;
            foreach (var accumulator in _tracking.Values)
            {
                if (accumulator.Changed)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int ChangedReportedCount => Math.Min(
        ChangedCount,
        TitleBackgroundCharaSelectBgPartSelectionChangeLogic.MaxReportedChanged);

    public int ValidPassCount { get; private set; }

    public int ReadFailureCount { get; private set; }

    public int GateBlockedPassCount { get; private set; }

    public bool Truncated => _truncated
        || FirstPassActiveCount > TitleBackgroundCharaSelectBgPartSelectionChangeLogic.MaxReportedFirstPassActive
        || ChangedCount > TitleBackgroundCharaSelectBgPartSelectionChangeLogic.MaxReportedChanged;

    // complete は terminal かつ first snapshot / report bounds が安全に揃った場合だけ true。
    public bool Complete => Armed && WindowComplete && FirstPassCaptured && !Truncated;

    public bool ShouldRunPass => Armed && !WindowComplete && !Truncated;

    private readonly Dictionary<ulong, TitleBackgroundBgPartObservation> _pass = new();
    private readonly Dictionary<ulong, TitleBackgroundBgPartAccumulator> _tracking = new();
    private bool _passReadFailed;
    private bool _passTruncated;
    private bool _truncated;

    public void ArmFromReArm()
    {
        if (Armed && !Complete)
        {
            return;
        }

        Reset();
        Armed = true;
    }

    public void BeginPass()
    {
        if (!ShouldRunPass)
        {
            return;
        }

        _pass.Clear();
        _passReadFailed = false;
        _passTruncated = false;
    }

    // false は unique UUID または map scan bound 超過。呼び出し側は pass を fail-closed で終了する。
    public bool TryRecordInstance(in TitleBackgroundBgPartObservation observation)
    {
        if (!ShouldRunPass)
        {
            return false;
        }

        if (!_pass.ContainsKey(observation.TitleEditUuid)
            && !_tracking.ContainsKey(observation.TitleEditUuid)
            && _tracking.Count + _pass.Count >= TitleBackgroundCharaSelectBgPartSelectionChangeLogic.MaxTrackedUuids)
        {
            MarkTruncated();
            return false;
        }

        _pass[observation.TitleEditUuid] = observation with
        {
            PrimaryPath = TitleBackgroundCharaSelectBgPartSelectionChangeLogic.SanitizePath(
                observation.PrimaryPath),
        };
        return true;
    }

    public void RecordReadFailure()
    {
        ReadFailureCount++;
        _passReadFailed = true;
    }

    public void MarkTruncated()
    {
        _passTruncated = true;
        _truncated = true;
    }

    public void FinishPass(bool valid, long elapsedMs)
    {
        if (!ShouldRunPass)
        {
            _pass.Clear();
            return;
        }

        if (!valid || _passReadFailed || _passTruncated)
        {
            _pass.Clear();
            return;
        }

        ValidPassCount++;
        if (!FirstPassCaptured)
        {
            FirstPassCaptured = true;
            FirstPassTotalCount = _pass.Count;
            FirstPassActiveCount = _pass.Values.Count(value => value.IsActive);
            foreach (var observation in _pass.Values)
            {
                _tracking[observation.TitleEditUuid] = new TitleBackgroundBgPartAccumulator(
                    observation,
                    firstPresent: true,
                    elapsedMs);
            }
        }
        else
        {
            foreach (var observation in _pass.Values)
            {
                if (_tracking.TryGetValue(observation.TitleEditUuid, out var accumulator))
                {
                    accumulator.ObservePresent(observation, elapsedMs);
                }
                else
                {
                    _tracking[observation.TitleEditUuid] = new TitleBackgroundBgPartAccumulator(
                        observation,
                        firstPresent: false,
                        elapsedMs);
                }
            }

            foreach (var accumulator in _tracking.Values)
            {
                if (!_pass.ContainsKey(accumulator.TitleEditUuid))
                {
                    accumulator.ObserveAbsent(elapsedMs);
                }
            }
        }

        _pass.Clear();
    }

    public void RecordGateBlockedPass()
    {
        if (Armed && !WindowComplete)
        {
            GateBlockedPassCount++;
        }
    }

    public void MarkWindowComplete()
    {
        if (Armed)
        {
            WindowComplete = true;
        }
    }

    public void MarkSessionEnd()
    {
        if (Armed)
        {
            WindowComplete = true;
        }
    }

    public IEnumerable<string> BuildDiagnosticLines()
    {
        yield return $"fru.bgpart.complete={Complete}";
        yield return $"fru.bgpart.firstPassCaptured={FirstPassCaptured}";
        yield return $"fru.bgpart.firstPassTotalCount={FirstPassTotalCount}";
        yield return $"fru.bgpart.firstPassActiveCount={FirstPassActiveCount}";
        yield return $"fru.bgpart.firstPassActiveReportedCount={FirstPassActiveReportedCount}";
        yield return $"fru.bgpart.changedCount={ChangedCount}";
        yield return $"fru.bgpart.changedReportedCount={ChangedReportedCount}";
        yield return $"fru.bgpart.validPassCount={ValidPassCount}";
        yield return $"fru.bgpart.readFailureCount={ReadFailureCount}";
        yield return $"fru.bgpart.gateBlockedPassCount={GateBlockedPassCount}";
        yield return $"fru.bgpart.truncated={Truncated}";
        yield return $"fru.bgpart.windowComplete={WindowComplete}";

        var activeIndex = 0;
        foreach (var accumulator in _tracking.Values
                     .Where(value => value.FirstPresent && value.FirstActive)
                     .OrderBy(value => value.TitleEditUuid))
        {
            if (activeIndex >= TitleBackgroundCharaSelectBgPartSelectionChangeLogic.MaxReportedFirstPassActive)
            {
                break;
            }

            yield return $"fru.bgpart.active{activeIndex}={accumulator.FormatFirstPassActive()}";
            activeIndex++;
        }

        var changedIndex = 0;
        foreach (var accumulator in _tracking.Values
                     .Where(value => value.Changed)
                     .OrderBy(value => value.TitleEditUuid))
        {
            if (changedIndex >= TitleBackgroundCharaSelectBgPartSelectionChangeLogic.MaxReportedChanged)
            {
                break;
            }

            yield return $"fru.bgpart.changed{changedIndex}={accumulator.FormatChanged()}";
            changedIndex++;
        }
    }

    public void Reset()
    {
        Armed = false;
        WindowComplete = false;
        FirstPassCaptured = false;
        FirstPassTotalCount = 0;
        FirstPassActiveCount = 0;
        ValidPassCount = 0;
        ReadFailureCount = 0;
        GateBlockedPassCount = 0;
        _pass.Clear();
        _tracking.Clear();
        _passReadFailed = false;
        _passTruncated = false;
        _truncated = false;
    }
}
