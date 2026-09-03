// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectSceneObjectSuppression.cs
// Description: FRU クリア後ステージ candidate 固有の、戦闘用 gimmick / telegraph object を
//              n4gw scene から抑止するための小さな allow/deny テーブルと判定ロジック、
//              scene-generation 単位の bounded write window の run-scoped 状態。
// Reason: n4gw を直接ロードすると FRU 戦闘中の gimmick / magic circle / ice / telegraph SharedGroup が
//         多数表示される（実機確認済み）。clear-stage の床・花畑・遠景・照明は残し、fight-specific な
//         SharedGroup instance だけを primary path token で candidate-specific に抑止する。
//         VFX 型 instance は現行 API で active 切替の semantic が未確認のため対象外
//         （telegraph の大半は sgvf_* SharedGroup として梱包されており SharedGroup 経路で足りる）。
//         generic Active/Inactive replay / generic VFX framework は作らない。native write は
//         scene-generation 単位の有限 window + per-instance retry budget で bounded にする。
namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundSceneObjectSuppressionVerdict
{
    // 対象外（判定しない）。
    Skip,
    // clear-stage に必要なので残す。
    Keep,
    // fight-specific なので抑止する。
    Suppress,
}

internal readonly record struct TitleBackgroundSceneObjectSuppressionDecision(
    TitleBackgroundSceneObjectSuppressionVerdict Verdict,
    string Reason);

internal static class TitleBackgroundCharaSelectSceneObjectSuppressionLogic
{
    // installed n4gw game-data（bg.lgb / planmap.lgb / vfx.lgb）解析で、fight-specific と判別できた
    // SharedGroup の primary path token。KEEP token を含む path はここに一致しても残す。
    // - _gmc  : sgbg_n4gw_a{2,3,4,6}_gmc* / sgbg_n4gc_a2_gmc19 / sgbg_n4g8_a1_gmc12（戦闘 gimmick）
    // - _ice0 : sgbg_n4gw_a3_ice01（氷ギミック）
    // - _mag0 : sgbg_n4gw_a6_mag00（魔法陣）
    // - _dst0 : sgbg_n4gw_a6_dst00（破壊演出デブリ）
    // - sgvf_w_lvd_b : bgcommon LVD telegraph VFX 共有グループ（b0095/b1843..b1846）
    // - sgvf_n4gw_b35 : n4gw 戦闘 VFX 共有グループ（b3557/b3558/b3559/b3561）
    public static readonly string[] DenyPathTokens =
    [
        "_gmc",
        "_ice0",
        "_mag0",
        "_dst0",
        "sgvf_w_lvd_b",
        "sgvf_n4gw_b35",
    ];

    // clear-stage の見た目に必要なので、deny token に一致しても絶対に抑止しない path token。
    // 床（_flo）・花畑と遠景（_plt / _tree / _enkei / _a8_）・照明（_lig / _light）。
    public static readonly string[] KeepPathTokens =
    [
        "_flo",
        "_plt",
        "_tree",
        "_enkei",
        "_a8_",
        "_lig",
        "_light",
    ];

    // path と instance type から抑止すべきかを判定する。SharedGroup 以外は Skip。
    public static TitleBackgroundSceneObjectSuppressionDecision Evaluate(string? primaryPath, bool isSharedGroup)
    {
        if (!isSharedGroup)
        {
            return new(TitleBackgroundSceneObjectSuppressionVerdict.Skip, "not-shared-group");
        }

        var path = (primaryPath ?? string.Empty).Trim().ToLowerInvariant();
        if (path.Length == 0)
        {
            return new(TitleBackgroundSceneObjectSuppressionVerdict.Skip, "no-primary-path");
        }

        var denyToken = MatchToken(path, DenyPathTokens);
        if (denyToken == null)
        {
            return new(TitleBackgroundSceneObjectSuppressionVerdict.Keep, "no-deny-token");
        }

        var keepToken = MatchToken(path, KeepPathTokens);
        if (keepToken != null)
        {
            return new(TitleBackgroundSceneObjectSuppressionVerdict.Keep, $"keep-token:{keepToken}");
        }

        return new(TitleBackgroundSceneObjectSuppressionVerdict.Suppress, $"deny-token:{denyToken}");
    }

    private static string? MatchToken(string loweredPath, string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (loweredPath.Contains(token, StringComparison.Ordinal))
            {
                return token;
            }
        }

