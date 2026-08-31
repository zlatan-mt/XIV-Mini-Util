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
}

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
        _passMatched = 0;
        _passAlreadyInactive = 0;
        _passDirty = false;
        _suppressedKeys.Clear();
        _writeAttempts.Clear();
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

        var passClean = _passMatched > 0
            && _passAlreadyInactive == _passMatched
            && !_passDirty;
        StableStreak = passClean ? StableStreak + 1 : 0;

        if (StableStreak >= StableStreakTarget)
        {
            Completed = true;
            StopReason = "stable";
            return;
        }

        if (!EverMatched && PassCount >= NoMatchGracePasses)
        {
            Completed = true;
            StopReason = "no-matched-instances";
            return;
        }

        if (PassCount >= MaxPassesPerGeneration)
        {
            Completed = true;
            StopReason = "budget-exhausted";
            RecordFailure("write-window-budget-exhausted");
        }
    }

    // pass を実行できなかった frame の理由（scene 初期化待ち等）。failure カウントには入れない。
    public void RecordGateStatus(string status)
    {
        LastGateStatus = string.IsNullOrWhiteSpace(status) ? LastGateStatus : status;
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
        _passMatched = 0;
        _passAlreadyInactive = 0;
        _passDirty = false;
        _suppressedKeys.Clear();
        _writeAttempts.Clear();
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
        yield return $"fru.suppression.vfxMode={VfxMode}";
        yield return $"fru.suppression.denyTokenCount={TitleBackgroundCharaSelectSceneObjectSuppressionLogic.DenyPathTokens.Length}";
        yield return $"fru.suppression.keepTokenCount={TitleBackgroundCharaSelectSceneObjectSuppressionLogic.KeepPathTokens.Length}";
        yield return $"fru.suppression.lastGateStatus={Normalize(LastGateStatus)}";
        yield return $"fru.suppression.firstFailureReason={Normalize(FirstFailureReason)}";
        yield return $"fru.suppression.cleanupState={Normalize(CleanupState)}";
        yield return $"fru.suppression.suppressedKeys={(_suppressedKeys.Count == 0 ? "none" : string.Join(",", _suppressedKeys))}";
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }
}
