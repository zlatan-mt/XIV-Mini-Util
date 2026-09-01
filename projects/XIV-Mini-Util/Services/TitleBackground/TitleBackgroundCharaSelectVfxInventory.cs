// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectVfxInventory.cs
// Description: FRU クリア後ステージ candidate 固有の、read-only な InstanceType.Vfx インベントリの
//              純ロジック（gate 判定 / stable identity / path hash / 代表サンプル選定）と、
//              scene-generation 単位で bounded な run-scoped 状態。auto-copy report が参照する。
// Reason: FRU 本来の ambient VFX（花びら等）が clear-stage で欠けている可能性の切り分けに、
//         現行 API で安全に読める最小限の VFX 情報を OneClick レポートへ compact に載せる。
//         このファイルは write を一切持たない（Checkpoint 1: VFX restore は実装しない）。
//         gate は既存 FRU static-anchor authorization / scene generation / loaded ActiveLayout
//         厳密確認と同等。native pointer は呼び出し側の 1 パス内でだけ使い保持しない。
//         generic VFX/layout framework は作らない。raw memory offset を新規に読まない
//         （PathCrc は現行 ILayoutInstance に型付きで公開されていないため、安全に取得した
//         primary path 文字列の managed hash を代替 identity として出す）。
namespace XivMiniUtil.Services.TitleBackground;

// VFX インベントリ収集を許可するか（fail-closed）。順序は MaintainFruVfxInventory の逐次 gate と一致。
internal enum TitleBackgroundVfxInventoryGate
{
    Collect,
    NotFruCandidate,
    PostLogin,
    SessionOrHookNotReady,
    NotCharaSelectMap,
    SceneGenerationNotObserved,
    SceneNotAuthorized,
    SceneGenerationMismatch,
    ActiveLayoutNotReady,
    LoadedLayoutTerritoryMismatch,
    LoadedLayoutLayerMismatch,
}

internal readonly record struct TitleBackgroundVfxInventoryGateResult(TitleBackgroundVfxInventoryGate Gate)
{
    public bool ShouldCollect => Gate == TitleBackgroundVfxInventoryGate.Collect;

    public string Reason => Gate switch
    {
        TitleBackgroundVfxInventoryGate.Collect => "authorized",
        TitleBackgroundVfxInventoryGate.NotFruCandidate => "not-fru-candidate",
        TitleBackgroundVfxInventoryGate.PostLogin => "post-login",
        TitleBackgroundVfxInventoryGate.SessionOrHookNotReady => "session-or-hook-not-ready",
        TitleBackgroundVfxInventoryGate.NotCharaSelectMap => "not-chara-select-map",
        TitleBackgroundVfxInventoryGate.SceneGenerationNotObserved => "scene-generation-not-observed",
        TitleBackgroundVfxInventoryGate.SceneNotAuthorized => "scene-not-authorized",
        TitleBackgroundVfxInventoryGate.SceneGenerationMismatch => "scene-generation-mismatch",
        TitleBackgroundVfxInventoryGate.ActiveLayoutNotReady => "active-layout-not-ready",
        TitleBackgroundVfxInventoryGate.LoadedLayoutTerritoryMismatch => "loaded-layout-territory-mismatch",
        TitleBackgroundVfxInventoryGate.LoadedLayoutLayerMismatch => "loaded-layout-layer-mismatch",
        _ => "unknown",
    };
}

internal static class TitleBackgroundCharaSelectVfxInventoryLogic
{
    // 期待 ActiveLayout 初期化完了値（既存 static-anchor / suppression と同一。fail-closed）。
    public const int RequiredInitState = 7;