        return null;
    }

    // Phase A: 1 回の代表的なキャラ/ワールド選択変更で観測した抑止ウィンドウの証拠から、
    // transient geometry を timing-gap / coverage-gap / deactivation-semantics / insufficient-evidence に
    // 分類する純粋関数。実機の目視 symptom と突き合わせる前提の best-guess であり、fix は選ばない。
    // 「イベント -> 最初の抑止パス」がこの ms 以内かつ layout gate による blocked frame が 0 なら
    // prompt（遅延なし）とみなす。60fps で約 15 フレーム。超過 / gate 待ちがあれば timing-gap 側の証拠。
    public const long PromptFirstPassMsThreshold = 250;

    public static (TitleBackgroundSceneObjectSelectionChangeClass Class, string Reason) ClassifySelectionChange(
        in TitleBackgroundSceneObjectSelectionChangeEvidence e)
    {
        if (!e.SelectionChangeObserved || !e.FirstReArmedPassCaptured || e.EventToFirstPassMs < 0)
        {
            return (TitleBackgroundSceneObjectSelectionChangeClass.InsufficientEvidence,
                "no-re-armed-pass-captured");
        }

        var prompt = e.EventToFirstPassMs <= PromptFirstPassMsThreshold
            && e.BlockedFramesBeforeFirstPass == 0;

        if (e.FirstPassMatchedActiveBeforeWrite > 0)
        {
            // deny set が既にカバーする SharedGroup が、選択変更直後の最初のパスで active だった。
            if (!prompt)
            {
                return (TitleBackgroundSceneObjectSelectionChangeClass.TimingGap,
                    $"deny-covered-group-active-after-delay:{e.EventToFirstPassMs}ms:blocked{e.BlockedFramesBeforeFirstPass}:{Normalize(e.FirstBlockingGate)}");
            }

            // 遅延なしで走査でき、その場で SetActive(false) -> readback inactive まで確定したのに
            // まだ見える -> ゲーム側の fade / teardown 区間（現行 API では消せない可能性）。
            if (e.FirstPassConfirmedInactive > 0 && e.FirstPassStillActive == 0)
            {
                return (TitleBackgroundSceneObjectSelectionChangeClass.DeactivationSemantics,
                    "deny-covered-group-suppressed-and-confirmed-inactive-in-first-pass");
            }

            return (TitleBackgroundSceneObjectSelectionChangeClass.TimingGap,
                "deny-covered-group-active-not-confirmed-inactive-in-first-pass");
        }

        // deny set に該当する active group は無かった。deny token 非該当（"no-deny-token"）で active な
        // 構造 SharedGroup を最初のパスで観測していれば、それが未カバーの大型ジオメトリの候補。
        if (e.ActiveNonDenyKeepPathSampleCount > 0)
        {
            return (TitleBackgroundSceneObjectSelectionChangeClass.CoverageGap,
                $"active-non-deny-sharedgroups:{e.ActiveNonDenyKeepPathSampleCount}");
        }

        return (TitleBackgroundSceneObjectSelectionChangeClass.InsufficientEvidence,
            "no-active-deny-or-non-deny-sharedgroup-associated-with-switch");
    }

    internal static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }
}

// Phase A の選択変更分類（fix は選ばない。evidence の要約のみ）。
internal enum TitleBackgroundSceneObjectSelectionChangeClass
{
    InsufficientEvidence,
    TimingGap,
    CoverageGap,
    DeactivationSemantics,
}

// ClassifySelectionChange の入力。すべて非永続の診断値（scene generation / monotonic ms / count /
// gate 文字列 / game-asset path count）で、character/account データや raw pointer は含まない。
internal readonly record struct TitleBackgroundSceneObjectSelectionChangeEvidence(
    bool SelectionChangeObserved,
    long EventToFirstPassMs,
    int BlockedFramesBeforeFirstPass,
    string FirstBlockingGate,
    bool FirstReArmedPassCaptured,
    int FirstPassMatchedActiveBeforeWrite,
    int FirstPassConfirmedInactive,
    int FirstPassStillActive,
    int ActiveNonDenyKeepPathSampleCount,
    string WindowStopReason);

// scene-generation 単位の bounded write window の run-scoped 状態。auto-copy report が参照する。
// candidate を跨いで永続化しない。
internal sealed class TitleBackgroundCharaSelectSceneObjectSuppressionRuntimeState
{
    // 1 instance あたりの SetActive(false) 呼び出し上限。ゲームが再 active 化しても無制限に書かない。
    public const int WriteBudgetPerInstance = 8;

