// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectSelectionChangeDelta.cs
// Description: Phase A 最終診断（review 5521774559）。1 回の通常のキャラ/ワールド選択変更で、
//              transient FRU geometry を sharedgroup-delta / vfx-delta / no-safe-layout-delta / incomplete
//              のいずれか 1 つへ確定させるための、bounded・READ-ONLY・managed な delta 証拠。
// Reason: 実機 c4d253b で flicker=YES・class=InsufficientEvidence（12-path whole-inactive probe 不発）。
//         (1) deny-covered 176 group は flicker 表示時点で既に inactive、(2) 12 sample のどれも 2500ms 内に
//         完全 inactive にならない、(3) 診断 sample が KeepPathTokens（_flo/_lig 等）で埋まる bias。
//         そこで「完全 inactive」ではなく「active-instance 数の変化」を、KeepPathToken を除いた
//         eligible SharedGroup path（最大 512）と、既に検証済みの typed VFX read（最大 4096）で捉える。
//         Evaluate / DenyPathTokens / KeepPathTokens / suppression semantics / SetActive は一切変更しない。
//         native pointer / address / instance id は保持しない。VFX write は 1 件も行わない。
namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundSelectionChangeDeltaLogic
{
    // 追跡する eligible SharedGroup path 数の上限（unique sanitized game-asset path）。
    public const int MaxTrackedSharedGroupPaths = 512;

    // report へ載せる「変化した候補」数の上限（sg0..sg19 / vfx0..vfx19）。
    public const int MaxReportedChanged = 20;

    // VFX scan の 1 パス上限（既存 VFX inventory の MaxScanPerPass と同じスケール。実測 FRU ~248）。
    public const int MaxVfxScanEntries = 4096;

    // 診断専用の KeepPathToken 判定。Evaluate / KeepPathTokens 自体は変更しない。
    // eligible diagnostic path から、検証済み clear-stage keep content（_flo/_lig/_plt/_tree/_enkei/_a8_/_light）
    // を除外するためだけに使う。入力は SanitizeGameAssetPath 済み（小文字）を想定。
    public static bool MatchesKnownKeepToken(string? loweredSanitizedPath)
    {
        var path = loweredSanitizedPath ?? string.Empty;
        if (path.Length == 0)
        {
            return false;
        }

        foreach (var token in TitleBackgroundCharaSelectSceneObjectSuppressionLogic.KeepPathTokens)
        {
            if (path.Contains(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // 最終 outcome の優先順位:
    //  1. eligible SharedGroup の active-instance 数 delta -> sharedgroup-delta
    //     （Phase B が既存の検証済み SharedGroup suppression を使えるため最優先）
    //  2. それ以外で typed VFX identity/state delta -> vfx-delta
    //  3. 2500ms 完走 + SG/VFX の valid scan あり + pre-event baseline を明示的に取得済み +
    //     read/gate failure が 1 件も無い -> no-safe-layout-delta（信頼できる negative）
    //  4. それ以外（gate blocked / read failure / baseline 未取得 等）-> incomplete
    // review 5099109840: gateBlockedPassCount>0 の negative は no-safe-layout-delta にしない。
    // baseline 未取得なら appeared/change を positive delta にせず、outcome は incomplete。
    public static string ClassifyFinalOutcome(
        int sharedGroupChangedCount,
        int vfxChangedCount,
        bool windowComplete,
        int sharedGroupValidPassCount,
        int vfxValidPassCount,
        int readFailureCount,
        int gateBlockedPassCount,
        bool sharedGroupBaselineAvailable,
        bool vfxBaselineAvailable)
    {
        if (sharedGroupChangedCount > 0)
        {
            return "sharedgroup-delta";
        }

        if (vfxChangedCount > 0)
        {
            return "vfx-delta";
        }

        if (windowComplete
            && readFailureCount == 0
            && gateBlockedPassCount == 0
            && sharedGroupBaselineAvailable
            && vfxBaselineAvailable
            && sharedGroupValidPassCount > 0
            && vfxValidPassCount > 0)
        {
            return "no-safe-layout-delta";
        }

        return "incomplete";
    }

    // BuildFinalDiagnosticLines が出す静的キー（動的 sg*/vfx* を含む機械列挙。実装と乖離させない）。
    public static readonly string[] DiagnosticKeys = BuildDiagnosticKeys();

    private static string[] BuildDiagnosticKeys()
    {
        var keys = new List<string>
        {
            "fru.suppression.selectionChange.final.armed",
            "fru.suppression.selectionChange.final.complete",
            "fru.suppression.selectionChange.final.outcome",
            "fru.suppression.selectionChange.final.sharedGroupTrackedCount",
            "fru.suppression.selectionChange.final.sharedGroupChangedCount",
            "fru.suppression.selectionChange.final.sharedGroupValidPassCount",
            "fru.suppression.selectionChange.final.sharedGroupPathCapReached",
            "fru.suppression.selectionChange.final.vfxBaselineCount",
            "fru.suppression.selectionChange.final.vfxChangedCount",
            "fru.suppression.selectionChange.final.vfxValidPassCount",
            "fru.suppression.selectionChange.final.readFailureCount",
            "fru.suppression.selectionChange.final.gateBlockedPassCount",
            "fru.suppression.selectionChange.final.sharedGroupBaselineAvailable",
            "fru.suppression.selectionChange.final.vfxBaselineAvailable",
        };

        for (var i = 0; i < MaxReportedChanged; i++)
        {
            keys.Add($"fru.suppression.selectionChange.final.sg{i}");
        }

        for (var i = 0; i < MaxReportedChanged; i++)
        {
            keys.Add($"fru.suppression.selectionChange.final.vfx{i}");
        }

        return [.. keys];
    }
}

// 1 つの eligible SharedGroup path の active-instance 数の推移。managed only。
// path が完全 inactive になる必要はない: 2->1 / 0->1->0 / 出現 / 消失 いずれも delta。
internal sealed class TitleBackgroundSharedGroupPathDeltaAccumulator
{
    public int Baseline { get; }

    public int First { get; private set; } = -1;

    public int Min { get; private set; } = int.MaxValue;

    public int Max { get; private set; } = -1;

    public int Final { get; private set; } = -1;

    public long FirstChangeElapsedMs { get; private set; } = -1;

    public int PassCount { get; private set; }

    public TitleBackgroundSharedGroupPathDeltaAccumulator(int baseline)
    {
        Baseline = baseline < 0 ? 0 : baseline;
    }

    // 1 つの valid pass の observed active count を畳み込む。
    public void Observe(int activeCount, long elapsedMs)
    {
        if (activeCount < 0)
        {
            activeCount = 0;
        }

        PassCount++;
        if (First < 0)
        {
            First = activeCount;
        }

        if (activeCount < Min)
        {
            Min = activeCount;
        }

        if (activeCount > Max)
        {
            Max = activeCount;
        }

        Final = activeCount;

        if (activeCount != Baseline && FirstChangeElapsedMs < 0)
        {
            FirstChangeElapsedMs = elapsedMs < 0 ? 0 : elapsedMs;
        }
    }

    public bool Changed => PassCount > 0
        && (FirstOrBaseline != Baseline
            || MinOrBaseline != Baseline
            || MaxOrBaseline != Baseline
            || FinalOrBaseline != Baseline);

    private int FirstOrBaseline => First < 0 ? Baseline : First;

    private int MinOrBaseline => Min == int.MaxValue ? Baseline : Min;

    private int MaxOrBaseline => Max < 0 ? Baseline : Max;

    private int FinalOrBaseline => Final < 0 ? Baseline : Final;

    public string Format(string path)
    {
        return $"path={path} baseline={Baseline} first={FirstOrBaseline} min={MinOrBaseline} "
            + $"max={MaxOrBaseline} final={FinalOrBaseline} changedAtMs={FirstChangeElapsedMs} passes={PassCount}";
    }
}

// 1 つの typed VFX identity（TitleEdit UUID）の read-only state。raw pointer/address は持たない。
internal readonly record struct TitleBackgroundVfxDeltaState(
    bool Active,
    bool Loaded,
    bool Gfx,
    uint PathHash);

// Phase A 最終診断の run-scoped managed 状態。scene generation / candidate を跨いで永続化しない。
// write は 1 件も行わない。native pointer / address / instance id は保持しない。
internal sealed class TitleBackgroundCharaSelectSelectionChangeDeltaRuntimeState
{
    // TitleEdit parity の最後の BgPart checkpoint。既存 final diagnostic と同じ re-arm /
    // terminal / reset lifecycle に乗せるが、observe 対象は BgPart だけで write は無い。
    private readonly TitleBackgroundCharaSelectBgPartSelectionChangeRuntimeState _bgPart = new();

    public TitleBackgroundCharaSelectBgPartSelectionChangeRuntimeState BgPart => _bgPart;

    public bool Armed { get; private set; }

    public bool WindowComplete { get; private set; }

    public string Outcome { get; private set; } = "not-run";

    public int ReadFailureCount { get; private set; }

    public int GateBlockedPassCount { get; private set; }

    // review 5099109840: pre-event baseline を明示管理する。
    // SharedGroup baseline = re-arm 前に valid な通常 suppression pass を 1 回以上取得済みか
    //   （path が 0 件でも valid pass なら「空 baseline あり」= true）。
    // VFX baseline = re-arm 時に current-generation の信頼できる既存 VFX inventory snapshot を受け取れたか。
    public bool SharedGroupBaselineAvailable { get; private set; }

    public bool VfxBaselineAvailable { get; private set; }

    // --- SharedGroup ---
    // 選択変更 re-arm 前の「直近の authorized な通常 suppression pass」の path->activeCount（managed copy）。
    private readonly Dictionary<string, int> _latestOrdinaryPassCounts = new(StringComparer.Ordinal);

    // 上記が valid な pass 由来か（空でも true になりうる）。ArmFromReArm で SharedGroupBaselineAvailable へ写す。
    private bool _latestOrdinaryPassValid;

    // re-arm 時に _latestOrdinaryPassCounts から snapshot した baseline。
    private readonly Dictionary<string, int> _baseline = new(StringComparer.Ordinal);

    private readonly Dictionary<string, TitleBackgroundSharedGroupPathDeltaAccumulator> _tracking =
        new(StringComparer.Ordinal);

    // 現在の SharedGroup delta pass の path->activeCount（scan 側が Begin/Record で埋める）。
    private readonly Dictionary<string, int> _passCounts = new(StringComparer.Ordinal);

    public bool SharedGroupPathCapReached { get; private set; }

    public int SharedGroupTrackedCount => _tracking.Count;

    public int SharedGroupValidPassCount { get; private set; }

    public int SharedGroupChangedCount
    {
        get
        {
            var n = 0;
            foreach (var acc in _tracking.Values)
            {
                if (acc.Changed)
                {
                    n++;
                }
            }

            return n;
        }
    }

    // --- VFX ---
    private readonly Dictionary<ulong, TitleBackgroundVfxDeltaState> _vfxBaseline = new();
    private readonly Dictionary<ulong, TitleBackgroundVfxDeltaState> _vfxCurrent = new();
    private readonly Dictionary<ulong, string> _vfxChange = new();
    private readonly Dictionary<ulong, uint> _vfxPathHash = new();
    private readonly Dictionary<ulong, string> _vfxPath = new();

    public int VfxBaselineCount => _vfxBaseline.Count;

    public int VfxValidPassCount { get; private set; }

    public int VfxChangedCount => _vfxChange.Count;

    private bool _sessionEnded;

    // MUST FIX (review 5098946239 #2): 現在の SG/VFX pass で per-instance read failure が 1 件でもあったか。
    // Begin*Pass でリセットし、その pass は valid=false 扱い（baseline->0 / disappeared を合成しない）。
    private bool _passReadFailed;

    public bool Complete => WindowComplete || _sessionEnded;

    // ---- 通常 suppression pass（re-arm 前）からの baseline source 更新 ----

    // 通常の（Armed でない）authorized suppression pass 終了時に、その pass の path->activeCount を
    // 直近値として保持する。native scan は増やさない（呼び出し側が既存 scan 内で _passCounts を埋める）。
    public void RecordOrdinarySuppressionPass(bool valid)
    {
        if (Armed || !valid)
        {
            _passCounts.Clear();
            return;
        }

        // valid な通常 pass を 1 回でも取得できれば SharedGroup baseline は「取得済み」（空でも可）。
        _latestOrdinaryPassValid = true;
        _latestOrdinaryPassCounts.Clear();
        foreach (var kv in _passCounts)
        {
            _latestOrdinaryPassCounts[kv.Key] = kv.Value;
        }

        _passCounts.Clear();
    }

    // Framework スレッドの選択変更 re-arm で呼ぶ。managed baseline だけを snapshot する（native scan なし）。
    // MUST FIX (review 5098946239 #1): 1 回の user-visible switch burst 中の複数 re-arm で、baseline /
    // accumulated SG/VFX delta をリセットしない。まだ観測中（Armed かつ未 terminal）なら no-op で、
    // burst の最初の re-arm が張った baseline を維持し、early delta を取りこぼさない。
    // window が terminal（timeout / session end）or Reset() の後は、次の switch で改めて re-baseline する。
    // vfxSnapshotReliable: この switch の scene generation に対応する、信頼できる既存 VFX inventory
    // snapshot（valid な完了パスあり）を受け取れたか。false のとき VFX baseline は「未取得」とする。
    public void ArmFromReArm(IReadOnlyList<TitleBackgroundVfxDetailEntry> vfxSnapshot, bool vfxSnapshotReliable)
    {
        if (Armed && !Complete)
        {
            return;
        }

        ResetObservation();
        Armed = true;
        Outcome = "observing";
        _bgPart.ArmFromReArm();

        SharedGroupBaselineAvailable = _latestOrdinaryPassValid;
        if (SharedGroupBaselineAvailable)
        {
            foreach (var kv in _latestOrdinaryPassCounts)
            {
                if (_baseline.Count >= TitleBackgroundSelectionChangeDeltaLogic.MaxTrackedSharedGroupPaths)
                {
                    SharedGroupPathCapReached = true;
                    break;
                }

                _baseline[kv.Key] = kv.Value;
            }
        }

        VfxBaselineAvailable = vfxSnapshotReliable && vfxSnapshot != null && vfxSnapshot.Count > 0;
        if (VfxBaselineAvailable)
        {
            foreach (var e in vfxSnapshot!)
            {
                if (_vfxBaseline.Count >= TitleBackgroundSelectionChangeDeltaLogic.MaxVfxScanEntries)
                {
                    break;
                }

                var uuid = e.TitleEditUuid;
                _vfxBaseline[uuid] = new(e.IsActive, e.IsPrimaryLoaded, e.HasGraphicsObject, e.PathHash);
                _vfxPathHash[uuid] = e.PathHash;
                if (!string.IsNullOrEmpty(e.PrimaryPath))
                {
                    _vfxPath[uuid] = e.PrimaryPath!;
                }
            }
        }
    }

    // ---- SharedGroup delta pass ----

    public void BeginSharedGroupPass()
    {
        _passCounts.Clear();
        _passReadFailed = false;
    }

    // scan が 1 instance を渡す。sanitizedPath は SanitizeGameAssetPath 済み・非 keep-token の eligible path。
    public void RecordSharedGroupInstance(string sanitizedPath, bool isActive)
    {
        if (string.IsNullOrEmpty(sanitizedPath))
        {
            return;
        }

        if (!_passCounts.ContainsKey(sanitizedPath))
        {
            if (!_tracking.ContainsKey(sanitizedPath)
                && !_baseline.ContainsKey(sanitizedPath)
                && _passCounts.Count + _tracking.Count
                    >= TitleBackgroundSelectionChangeDeltaLogic.MaxTrackedSharedGroupPaths)
            {
                SharedGroupPathCapReached = true;
                return;
            }

            _passCounts[sanitizedPath] = 0;
        }

        if (isActive)
        {
            _passCounts[sanitizedPath]++;
        }
    }

    // pass 終了。Armed なら delta として畳み込む。partial/failed scan（valid=false）や、
    // この pass で per-instance read failure が 1 件でもあった場合（_passReadFailed）は
    // valid=false 扱いにして 0 を合成しない（baseline->0 / disappeared を作らない）。
    public void FinishSharedGroupPass(bool valid, long elapsedMs)
    {
        var passValid = valid && !_passReadFailed;

        if (!Armed)
        {
            RecordOrdinarySuppressionPass(passValid);
            return;
        }

        if (!passValid)
        {
            _passCounts.Clear();
            return;
        }

        SharedGroupValidPassCount++;

        // review 5099109840: pre-event baseline 未取得なら appeared/change を positive delta にしない。
        if (!SharedGroupBaselineAvailable)
        {
            _passCounts.Clear();
            RecomputeOutcome();
            return;
        }

        // observed path ∪ baseline path。baseline にあってこの valid pass に現れない path は current=0。
        var paths = new HashSet<string>(_passCounts.Keys, StringComparer.Ordinal);
        foreach (var b in _baseline.Keys)
        {
            paths.Add(b);
        }

        foreach (var p in paths)
        {
            if (!_tracking.TryGetValue(p, out var acc))
            {
                if (_tracking.Count >= TitleBackgroundSelectionChangeDeltaLogic.MaxTrackedSharedGroupPaths)
                {
                    SharedGroupPathCapReached = true;
                    continue;
                }

                acc = new TitleBackgroundSharedGroupPathDeltaAccumulator(
                    _baseline.TryGetValue(p, out var b0) ? b0 : 0);
                _tracking[p] = acc;
            }

            acc.Observe(_passCounts.TryGetValue(p, out var c) ? c : 0, elapsedMs);
        }

        _passCounts.Clear();
        RecomputeOutcome();
    }

    // ---- VFX delta pass ----

    public void BeginVfxPass()
    {
        _vfxCurrent.Clear();
        _passReadFailed = false;
    }

    public void RecordVfxInstance(ulong uuid, bool isActive, bool loaded, bool gfx, uint pathHash, string? primaryPath)
    {
        if (_vfxCurrent.Count >= TitleBackgroundSelectionChangeDeltaLogic.MaxVfxScanEntries)
        {
            return;
        }

        _vfxCurrent[uuid] = new(isActive, loaded, gfx, pathHash);
        _vfxPathHash[uuid] = pathHash;
        if (!string.IsNullOrEmpty(primaryPath))
        {
            _vfxPath[uuid] = primaryPath!;
        }
    }

    // valid=false（partial/failed scan）や、この pass で per-instance read failure が 1 件でもあった場合
    // （_passReadFailed）は disappear / change を合成しない。
    public void FinishVfxPass(bool valid)
    {
        if (!Armed || !valid || _passReadFailed)
        {
            _vfxCurrent.Clear();
            return;
        }

        VfxValidPassCount++;

        // review 5099109840: VFX baseline 未取得なら appeared/disappeared/change を positive delta にしない。
        if (!VfxBaselineAvailable)
        {
            _vfxCurrent.Clear();
            RecomputeOutcome();
            return;
        }

        foreach (var kv in _vfxCurrent)
        {
            if (!_vfxBaseline.TryGetValue(kv.Key, out var b))
            {
                _vfxChange[kv.Key] = "appeared";
                continue;
            }

            var c = kv.Value;
            var flags = string.Empty;
            if (b.Active != c.Active)
            {
                flags += "active+";
            }

            if (b.Loaded != c.Loaded)
            {
                flags += "loaded+";
            }

            if (b.Gfx != c.Gfx)
            {
                flags += "gfx+";
            }

            if (b.PathHash != c.PathHash)
            {
                flags += "path+";
            }

            if (flags.Length > 0)
            {
                _vfxChange[kv.Key] = flags.TrimEnd('+');
            }
        }

        // complete valid pass でのみ disappear を判定する。
        foreach (var uuid in _vfxBaseline.Keys)
        {
            if (!_vfxCurrent.ContainsKey(uuid))
            {
                _vfxChange[uuid] = "disappeared";
            }
        }

        RecomputeOutcome();
    }

    // ---- failure / terminal ----

    // per-instance read failure。window 累積 counter を進め、かつ現在の SG/VFX pass を invalidate する
    // （その pass では baseline->0 / disappeared / change を合成しない）。
    public void RecordReadFailure()
    {
        ReadFailureCount++;
        _passReadFailed = true;
    }

    public void RecordGateBlockedPass()
    {
        if (Armed && !WindowComplete)
        {
            GateBlockedPassCount++;
            _bgPart.RecordGateBlockedPass();
        }
    }

    public void MarkWindowComplete()
    {
        if (!Armed)
        {
            return;
        }

        WindowComplete = true;
        _bgPart.MarkWindowComplete();
        RecomputeOutcome();
    }

    public void MarkSessionEnd()
    {
        if (!Armed)
        {
            return;
        }

        _sessionEnded = true;
        WindowComplete = true;
        _bgPart.MarkSessionEnd();
        RecomputeOutcome();
    }

    private void RecomputeOutcome()
    {
        Outcome = TitleBackgroundSelectionChangeDeltaLogic.ClassifyFinalOutcome(
            SharedGroupChangedCount,
            VfxChangedCount,
            WindowComplete,
            SharedGroupValidPassCount,
            VfxValidPassCount,
            ReadFailureCount,
            GateBlockedPassCount,
            SharedGroupBaselineAvailable,
            VfxBaselineAvailable);
    }

    // ---- report ----

    public IEnumerable<string> BuildFinalDiagnosticLines()
    {
        yield return $"fru.suppression.selectionChange.final.armed={Armed}";
        yield return $"fru.suppression.selectionChange.final.complete={Complete}";
        yield return $"fru.suppression.selectionChange.final.outcome={Outcome}";
        yield return $"fru.suppression.selectionChange.final.sharedGroupTrackedCount={SharedGroupTrackedCount}";
        yield return $"fru.suppression.selectionChange.final.sharedGroupChangedCount={SharedGroupChangedCount}";
        yield return $"fru.suppression.selectionChange.final.sharedGroupValidPassCount={SharedGroupValidPassCount}";
        yield return $"fru.suppression.selectionChange.final.sharedGroupPathCapReached={SharedGroupPathCapReached}";
        yield return $"fru.suppression.selectionChange.final.vfxBaselineCount={VfxBaselineCount}";
        yield return $"fru.suppression.selectionChange.final.vfxChangedCount={VfxChangedCount}";
        yield return $"fru.suppression.selectionChange.final.vfxValidPassCount={VfxValidPassCount}";
        yield return $"fru.suppression.selectionChange.final.readFailureCount={ReadFailureCount}";
        yield return $"fru.suppression.selectionChange.final.gateBlockedPassCount={GateBlockedPassCount}";
        yield return $"fru.suppression.selectionChange.final.sharedGroupBaselineAvailable={SharedGroupBaselineAvailable}";
        yield return $"fru.suppression.selectionChange.final.vfxBaselineAvailable={VfxBaselineAvailable}";

        var sgIndex = 0;
        foreach (var kv in _tracking)
        {
            if (sgIndex >= TitleBackgroundSelectionChangeDeltaLogic.MaxReportedChanged)
            {
                break;
            }

            if (!kv.Value.Changed)
            {
                continue;
            }

            yield return $"fru.suppression.selectionChange.final.sg{sgIndex}={kv.Value.Format(kv.Key)}";
            sgIndex++;
        }

        var vfxIndex = 0;
        foreach (var kv in _vfxChange)
        {
            if (vfxIndex >= TitleBackgroundSelectionChangeDeltaLogic.MaxReportedChanged)
            {
                break;
            }

            var uuid = kv.Key;
            _vfxBaseline.TryGetValue(uuid, out var b);
            _vfxCurrent.TryGetValue(uuid, out var c);
            var hash = _vfxPathHash.TryGetValue(uuid, out var h) ? h : 0u;
            var path = _vfxPath.TryGetValue(uuid, out var p) ? p : "none";
            yield return $"fru.suppression.selectionChange.final.vfx{vfxIndex}="
                + $"uuid={uuid} change={kv.Value} pathHash={hash:x8} path={path} "
                + $"baseActive={(b.Active ? 1 : 0)} baseLoaded={(b.Loaded ? 1 : 0)} baseGfx={(b.Gfx ? 1 : 0)} "
                + $"curActive={(c.Active ? 1 : 0)} curLoaded={(c.Loaded ? 1 : 0)} curGfx={(c.Gfx ? 1 : 0)}";
            vfxIndex++;
        }
    }

    public IEnumerable<string> BuildBgPartDiagnosticLines()
    {
        return _bgPart.BuildDiagnosticLines();
    }

    private void ResetObservation()
    {
        WindowComplete = false;
        _sessionEnded = false;
        Outcome = "not-run";
        ReadFailureCount = 0;
        GateBlockedPassCount = 0;
        SharedGroupPathCapReached = false;
        SharedGroupValidPassCount = 0;
        VfxValidPassCount = 0;
        SharedGroupBaselineAvailable = false;
        VfxBaselineAvailable = false;
        _passReadFailed = false;
        _baseline.Clear();
        _tracking.Clear();
        _passCounts.Clear();
        _vfxBaseline.Clear();
        _vfxCurrent.Clear();
        _vfxChange.Clear();
        _vfxPathHash.Clear();
        _vfxPath.Clear();
        _bgPart.Reset();
    }

    public void Reset()
    {
        Armed = false;
        ResetObservation();
        _latestOrdinaryPassCounts.Clear();
        _latestOrdinaryPassValid = false;
    }
}