    // read-only インベントリ収集の許可判定。すべて成立したときだけ Collect。
    // 判定は純粋（native アクセスなし）。呼び出し側が同一 frame で読んだ値を渡す。
    public static TitleBackgroundVfxInventoryGateResult Evaluate(
        bool candidateIsFru,
        bool isLoggedIn,
        bool charaSelectTitleBackgroundSessionActive,
        bool hookReady,
        bool currentLobbyMapIsCharaSelect,
        int sceneGeneration,
        bool staticAnchorAuthorized,
        int authorizedAnchorSceneGeneration,
        bool activeLayoutAvailable,
        int activeLayoutInitState,
        uint loadedLayoutTerritoryTypeId,
        uint loadedLayoutLayerFilterKey,
        uint candidateTerritoryId,
        uint candidateLayerFilterKey)
    {
        if (!candidateIsFru)
        {
            return new(TitleBackgroundVfxInventoryGate.NotFruCandidate);
        }

        if (isLoggedIn)
        {
            return new(TitleBackgroundVfxInventoryGate.PostLogin);
        }

        if (!charaSelectTitleBackgroundSessionActive || !hookReady)
        {
            return new(TitleBackgroundVfxInventoryGate.SessionOrHookNotReady);
        }

        if (!currentLobbyMapIsCharaSelect)
        {
            return new(TitleBackgroundVfxInventoryGate.NotCharaSelectMap);
        }

        if (sceneGeneration <= 0)
        {
            return new(TitleBackgroundVfxInventoryGate.SceneGenerationNotObserved);
        }

        if (!staticAnchorAuthorized)
        {
            return new(TitleBackgroundVfxInventoryGate.SceneNotAuthorized);
        }

        if (authorizedAnchorSceneGeneration != sceneGeneration)
        {
            return new(TitleBackgroundVfxInventoryGate.SceneGenerationMismatch);
        }

        // fail-closed: TerritoryTypeId==0 も不一致扱い（activeLayoutAvailable=false 相当を含む）。
        if (!activeLayoutAvailable || activeLayoutInitState != RequiredInitState)
        {
            return new(TitleBackgroundVfxInventoryGate.ActiveLayoutNotReady);
        }

        if (loadedLayoutTerritoryTypeId != candidateTerritoryId)
        {
            return new(TitleBackgroundVfxInventoryGate.LoadedLayoutTerritoryMismatch);
        }

        if (loadedLayoutLayerFilterKey != candidateLayerFilterKey)
        {
            return new(TitleBackgroundVfxInventoryGate.LoadedLayoutLayerMismatch);
        }

        return new(TitleBackgroundVfxInventoryGate.Collect);
    }

    // 安全に取得した primary path 文字列の deterministic な managed hash（FNV-1a 32bit）。
    // 現行 API に型付き PathCrc が無いための代替 stable identity。raw memory は読まない。
    public static uint HashPath(string? primaryPath)
    {
        var path = (primaryPath ?? string.Empty).Trim().ToLowerInvariant();
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var c in path)
        {
            hash = (hash ^ c) * prime;
        }

        return hash;
    }

    // stable identity: instance map key の上位 32bit（instance id 相当）と SubId を連結した文字列。
    // suppression の suppressedKey 形式（"<type>:<hi>"）と整合させ、raw pointer は出さない。
    public static string BuildStableIdentity(ulong instanceMapKey, uint subId)
    {
        var hi = (uint)(instanceMapKey >> 32);
        return $"{hi}.{subId}";
    }

    // 代表サンプル 1 件の compact 表現。原因判定に必要な最小フィールドだけ。
    // 例: "k=123.0 a=1 l=1 g=1 h=1a2b3c4d p=bg/ex3/.../xxx.avfx"
    public static string FormatRepresentative(
        ulong instanceMapKey,
        uint subId,
        bool isActive,
        bool isPrimaryLoaded,
        bool hasGraphicsObject,
        uint pathHash,
        string? primaryPath,
        int maxPathLength)
    {
        var identity = BuildStableIdentity(instanceMapKey, subId);
        var path = (primaryPath ?? string.Empty).Trim();
        if (path.Length == 0)
        {
            path = "none";
        }
        else if (maxPathLength > 0 && path.Length > maxPathLength)
        {
            path = string.Concat("…", path.AsSpan(path.Length - maxPathLength));
        }

        return $"k={identity} a={(isActive ? 1 : 0)} l={(isPrimaryLoaded ? 1 : 0)} "
            + $"g={(hasGraphicsObject ? 1 : 0)} h={pathHash:x8} p={path}";
    }
}

// scene-generation 単位で bounded な read-only インベントリの run-scoped 状態。
// candidate を跨いで永続化しない。write は 1 件も行わない（fru.vfx.writes は常に 0）。
internal sealed class TitleBackgroundCharaSelectVfxInventoryRuntimeState
{
    // レポート本文へ載せる代表サンプル上限（bounded。数百件の raw dump は入れない）。
    public const int MaxRepresentatives = 8;

    // 1 パスあたりの走査上限（read-only だが暴走を防ぐ安全弁）。
    public const int MaxScanPerPass = 4096;

    // 代表サンプルの primary path 表示の最大長。
    public const int RepresentativePathMaxLength = 72;

    // 連続でカウントが一致したら「安定」とみなして window を閉じるまでのパス数。
    public const int StablePassTarget = 3;

    // 1 scene generation あたりの走査パス上限（安定しなくても閉じる。≈ 5 秒 @ 60fps）。
    public const int MaxPassesPerGeneration = 300;