    // stable 判定に必要な「全 match が非 active」連続パス数。1 パスの偶発的 readback 成功では終了しない。
    public const int StableStreakTarget = 5;

    // matched が一度も出ない場合に「抑止対象なし」で window を閉じるまでの猶予パス数（≈ 2 秒 @ 60fps）。
    public const int NoMatchGracePasses = 120;

    // 1 scene generation あたりの走査パス上限（安定しなければ window を閉じる。≈ 10 秒 @ 60fps）。
    public const int MaxPassesPerGeneration = 600;

    public const string VfxMode = "excluded-semantics-unverified";

    // 最初の re-arm パスで採取する「deny token 非該当かつ active」な SharedGroup の
    // game-asset primary path サンプル上限（coverage-gap 判定用。write は一切しない）。
    public const int MaxActiveNonDenyKeepPathSamples = 12;

    // auto-copy report の allowlist と emitter の単一ソース（skill §3: 実装と allowlist の乖離防止）。
    // BuildDiagnosticLines が出す静的キーはすべてここに列挙する。
    public static readonly string[] DiagnosticKeys =
    [
        "fru.suppression.candidate",
        "fru.suppression.applicable",
        "fru.suppression.attempted",
        "fru.suppression.armedSceneGeneration",
        "fru.suppression.passCount",
        "fru.suppression.stableStreak",
        "fru.suppression.stableStreakTarget",
        "fru.suppression.everMatched",
        "fru.suppression.targetInstanceCount",
        "fru.suppression.matchedCount",
        "fru.suppression.alreadyInactiveCount",
        "fru.suppression.writeAttemptedInstanceCount",
        "fru.suppression.totalWriteCalls",
        "fru.suppression.confirmedInactiveCount",
        "fru.suppression.stillActiveCount",
        "fru.suppression.budgetExhaustedCount",
        "fru.suppression.writeBudgetPerInstance",
        "fru.suppression.completed",
        "fru.suppression.stopReason",
        "fru.suppression.terminalAtPassCount",
        "fru.suppression.vfxMode",
        "fru.suppression.denyTokenCount",
        "fru.suppression.keepTokenCount",
        "fru.suppression.lastGateStatus",
        "fru.suppression.firstFailureReason",
        "fru.suppression.cleanupState",
        "fru.suppression.suppressedKeys",
        // Phase A: 1 回の代表的なキャラ/ワールド選択変更の分類証拠。
        "fru.suppression.selectionChange.reArmCount",
        "fru.suppression.selectionChange.generationAtEvent",
        "fru.suppression.selectionChange.generationAtReArm",
        "fru.suppression.selectionChange.eventToReArmMs",
        "fru.suppression.selectionChange.eventToFirstPassMs",
        "fru.suppression.selectionChange.blockedFramesBeforeFirstPass",
        "fru.suppression.selectionChange.firstBlockingGate",
        "fru.suppression.selectionChange.firstPassCaptured",
        "fru.suppression.selectionChange.firstPassMatched",
        "fru.suppression.selectionChange.firstPassMatchedActiveBeforeWrite",
        "fru.suppression.selectionChange.firstPassAlreadyInactive",
        "fru.suppression.selectionChange.firstPassWrites",
        "fru.suppression.selectionChange.firstPassConfirmedInactive",
        "fru.suppression.selectionChange.firstPassStillActive",
        "fru.suppression.selectionChange.activeNonDenyKeepSampleCount",
        "fru.suppression.selectionChange.activeNonDenyKeepPaths",
        "fru.suppression.selectionChange.class",
        "fru.suppression.selectionChange.classReason",
    ];

    public bool Attempted { get; private set; }

    public int ArmedSceneGeneration { get; private set; } = -1;

    public int PassCount { get; private set; }

    public int StableStreak { get; private set; }

    public bool EverMatched { get; private set; }

    // --- 最新パスのスナップショット ---
    public int TargetInstanceCount { get; private set; }

    public int MatchedCount { get; private set; }

    public int AlreadyInactiveCount { get; private set; }

    public int ConfirmedInactiveCount { get; private set; }

    public int StillActiveCount { get; private set; }

    public int BudgetExhaustedCount { get; private set; }

    // --- window 累積 ---
    public int WriteAttemptedInstanceCount => _writeAttempts.Count;

    public int TotalWriteCalls { get; private set; }

    public bool Completed { get; private set; }

    public string StopReason { get; private set; } = "not-run";

    // pass を実行できなかった frame の理由（scene 初期化待ち等。failure ではない）。
    public string LastGateStatus { get; private set; } = "not-run";

    // pass 実行中の実障害（例外・budget 枯渇）だけを記録する。generation 変更でリセットする。
    public string FirstFailureReason { get; private set; } = "none";

    public string CleanupState { get; private set; } = "not-run";

    // window が terminal（stable / no-matched-instances / budget-exhausted）へ到達した pass 番号。
    // 未到達なら -1。generation / forceReArm の再 arm でリセットする。
    public int TerminalAtPassCount { get; private set; } = -1;

    // --- Phase A: 1 回の代表的なキャラ/ワールド選択変更の再 arm 証拠（診断専用・非永続） ---
    public int SelectionChangeReArmCount { get; private set; }

    public int SelectionChangeGenerationAtEvent { get; private set; } = -1;

    public int SelectionChangeGenerationAtReArm { get; private set; } = -1;

    // OnCharaSelectSelectionChanged が publish した monotonic tick（ms）。0 = 未観測。
    public long SelectionChangeEventTickMs { get; private set; }

    public long SelectionChangeEventToReArmMs { get; private set; } = -1;

    public long SelectionChangeEventToFirstPassMs { get; private set; } = -1;

    // 再 arm 後、最初の実パス（BeginPass）までに gate で弾かれた Framework フレーム数と、最初の gate 理由。
    public int SelectionChangeBlockedFramesBeforeFirstPass { get; private set; }

    public string SelectionChangeFirstBlockingGate { get; private set; } = "none";

    public bool AwaitingFirstReArmedPass { get; private set; }

    public bool CapturingFirstReArmedPass => _capturingFirstReArmedPass;

    // 最初の re-arm パスのスナップショット。
    public bool FirstReArmedPassCaptured { get; private set; }

    public int FirstReArmedPassMatched { get; private set; }

    public int FirstReArmedPassMatchedActiveBeforeWrite { get; private set; }

    public int FirstReArmedPassAlreadyInactive { get; private set; }

    public int FirstReArmedPassWrites { get; private set; }

    public int FirstReArmedPassConfirmedInactive { get; private set; }

    public int FirstReArmedPassStillActive { get; private set; }

    public int ActiveNonDenyKeepPathSampleCount => _activeNonDenyKeepPaths.Count;

    public IReadOnlyList<string> ActiveNonDenyKeepPaths => _activeNonDenyKeepPaths;

    private bool _capturingFirstReArmedPass;
    private int _passWrites;
    private readonly List<string> _activeNonDenyKeepPaths = [];

    private readonly List<string> _suppressedKeys = [];
    private readonly Dictionary<ulong, int> _writeAttempts = [];

    // このパスの matched instance 数と、うち「pass 開始時点で既に inactive」だった数。
    // stable streak は「matched>0 かつ 全 matched が開始時 inactive かつ dirty でない」パスだけ進める。
    // write を1回でも行った / まだ active / 例外・unknown があれば dirty（このパスは stable にしない）。
    private int _passMatched;
    private int _passAlreadyInactive;
    private bool _passDirty;

    public IReadOnlyList<string> SuppressedKeys => _suppressedKeys;

    // 指定 scene generation 用に window を arm する。generation が変わったら（または forceReArm）
    // window 状態を generation-scoped failure / cleanup も含めてリセットする。
    public void ArmForGeneration(int sceneGeneration, bool forceReArm = false)
    {
        Attempted = true;
        if (!forceReArm && sceneGeneration == ArmedSceneGeneration)
        {
            return;
        }

        var isNewGeneration = sceneGeneration != ArmedSceneGeneration;
        ArmedSceneGeneration = sceneGeneration;
        PassCount = 0;
        StableStreak = 0;
        EverMatched = false;
        TargetInstanceCount = 0;
        MatchedCount = 0;
        AlreadyInactiveCount = 0;
        ConfirmedInactiveCount = 0;
        StillActiveCount = 0;
        BudgetExhaustedCount = 0;
        TotalWriteCalls = 0;
        Completed = false;
        StopReason = "running";
        FirstFailureReason = "none";
        CleanupState = "not-run";
        TerminalAtPassCount = -1;
        _passMatched = 0;
        _passAlreadyInactive = 0;
        _passDirty = false;
        _passWrites = 0;
        _suppressedKeys.Clear();
        _writeAttempts.Clear();

        // 新しい scene generation で re-arm したときは、前 scene の選択変更証拠を持ち越さない。
        // forceReArm（同一 generation でのキャラ切替）では NoteSelectionChangeReArm が直前に
        // 今回の証拠をセット済みなので消さない。
        if (isNewGeneration && !forceReArm)
        {
            ResetSelectionChangeEvidence();
        }
    }