    // auto-copy report の allowlist と emitter の単一ソース（skill §3: 実装と allowlist の乖離防止）。
    public static readonly string[] DiagnosticKeys =
    [
        "fru.vfx.candidate",
        "fru.vfx.applicable",
        "fru.vfx.attempted",
        "fru.vfx.armedSceneGeneration",
        "fru.vfx.passCount",
        "fru.vfx.completed",
        "fru.vfx.stopReason",
        "fru.vfx.lastGateStatus",
        "fru.vfx.firstFailureReason",
        "fru.vfx.writes",
        "fru.vfx.totalCount",
        "fru.vfx.activeCount",
        "fru.vfx.inactiveCount",
        "fru.vfx.primaryPathCount",
        "fru.vfx.primaryLoadedCount",
        "fru.vfx.graphicsObjectCount",
        "fru.vfx.readFailureCount",
        "fru.vfx.representativeCount",
        "fru.vfx.rep0",
        "fru.vfx.rep1",
        "fru.vfx.rep2",
        "fru.vfx.rep3",
        "fru.vfx.rep4",
        "fru.vfx.rep5",
        "fru.vfx.rep6",
        "fru.vfx.rep7",
    ];

    public bool Attempted { get; private set; }

    public int ArmedSceneGeneration { get; private set; } = -1;

    public int PassCount { get; private set; }

    public bool Completed { get; private set; }

    public string StopReason { get; private set; } = "not-run";

    // pass を実行できなかった frame の理由（scene 初期化待ち等。failure ではない）。
    public string LastGateStatus { get; private set; } = "not-run";

    // pass 実行中の実障害（例外等）だけを記録する。generation 変更でリセットする。
    public string FirstFailureReason { get; private set; } = "none";

    // --- 最新パスのスナップショット ---
    public int TotalCount { get; private set; }

    public int ActiveCount { get; private set; }

    public int InactiveCount { get; private set; }

    public int PrimaryPathCount { get; private set; }

    public int PrimaryLoadedCount { get; private set; }

    public int GraphicsObjectCount { get; private set; }

    public int ReadFailureCount { get; private set; }

    public int RepresentativeCount => _representatives.Count;

    private readonly List<string> _representatives = [];
    private readonly List<string> _repBuffer = [];
    private int _scannedThisPass;
    private int _prevTotalCount = -1;
    private int _prevActiveCount = -1;
    private int _stableStreak;

    // 指定 scene generation 用に window を arm する。generation が変わったら（または forceReArm）
    // window 状態を generation-scoped failure も含めてリセットする。
    public void ArmForGeneration(int sceneGeneration, bool forceReArm = false)
    {
        Attempted = true;
        if (!forceReArm && sceneGeneration == ArmedSceneGeneration)
        {
            return;
        }

        ArmedSceneGeneration = sceneGeneration;
        PassCount = 0;
        Completed = false;
        StopReason = "running";
        FirstFailureReason = "none";
        TotalCount = 0;
        ActiveCount = 0;
        InactiveCount = 0;
        PrimaryPathCount = 0;
        PrimaryLoadedCount = 0;
        GraphicsObjectCount = 0;
        ReadFailureCount = 0;
        _scannedThisPass = 0;
        _prevTotalCount = -1;
        _prevActiveCount = -1;
        _stableStreak = 0;
        _representatives.Clear();
        _repBuffer.Clear();
    }

    public bool ShouldRunPass() => !Completed;

    public void BeginPass()
    {
        PassCount++;
        TotalCount = 0;
        ActiveCount = 0;
        InactiveCount = 0;
        PrimaryPathCount = 0;
        PrimaryLoadedCount = 0;
        GraphicsObjectCount = 0;
        ReadFailureCount = 0;
        _scannedThisPass = 0;
        _repBuffer.Clear();
    }

    // このパスでさらに走査してよいか（read-only の安全弁）。
    public bool CanScanMore() => _scannedThisPass < MaxScanPerPass;

    // VFX instance 1 件を読めた。
    public void RecordInstance(bool isActive, bool hasPrimaryPath, bool isPrimaryLoaded, bool hasGraphicsObject)
    {
        _scannedThisPass++;
        TotalCount++;
        if (isActive)
        {
            ActiveCount++;
        }
        else
        {
            InactiveCount++;
        }

        if (hasPrimaryPath)
        {
            PrimaryPathCount++;
        }

        if (isPrimaryLoaded)
        {
            PrimaryLoadedCount++;
        }

        if (hasGraphicsObject)
        {
            GraphicsObjectCount++;
        }
    }

    // instance を評価できなかった（例外等）。read 失敗としてのみ計上する。
    public void RecordReadFailure(string reason)
    {
        _scannedThisPass++;
        ReadFailureCount++;
        RecordFailure(reason);
    }

    // 代表サンプル候補を bounded に積む。primary path 有りを優先し、次に active を優先する。
    public void OfferRepresentative(string formatted, bool hasPrimaryPath, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(formatted))
        {
            return;
        }