    // Phase A: OnCharaSelectSelectionChanged を Framework スレッドで消費した瞬間に呼ぶ。
    // ArmForGeneration(forceReArm:true) の直前に呼ぶ前提で、今回の選択変更証拠を初期化する。
    public void NoteSelectionChangeReArm(
        int generationAtEvent,
        int generationAtReArm,
        long eventTickMs,
        long eventToReArmMs)
    {
        SelectionChangeReArmCount++;
        SelectionChangeGenerationAtEvent = generationAtEvent;
        SelectionChangeGenerationAtReArm = generationAtReArm;
        SelectionChangeEventTickMs = eventTickMs;
        SelectionChangeEventToReArmMs = eventToReArmMs;
        SelectionChangeEventToFirstPassMs = -1;
        SelectionChangeBlockedFramesBeforeFirstPass = 0;
        SelectionChangeFirstBlockingGate = "none";
        AwaitingFirstReArmedPass = true;
        _capturingFirstReArmedPass = false;
        FirstReArmedPassCaptured = false;
        FirstReArmedPassMatched = 0;
        FirstReArmedPassMatchedActiveBeforeWrite = 0;
        FirstReArmedPassAlreadyInactive = 0;
        FirstReArmedPassWrites = 0;
        FirstReArmedPassConfirmedInactive = 0;
        FirstReArmedPassStillActive = 0;
        _activeNonDenyKeepPaths.Clear();
    }

    // 最初の re-arm パスの BeginPass 直前に呼ぶ（サービスが gate 通過を確認済みのとき）。
    // eventToFirstPassMs は OnCharaSelectSelectionChanged イベントからの monotonic 経過 ms。
    public void MarkFirstReArmedPassStarting(long eventToFirstPassMs)
    {
        if (!AwaitingFirstReArmedPass || _capturingFirstReArmedPass || FirstReArmedPassCaptured)
        {
            return;
        }

        _capturingFirstReArmedPass = true;
        SelectionChangeEventToFirstPassMs = eventToFirstPassMs;
    }

    // 最初の re-arm パスでだけ、deny token 非該当かつ active な SharedGroup の game-asset primary path を
    // bounded に記録する（coverage-gap 証拠）。write は一切しない。
    public void RecordActiveNonDenyKeepPath(string? primaryPath)
    {
        if (!_capturingFirstReArmedPass
            || _activeNonDenyKeepPaths.Count >= MaxActiveNonDenyKeepPathSamples)
        {
            return;
        }

        var sanitized = SanitizeGameAssetPath(primaryPath);
        if (sanitized.Length == 0 || _activeNonDenyKeepPaths.Contains(sanitized))
        {
            return;
        }

        _activeNonDenyKeepPaths.Add(sanitized);
    }

    // 診断へ出すのはゲームアセットパス（bg/ ・ bgcommon/）だけ。fail-closed で他は空にする。
    private static string SanitizeGameAssetPath(string? path)
    {
        var p = (path ?? string.Empty).Trim().ToLowerInvariant();
        if (p.Length is 0 or > 128)
        {
            return string.Empty;
        }

        return p.StartsWith("bg/", StringComparison.Ordinal)
            || p.StartsWith("bgcommon/", StringComparison.Ordinal)
                ? p
                : string.Empty;
    }

    private void ResetSelectionChangeEvidence()
    {
        SelectionChangeReArmCount = 0;
        SelectionChangeGenerationAtEvent = -1;
        SelectionChangeGenerationAtReArm = -1;
        SelectionChangeEventTickMs = 0;
        SelectionChangeEventToReArmMs = -1;
        SelectionChangeEventToFirstPassMs = -1;
        SelectionChangeBlockedFramesBeforeFirstPass = 0;
        SelectionChangeFirstBlockingGate = "none";
        AwaitingFirstReArmedPass = false;
        _capturingFirstReArmedPass = false;
        FirstReArmedPassCaptured = false;
        FirstReArmedPassMatched = 0;
        FirstReArmedPassMatchedActiveBeforeWrite = 0;
        FirstReArmedPassAlreadyInactive = 0;
        FirstReArmedPassWrites = 0;
        FirstReArmedPassConfirmedInactive = 0;
        FirstReArmedPassStillActive = 0;
        _activeNonDenyKeepPaths.Clear();
    }

    // window がまだ書込を試みてよいか（bounded）。
    public bool ShouldRunPass()
    {
        return !Completed;
    }

    public void BeginPass()
    {
        PassCount++;
        TargetInstanceCount = 0;
        MatchedCount = 0;
        AlreadyInactiveCount = 0;
        ConfirmedInactiveCount = 0;
        StillActiveCount = 0;
        BudgetExhaustedCount = 0;
        _passMatched = 0;
        _passAlreadyInactive = 0;
        _passDirty = false;
        _passWrites = 0;
    }

    public void RecordScanned()
    {
        TargetInstanceCount++;
    }

    public void RecordMatched(ulong instanceKey)
    {
        MatchedCount++;
        _passMatched++;
        EverMatched = true;
    }

    // matched instance が pass 開始時点で既に inactive だった（前パスで抑止済み or ゲーム側で非 active）。
    public void RecordAlreadyInactive(ulong instanceKey)
    {
        AlreadyInactiveCount++;
        _passAlreadyInactive++;
    }

    // per-instance の write budget を消費する。false なら予算切れ（今回は書かない）。
    public bool TryConsumeWriteBudget(ulong instanceKey)
    {
        var used = _writeAttempts.TryGetValue(instanceKey, out var n) ? n : 0;
        if (used >= WriteBudgetPerInstance)
        {
            return false;
        }

        _writeAttempts[instanceKey] = used + 1;
        TotalWriteCalls++;
        return true;
    }

    // SetActive(false) を実際に書いた。write を行ったパスは stable にしない（dirty）。
    public void RecordWriteAttempted(ulong instanceKey, string instanceType)
    {
        _passDirty = true;
        _passWrites++;
        if (_suppressedKeys.Count < 64
            && !_suppressedKeys.Exists(k => k.EndsWith($":{(uint)(instanceKey >> 32)}", StringComparison.Ordinal)))
        {
            _suppressedKeys.Add($"{instanceType}:{(uint)(instanceKey >> 32)}");
        }
    }

    // SetActive(false) 後の readback が inactive を返した（write は行った）。
    public void RecordConfirmedInactive(ulong instanceKey)
    {
        ConfirmedInactiveCount++;
    }

    // SetActive(false) を書いた（または budget 枯渇した）が、まだ active。dirty。
    public void RecordStillActive(ulong instanceKey)
    {
        StillActiveCount++;
        _passDirty = true;
    }

    public void RecordBudgetExhausted(ulong instanceKey)
    {
        BudgetExhaustedCount++;
        RecordStillActive(instanceKey);
    }

    // matched instance に対する例外・unknown 状態。dirty（このパスは stable にしない）。
    public void MarkPassDirty()
    {
        _passDirty = true;
    }

    // パス終了時に window を閉じるべきか判定する。
    // - stable streak を進めるのは「matched>0 かつ 全 matched が pass 開始時から inactive かつ
    //   このパスで write 0 回・未解決 0 件・例外/unknown なし」のパスだけ。
    //   （write して readback 成功しただけでは clean にしない。再出現ギミックをその場で消しても clean にしない。）
    // - matched が一度も出ず grace パス超過 -> no-matched-instances（failure ではない）。
    // - PassCount が上限 -> budget-exhausted（failure として report）。
    public void EndPass()
    {
        if (Completed)
        {
            return;
        }

        CaptureFirstReArmedPassIfPending();

        var passClean = _passMatched > 0
            && _passAlreadyInactive == _passMatched
            && !_passDirty;
        StableStreak = passClean ? StableStreak + 1 : 0;

        if (StableStreak >= StableStreakTarget)
        {
            Completed = true;
            StopReason = "stable";
            TerminalAtPassCount = PassCount;
            return;
        }

        if (!EverMatched && PassCount >= NoMatchGracePasses)
        {
            Completed = true;
            StopReason = "no-matched-instances";
            TerminalAtPassCount = PassCount;
            return;
        }

        if (PassCount >= MaxPassesPerGeneration)
        {
            Completed = true;
            StopReason = "budget-exhausted";
            TerminalAtPassCount = PassCount;
            RecordFailure("write-window-budget-exhausted");
        }
    }