        if (_repBuffer.Count < MaxRepresentatives)
        {
            _repBuffer.Add(formatted);
            return;
        }

        // buffer 満杯: 優先度の低い既存要素を 1 件だけ置換する（path 無し > path 有り・inactive）。
        var replaceIndex = -1;
        for (var i = 0; i < _repBuffer.Count; i++)
        {
            var existing = _repBuffer[i];
            var existingHasPath = !existing.Contains("p=none", StringComparison.Ordinal);
            var existingActive = existing.Contains("a=1", StringComparison.Ordinal);
            var existingScore = (existingHasPath ? 2 : 0) + (existingActive ? 1 : 0);
            var candidateScore = (hasPrimaryPath ? 2 : 0) + (isActive ? 1 : 0);
            if (candidateScore > existingScore)
            {
                replaceIndex = i;
                break;
            }
        }

        if (replaceIndex >= 0)
        {
            _repBuffer[replaceIndex] = formatted;
        }
    }

    // パス終了時に window を閉じるべきか判定する。
    // - 連続 StablePassTarget パスで totalCount / activeCount が一致 -> stable。
    // - PassCount が上限 -> pass-budget-exhausted（failure ではない。read-only なので情報は取れている）。
    public void EndPass()
    {
        if (Completed)
        {
            return;
        }

        // 最新パスの代表サンプルで置き換える（latest pass wins）。
        _representatives.Clear();
        _representatives.AddRange(_repBuffer);

        if (_prevTotalCount == TotalCount && _prevActiveCount == ActiveCount)
        {
            _stableStreak++;
        }
        else
        {
            _stableStreak = 0;
        }

        _prevTotalCount = TotalCount;
        _prevActiveCount = ActiveCount;

        if (_stableStreak >= StablePassTarget)
        {
            Completed = true;
            StopReason = "stable";
            return;
        }

        if (PassCount >= MaxPassesPerGeneration)
        {
            Completed = true;
            StopReason = "pass-budget-exhausted";
        }
    }

    public void RecordGateStatus(string status)
    {
        LastGateStatus = string.IsNullOrWhiteSpace(status) ? LastGateStatus : status;
    }

    public void RecordFailure(string reason)
    {
        if (string.Equals(FirstFailureReason, "none", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(reason))
        {
            FirstFailureReason = reason;
        }
    }

    public void Reset()
    {
        Attempted = false;
        ArmedSceneGeneration = -1;
        PassCount = 0;
        Completed = false;
        StopReason = "not-run";
        LastGateStatus = "not-run";
        FirstFailureReason = "none";
        TotalCount = 0;
        ActiveCount = 0;
        InactiveCount = 0;
        PrimaryPathCount = 0;
        PrimaryLoadedCount = 0;
        GraphicsObjectCount = 0;
        ReadFailureCount = 0;
        _scannedThisPass = 0;
        _prevTotalCount = -1;
        _prevActiveCount = -1;
        _stableStreak = 0;
        _representatives.Clear();
        _repBuffer.Clear();
    }

    public IEnumerable<string> BuildDiagnosticLines(string candidateId, bool candidateIsFru)
    {
        yield return $"fru.vfx.candidate={Normalize(candidateId)}";
        yield return $"fru.vfx.applicable={candidateIsFru}";
        yield return $"fru.vfx.attempted={Attempted}";
        yield return $"fru.vfx.armedSceneGeneration={ArmedSceneGeneration}";
        yield return $"fru.vfx.passCount={PassCount}";
        yield return $"fru.vfx.completed={Completed}";
        yield return $"fru.vfx.stopReason={Normalize(StopReason)}";
        yield return $"fru.vfx.lastGateStatus={Normalize(LastGateStatus)}";
        yield return $"fru.vfx.firstFailureReason={Normalize(FirstFailureReason)}";
        // Checkpoint 1 の不変条件: このパスでは VFX write を 1 件も行わない。
        yield return "fru.vfx.writes=0";
        yield return $"fru.vfx.totalCount={TotalCount}";
        yield return $"fru.vfx.activeCount={ActiveCount}";
        yield return $"fru.vfx.inactiveCount={InactiveCount}";
        yield return $"fru.vfx.primaryPathCount={PrimaryPathCount}";
        yield return $"fru.vfx.primaryLoadedCount={PrimaryLoadedCount}";
        yield return $"fru.vfx.graphicsObjectCount={GraphicsObjectCount}";
        yield return $"fru.vfx.readFailureCount={ReadFailureCount}";
        yield return $"fru.vfx.representativeCount={RepresentativeCount}";
        for (var i = 0; i < MaxRepresentatives; i++)
        {
            var value = i < _representatives.Count ? _representatives[i] : "none";
            yield return $"fru.vfx.rep{i}={value}";
        }
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }
}