    // 最初の re-arm パスの計数結果を 1 度だけスナップショットする（EndPass 内で呼ぶ）。
    private void CaptureFirstReArmedPassIfPending()
    {
        if (!_capturingFirstReArmedPass || FirstReArmedPassCaptured)
        {
            return;
        }

        FirstReArmedPassCaptured = true;
        FirstReArmedPassMatched = MatchedCount;
        FirstReArmedPassAlreadyInactive = AlreadyInactiveCount;
        FirstReArmedPassMatchedActiveBeforeWrite = Math.Max(0, MatchedCount - AlreadyInactiveCount);
        FirstReArmedPassWrites = _passWrites;
        FirstReArmedPassConfirmedInactive = ConfirmedInactiveCount;
        FirstReArmedPassStillActive = StillActiveCount;
        _capturingFirstReArmedPass = false;
        AwaitingFirstReArmedPass = false;
    }

    // pass を実行できなかった frame の理由（scene 初期化待ち等）。failure カウントには入れない。
    // Phase A: 選択変更で re-arm 済みかつ最初の実パス前なら、"authorized" 以外の gate を blocked frame として数える。
    public void RecordGateStatus(string status)
    {
        LastGateStatus = string.IsNullOrWhiteSpace(status) ? LastGateStatus : status;

        if (AwaitingFirstReArmedPass
            && !_capturingFirstReArmedPass
            && !FirstReArmedPassCaptured
            && !string.IsNullOrWhiteSpace(status)
            && !string.Equals(status, "authorized", StringComparison.Ordinal))
        {
            SelectionChangeBlockedFramesBeforeFirstPass++;
            if (string.Equals(SelectionChangeFirstBlockingGate, "none", StringComparison.Ordinal))
            {
                SelectionChangeFirstBlockingGate = status.Trim();
            }
        }
    }

    // pass 実行中の実障害だけを記録する（例外・budget 枯渇）。
    public void RecordFailure(string reason)
    {
        if (string.Equals(FirstFailureReason, "none", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(reason))
        {
            FirstFailureReason = reason;
        }
    }

    public void RecordCleanupState(string state)
    {
        CleanupState = string.IsNullOrWhiteSpace(state) ? CleanupState : state;
    }

    public void Reset()
    {
        Attempted = false;
        ArmedSceneGeneration = -1;
        PassCount = 0;
        StableStreak = 0;
        EverMatched = false;
        TargetInstanceCount = 0;
        MatchedCount = 0;
        AlreadyInactiveCount = 0;
        ConfirmedInactiveCount = 0;
        StillActiveCount = 0;
        BudgetExhaustedCount = 0;
        TotalWriteCalls = 0;
        Completed = false;
        StopReason = "not-run";
        LastGateStatus = "not-run";
        FirstFailureReason = "none";
        CleanupState = "not-run";
        TerminalAtPassCount = -1;
        _passMatched = 0;
        _passAlreadyInactive = 0;
        _passDirty = false;
        _passWrites = 0;
        _suppressedKeys.Clear();
        _writeAttempts.Clear();
        ResetSelectionChangeEvidence();
    }

    public IEnumerable<string> BuildDiagnosticLines(string candidateId, bool candidateIsFru)
    {
        yield return $"fru.suppression.candidate={Normalize(candidateId)}";
        yield return $"fru.suppression.applicable={candidateIsFru}";
        yield return $"fru.suppression.attempted={Attempted}";
        yield return $"fru.suppression.armedSceneGeneration={ArmedSceneGeneration}";
        yield return $"fru.suppression.passCount={PassCount}";
        yield return $"fru.suppression.stableStreak={StableStreak}";
        yield return $"fru.suppression.stableStreakTarget={StableStreakTarget}";
        yield return $"fru.suppression.everMatched={EverMatched}";
        yield return $"fru.suppression.targetInstanceCount={TargetInstanceCount}";
        yield return $"fru.suppression.matchedCount={MatchedCount}";
        yield return $"fru.suppression.alreadyInactiveCount={AlreadyInactiveCount}";
        yield return $"fru.suppression.writeAttemptedInstanceCount={WriteAttemptedInstanceCount}";
        yield return $"fru.suppression.totalWriteCalls={TotalWriteCalls}";
        yield return $"fru.suppression.confirmedInactiveCount={ConfirmedInactiveCount}";
        yield return $"fru.suppression.stillActiveCount={StillActiveCount}";
        yield return $"fru.suppression.budgetExhaustedCount={BudgetExhaustedCount}";
        yield return $"fru.suppression.writeBudgetPerInstance={WriteBudgetPerInstance}";
        yield return $"fru.suppression.completed={Completed}";
        yield return $"fru.suppression.stopReason={Normalize(StopReason)}";
        yield return $"fru.suppression.terminalAtPassCount={TerminalAtPassCount}";
        yield return $"fru.suppression.vfxMode={VfxMode}";
        yield return $"fru.suppression.denyTokenCount={TitleBackgroundCharaSelectSceneObjectSuppressionLogic.DenyPathTokens.Length}";
        yield return $"fru.suppression.keepTokenCount={TitleBackgroundCharaSelectSceneObjectSuppressionLogic.KeepPathTokens.Length}";
        yield return $"fru.suppression.lastGateStatus={Normalize(LastGateStatus)}";
        yield return $"fru.suppression.firstFailureReason={Normalize(FirstFailureReason)}";
        yield return $"fru.suppression.cleanupState={Normalize(CleanupState)}";
        yield return $"fru.suppression.suppressedKeys={(_suppressedKeys.Count == 0 ? "none" : string.Join(",", _suppressedKeys))}";

        // Phase A: 1 回の代表的なキャラ/ワールド選択変更の分類証拠。
        var evidence = new TitleBackgroundSceneObjectSelectionChangeEvidence(
            SelectionChangeObserved: SelectionChangeReArmCount > 0,
            EventToFirstPassMs: SelectionChangeEventToFirstPassMs,
            BlockedFramesBeforeFirstPass: SelectionChangeBlockedFramesBeforeFirstPass,
            FirstBlockingGate: SelectionChangeFirstBlockingGate,
            FirstReArmedPassCaptured: FirstReArmedPassCaptured,
            FirstPassMatchedActiveBeforeWrite: FirstReArmedPassMatchedActiveBeforeWrite,
            FirstPassConfirmedInactive: FirstReArmedPassConfirmedInactive,
            FirstPassStillActive: FirstReArmedPassStillActive,
            ActiveNonDenyKeepPathSampleCount: ActiveNonDenyKeepPathSampleCount,
            WindowStopReason: StopReason);
        var (selectionChangeClass, selectionChangeReason) =
            TitleBackgroundCharaSelectSceneObjectSuppressionLogic.ClassifySelectionChange(evidence);

        yield return $"fru.suppression.selectionChange.reArmCount={SelectionChangeReArmCount}";
        yield return $"fru.suppression.selectionChange.generationAtEvent={SelectionChangeGenerationAtEvent}";
        yield return $"fru.suppression.selectionChange.generationAtReArm={SelectionChangeGenerationAtReArm}";
        yield return $"fru.suppression.selectionChange.eventToReArmMs={SelectionChangeEventToReArmMs}";
        yield return $"fru.suppression.selectionChange.eventToFirstPassMs={SelectionChangeEventToFirstPassMs}";
        yield return $"fru.suppression.selectionChange.blockedFramesBeforeFirstPass={SelectionChangeBlockedFramesBeforeFirstPass}";
        yield return $"fru.suppression.selectionChange.firstBlockingGate={Normalize(SelectionChangeFirstBlockingGate)}";
        yield return $"fru.suppression.selectionChange.firstPassCaptured={FirstReArmedPassCaptured}";
        yield return $"fru.suppression.selectionChange.firstPassMatched={FirstReArmedPassMatched}";
        yield return $"fru.suppression.selectionChange.firstPassMatchedActiveBeforeWrite={FirstReArmedPassMatchedActiveBeforeWrite}";
        yield return $"fru.suppression.selectionChange.firstPassAlreadyInactive={FirstReArmedPassAlreadyInactive}";
        yield return $"fru.suppression.selectionChange.firstPassWrites={FirstReArmedPassWrites}";
        yield return $"fru.suppression.selectionChange.firstPassConfirmedInactive={FirstReArmedPassConfirmedInactive}";
        yield return $"fru.suppression.selectionChange.firstPassStillActive={FirstReArmedPassStillActive}";
        yield return $"fru.suppression.selectionChange.activeNonDenyKeepSampleCount={ActiveNonDenyKeepPathSampleCount}";
        yield return $"fru.suppression.selectionChange.activeNonDenyKeepPaths={(_activeNonDenyKeepPaths.Count == 0 ? "none" : string.Join(",", _activeNonDenyKeepPaths))}";
        yield return $"fru.suppression.selectionChange.class={selectionChangeClass}";
        yield return $"fru.suppression.selectionChange.classReason={Normalize(selectionChangeReason)}";
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }
}
