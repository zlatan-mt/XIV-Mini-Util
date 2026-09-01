// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundQuickCheck.cs
// Description: Character Select 背景 QuickCheck の状態、判定、UI表示文字列をまとめる
// Reason: 累積診断に頼らず、1回の確認結果を OK/WARN/NG で出すため
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Services.TitleBackground;

public enum TitleBackgroundQuickCheckLevel
{
    NotRun,
    OK,
    WARN,
    NG,
}

public enum TitleBackgroundQuickCheckRunState
{
    Idle,
    Armed,
    CharaSelectObserved,
    LoggedInObserved,
    Completed,
    Failed,
}

public enum TitleBackgroundAutomaticCheckState
{
    Idle,
    WaitingForCharacterSelect,
    Collecting,
    Completed,
    Failed,
}

internal readonly record struct TitleBackgroundAutomaticCheckStatus(
    TitleBackgroundAutomaticCheckState State,
    string StatusLine,
    string NextActionLine,
    bool CanCopyLastReport);

internal static class TitleBackgroundAutomaticCheckLogic
{
    public static readonly TimeSpan LoginTransitionTimeout = TimeSpan.FromSeconds(10);

    public static bool ShouldForcePartialCompletion(
        TitleBackgroundAutomaticCheckState state,
        bool isLoggedIn,
        DateTimeOffset? loginObservedAt,
        DateTimeOffset now)
    {
        return state == TitleBackgroundAutomaticCheckState.Collecting
            && isLoggedIn
            && loginObservedAt.HasValue
            && now - loginObservedAt.Value >= LoginTransitionTimeout;
    }

    // 判定（Delivery / Transition）に使う sceneReady accepted 回数を解決する。
    // 自動確認は run-scoped（current run の受理回数のみ）で「複数回受理」を判定し、
    // プラグイン起動以来の累積回数だけで current run を unsafe にしない。
    // 通常の長期診断（automaticInvocation=false）は従来どおり累積値を返して傾向を残す。
    // post-login sceneReady / active override / stale state / Phase2G 漏れなどの危険判定は
    // この値に依存しない別経路なので、run-scoped 化しても弱まらない。
    public static int ResolveVerdictSceneReadyAcceptedCount(
        bool automaticInvocation,
        bool runScopedActive,
        int cumulativeAcceptedCount,
        int runStartAcceptedCount)
    {
        if (automaticInvocation && runScopedActive)
        {
            return Math.Max(0, cumulativeAcceptedCount - runStartAcceptedCount);
        }

        return cumulativeAcceptedCount;
    }

    // 累積カウントを run-scoped で解決する汎用ヘルパー。run-scoped 時は run 開始からの差分、
    // それ以外は累積を返す。placement / override applied など run 内回数の判定に使う。
    public static int ResolveRunScopedCount(
        bool runScoped,
        int cumulativeCount,
        int runStartCount)
    {
        return runScoped ? Math.Max(0, cumulativeCount - runStartCount) : cumulativeCount;
    }

    // キャラ配置回数を run-scoped で解決する。前回 run の配置成功を今回の結果へ流用しない。
    public static int ResolveRunScopedPlacementCount(
        bool runScoped,
        int cumulativeCount,
        int runStartCount)
    {
        return ResolveRunScopedCount(runScoped, cumulativeCount, runStartCount);
    }

    // 地面検証済み配置か。anchor 由来かつ frame が明確な地面 provenance を持つ場合のみ true。
    // CharaSelectFallback（水上座標の再保存の可能性）/ Unknown / World は地面確認済みにしない。
    public static bool ResolveGroundPlacementVerified(
        bool placementApplied,
        string? placementSource,
        string? anchorFrame)
    {
        return placementApplied
            && string.Equals(placementSource, TitleBackgroundCharaSelectAnchorLogic.AnchorSource, StringComparison.Ordinal)
            && TitleBackgroundCharaSelectAnchorFrame.HasGroundProvenance(anchorFrame);
    }

    // camera-focus フォールバック由来の配置か（画面内のみ・地面位置未確認）。
    public static bool ResolveCameraFocusFallbackPlacement(
        bool placementApplied,
        string? placementSource)
    {
        return placementApplied
            && string.Equals(placementSource, TitleBackgroundCharaSelectAnchorLogic.CameraFocusSource, StringComparison.Ordinal);
    }

    // イベント発生型の post-login 異常（post-login sceneReady / Phase2G）を run-scoped で解決する。
    // run-scoped 時は run 開始 event sequence より後に記録された異常だけを今回の異常として扱い、
    // 前回 run で検出した sticky な異常を今回の判定へ持ち込まない。
    public static bool ResolveRunScopedEventAnomaly(
        bool runScoped,
        bool detected,
        long lastEventSeq,
        long runStartEventSeq)
    {
        if (!runScoped)
        {
            return detected;
        }

        return detected && lastEventSeq > runStartEventSeq;
    }

    // 状態型の post-login 異常（stale adapter / active scene override）を run-scoped で解決する。
    // run-scoped 時は「現時点の状態」だけを見て、過去 run の sticky 履歴は判定に含めない。
    public static bool ResolveRunScopedStateAnomaly(
        bool runScoped,
        bool historicalDetected,
        bool freshDetected)
    {
        return runScoped ? freshDetected : historicalDetected || freshDetected;
    }

    // 確認 run 成功時、実際に使われた world probe anchor を通常セッション向けに永続化してよいか。
    // fail-closed: run 中に world-experimental(probe) 配置が実際に適用され、かつ gate が Eligible を
    // 返している場合のみ true。呼び出し側は gate 通過済みの Resolve 結果をそのまま渡す前提だが、
    // ここでも Source/Eligible/placement 適用実績を独立に再検証する（二重チェック）。
    public static bool ShouldPersistRunAnchor(
        bool runScopedPlacementApplied,
        string? runPlacementSource,
        bool worldExperimentalEligible,
        string worldExperimentalSource,
        string worldExperimentalSourceProbeValue)
    {
        return runScopedPlacementApplied
            && string.Equals(runPlacementSource, TitleBackgroundCharaSelectAnchorLogic.WorldExperimentalSource, StringComparison.Ordinal)
            && worldExperimentalEligible
            && string.Equals(worldExperimentalSource, worldExperimentalSourceProbeValue, StringComparison.Ordinal);
    }
}

// 確認 run 完了時に実際に使われた world probe anchor（永続化候補）。
// _configuration が finally の設定復元で run 開始前へ巻き戻る前に、run 中の値をこの不変レコードへ
// キャプチャしておくための入れ物（config への書込みはしない、純粋なデータ保持のみ）。
internal readonly record struct TitleBackgroundRunAnchorPersistenceCandidate(
    string CandidateId,
    System.Numerics.Vector3 Position,
    uint TerritoryTypeId);

internal readonly record struct TitleBackgroundRunFacingCalibrationPersistenceCandidate(
    string CandidateId,
    float Offset);

internal readonly record struct TitleBackgroundRunCharaSelectPlacementPersistenceCandidate(
    string CandidateId,
    string TerritoryPath,
    uint LayoutTerritoryTypeId,
    uint LayoutLayerFilterKey,
    System.Numerics.Vector3 Position,
    float Rotation);

internal static class TitleBackgroundAutomaticCheckReportBuilder
{
    public const string FileName = "title-background-auto-check.txt";

    public static string Build(
        DateTimeOffset completedAt,
        IReadOnlyList<string> quickCheckLines,
        IReadOnlyList<string> diagnosticLines,
        bool partial = false,
        string? runId = null)
    {
        var lines = new List<string>
        {
            "[XIV Mini Util] Title Background automatic check",
            $"[XIV Mini Util] completedAt={completedAt:yyyy-MM-dd HH:mm:ss zzz}",
            $"[XIV Mini Util] runId={(string.IsNullOrWhiteSpace(runId) ? "none" : runId)}",
            $"[XIV Mini Util] completion={(partial ? "partial" : "complete")}",
            "[XIV Mini Util] --- QuickCheck ---",
        };
        lines.AddRange(quickCheckLines.Select(line => $"[XIV Mini Util] {line}"));
        lines.Add("[XIV Mini Util] --- Diagnostic ---");
        lines.AddRange(diagnosticLines.Select(line => $"[XIV Mini Util] {line}"));
        return string.Join(Environment.NewLine, lines);
    }
}

internal static class TitleBackgroundAutomaticCheckDiagnosticSelector
{
    private static readonly HashSet<string> IncludedKeys = new(StringComparer.Ordinal)
    {
        "runtimeMode",
        "sceneOverrideEnabled",
        "lastOverrideApplied",
        "lastOverrideNewPath",
        "lastOverrideTerritoryId",
        "lastOverrideLayerFilterKey",
        "failureSummary",
        "hooksReady",
        "environment.weatherCandidate",
        "environment.weatherRequestedId",
        "environment.weatherAppliedId",
        // V2 production path (Il Mheg proof) の 1 クリックレポート行。
        "v2.enabled",
        "v2.active",
        "v2.applied",
        "v2.targetScene",
        "v2.sceneReadyObservedCount",
        "v2.framingAttemptCount",
        "v2.framingAppliedCount",
        "v2.boundedRetryRemaining",
        "v2.boundedSettleRemaining",
        "v2.windowClosed",
        "v2.lastFramingStatus",
        "v2.lastStopReason",
        "v2.appliedPose",
        "v2.appliedFrame",
        "v2.legacyCameraPathInactive",
        "v2.postLoginWritesStopped",
        "v2.disposeState",
        // TitleEdit-informed Character Select placement path (PR #7 根本修正) の 1 クリックレポート行。
        // 実キー集合は TitleBackgroundCharaSelectPlacementDiagnostics.Keys が単一ソース。
        // 下の static 初期化で UnionWith して実装と allowlist を機械的に一致させる。
        "delivery.deliveryVerdict",
        "delivery.backgroundDeliveryVerdict",
        "delivery.transitionSafetyVerdict",
        "delivery.postLoginLeakVerdict",
        "delivery.mvpStatus",
        "delivery.mvpBlockingIssue",
        "delivery.nextAction",
        "transition.verdict.loginTransitionSafety",
        "transition.verdict.staleCharaSelectStateAfterLogin",
        "characterDraw.preLoginDrawPositionNonZero",
        // 自動確認レポートは run-scoped の配置証拠を出す。累積の last* は出さない（過去 run と混同させない）。
        "characterPlace.runAppliedFrameCount",
        "characterPlace.runTarget",
        "characterPlace.runSource",
        "characterPlace.runAnchorFrame",
        "characterPlace.runAnchorFrameGroundProvenance",
        "characterPlace.lastError",
        "characterPlace.anchorFrame",
        "characterPlace.anchorFrameSupported",
        // 問題4: world experimental の判定根拠（1フローで適用可否を読めるようにする）。
        "characterPlace.worldExperimentalSource",
        "characterPlace.savedTerritoryTypeId",
        "characterPlace.activeCandidateTerritoryId",
        "characterPlace.candidateMatch",
        "characterPlace.territoryMatch",
        "characterPlace.worldExperimentalEnabled",
        "characterPlace.worldExperimentalConfiguredEnabled",
        "characterPlace.persistentApplyEnabled",
        "characterPlace.worldExperimentalGate",
        "characterPlace.worldExperimentalApplicable",
        "character.facing.active",
        "character.facing.appliedFrameCount",
        "character.facing.appliedYaw",
        "character.facing.savedDirH",
        "character.facing.readBackRotation",
        "character.facing.calibration.source",
        "character.facing.calibration.offset",
        "character.facing.calibration.derivedOffset",
        "character.facing.calibration.naturalRotation",
        "character.facing.calibration.expectedFromGeometry",
        "character.facing.calibration.sampleCount",
        "character.facing.calibration.stableSampleCount",
        "character.facing.calibration.rejectedTransientCount",
        "character.facing.calibration.maxOffsetDelta",
        "character.facing.geometricExpectedYaw",
        "character.facing.angularError",
        "character.facing.maxAngularError",
        "character.facing.settledConsecutiveFrameCount",
        "character.facing.settledMaxAngularError",
        "character.facing.offsetAbsError",
        "character.facing.maxFrameDelta",
        "character.facing.maxAppliedError",
        "character.facing.lastError",
        "verify.poseHold",
        "verify.poseHold.metric",
        "verify.poseHold.detail",
        "verify.cameraJitter",
        "verify.cameraJitter.metric",
        "verify.cameraJitter.detail",
        "verify.rotationJitter",
        "verify.rotationJitter.metric",
        "verify.rotationJitter.detail",
        "verify.facing",
        "verify.facing.metric",
        "verify.facing.detail",
        "verify.framing",
        "verify.framing.metric",
        "verify.framing.detail",
        "verify.suppression",
        "verify.suppression.metric",
        "verify.suppression.detail",
        "verify.loginStop",
        "verify.loginStop.metric",
        "verify.loginStop.detail",
        "verify.environment",
        "verify.environment.metric",
        "verify.environment.detail",
        "view.enabled",
        "view.candidate",
        "view.camera",
        "view.focus",
        "view.fovY",
        "view.overrideAppliedCount",
        "view.overrideLastSource",
        // 保存 view のネイティブ pose とその復元結果、run 中の view 抑止フラグ。
        "view.poseCaptured",
        "view.poseDirH",
        "view.poseDirV",
        "view.poseDistance",
        "view.poseRestoreStatus",
        "view.poseAppliedCount",
        "view.poseAppliedDirH",
        "view.poseAppliedDirV",
        "view.poseAppliedDistance",
        "view.poseAppliedFovY",
        "view.poseLastRestoreStatus",
        "view.poseLastRestoreSceneGeneration",
        "view.poseMaintain.active",
        "view.poseMaintain.sceneGeneration",
        "view.poseMaintain.appliedCallCount",
        "view.poseMaintain.appliedFrameCount",
        "view.poseMaintain.lastFrame",
        "view.poseMaintain.stopReason",
        "view.suppressedByRun",
        // 保存view再現バグ診断（view.trace.*）: pose/FixOn適用直後〜後続フレームのカメラ実値trace。
        // サマリー行は常に allowlist。サンプル明細行（view.trace.sample[N].*）は動的キーのため
        // BuildViewReplayTraceSampleKeys() で TitleBackgroundViewReplayTraceLogic のフレームリストから機械的に生成する。
        "view.trace.status",
        "view.trace.sceneGeneration",
        "view.trace.fromCurrentRun",
        "view.trace.source",
        "view.trace.poseApplyAbsoluteFrame",
        "view.trace.fixOnApplyAbsoluteFrame",
        "view.trace.startAbsoluteFrame",
        "view.trace.startLookAtYCallCount",
        "view.trace.startCurveSetMidCallCount",
        "view.trace.startCurveLowHighCallCount",
        "view.trace.targetCamera",
        "view.trace.targetFocus",
        "view.trace.targetFovY",
        "view.trace.targetDirH",
        "view.trace.targetDirV",
        "view.trace.targetDistance",
        "view.trace.sampleCount",
        "view.trace.firstFrame",
        "view.trace.lastFrame",
        "view.trace.diverged",
        "view.trace.divergedAtFrame",
        "view.trace.divergedComponent",
        "view.trace.divergedMagnitude",
        "view.trace.lookAtYCallCount",
        "view.trace.lookAtYLastFrame",
        "view.trace.lookAtYLastReturnValue",
        "view.trace.curveSetMidCallCount",
        "view.trace.curveLowHighCallCount",
        "view.trace.curveGenerationOverrideSetMidAppliedCount",
        "view.trace.curveGenerationOverrideLowHighAppliedCount",
        "view.trace.curveGenerationOverrideLastAppliedFrame",
        "view.trace.runtimeRestoreStatus",
        "environment.dayTimeHours",
        "environment.weather",
        "environment.rainy",
        "environment.brightnessHint",
        "environment.noonOverrideEnabled",
        "environment.noonOverrideAppliedFrameCount",
        "environment.noonOverrideLastStatus",
        "environment.clearSkyOverrideEnabled",
        "environment.clearSkyOverrideAppliedFrameCount",
        "environment.clearSkyOverrideLastStatus",
        // candidate-specific 時刻ポリシー（FRU のみ非正午）と pre-login environment snapshot。
        "environment.timePolicyName",
        "environment.dayTimeRequestedSeconds",
        "environment.preLogin.snapshotCaptured",
        "environment.preLogin.weatherRequestedId",
        "environment.preLogin.weatherAppliedId",
        "environment.preLogin.weatherReadbackId",
        "environment.preLogin.timePolicyName",
        "environment.preLogin.dayTimeRequestedSeconds",
        "environment.preLogin.dayTimeReadbackSeconds",
        // FRU 固有: 戦闘 gimmick / telegraph SharedGroup の抑止（scene-generation 単位の bounded window）。
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
        "fru.suppression.vfxMode",
        "fru.suppression.denyTokenCount",
        "fru.suppression.keepTokenCount",
        "fru.suppression.lastGateStatus",
        "fru.suppression.firstFailureReason",
        "fru.suppression.cleanupState",
        "fru.suppression.suppressedKeys",
        "fixOn.hookInstalled",
        "fixOn.calls",
        "fixOn.lastFocusArgs",
        "fixOn.exp.gateReason",
        "fixOn.exp.applied",
        "fixOn.exp.anchorFrame",
        "fixOn.exp.anchor",
        "fixOn.exp.observedCamera",
        "fixOn.exp.observedFocus",
        "fixOn.exp.overrideFocus",
        "fixOn.exp.preLoginCamera",
        "fixOn.exp.preLoginLookAt",
        "fixOn.exp.preLoginCameraGenerationMatchesFixOn",
        "fixOn.exp.sceneGeneration",
        "fixOn.exp.captureContext",
        "fixOn.exp.charaSelectSession",
    };

    // view.trace.sample[N].* は相対フレーム番号ごとの動的キー。TitleBackgroundViewReplayTraceLogic の
    // 固定フレームリスト（frame 0 + SamplingFrames）から機械的に生成し、allowlist へ静的に追加する。
    // フレームリストが変わってもここを手で書き直す必要がないようにするため。
    static TitleBackgroundAutomaticCheckDiagnosticSelector()
    {
        // TitleEdit-informed placement path の診断キーは単一ソース（実装と allowlist の乖離防止, skill §3）。
        IncludedKeys.UnionWith(TitleBackgroundCharaSelectPlacementDiagnostics.Keys);

        foreach (var frame in BuildViewReplayTraceSampleFrames())
        {
            IncludedKeys.Add($"view.trace.sample[{frame}].status");
            IncludedKeys.Add($"view.trace.sample[{frame}].camera");
            IncludedKeys.Add($"view.trace.sample[{frame}].lookAt");
            IncludedKeys.Add($"view.trace.sample[{frame}].fovY");
            IncludedKeys.Add($"view.trace.sample[{frame}].dirH");
            IncludedKeys.Add($"view.trace.sample[{frame}].dirV");
            IncludedKeys.Add($"view.trace.sample[{frame}].distance");
            // 絶対フレーム（Phase2C基準）と採取時点の累計フックカウンタ（区間差分でフック稼働を読むための併記値）。
            IncludedKeys.Add($"view.trace.sample[{frame}].absoluteFrame");
            IncludedKeys.Add($"view.trace.sample[{frame}].lookAtYCalls");
            IncludedKeys.Add($"view.trace.sample[{frame}].curveSetMidCalls");
            IncludedKeys.Add($"view.trace.sample[{frame}].curveLowHighCalls");
        }
    }

    private static IEnumerable<int> BuildViewReplayTraceSampleFrames()
    {
        yield return 0;
        foreach (var frame in TitleBackgroundViewReplayTraceLogic.SamplingFrames)
        {
            yield return frame;
        }
    }

    public static IReadOnlyList<string> Select(IReadOnlyList<string> lines)
    {
        return lines
            .Where(line => IncludedKeys.Contains(GetKey(line)))
            .ToArray();
    }

    private static string GetKey(string line)
    {
        var separatorIndex = line.IndexOf('=');
        return separatorIndex < 0 ? line : line[..separatorIndex];
    }
}

public enum TitleBackgroundSettingsDisplayMode
{
    Simple,
    Advanced,
    DeveloperDiagnostics,
}

internal readonly record struct TitleBackgroundQuickCheckState(
    TitleBackgroundQuickCheckRunState RunState,
    DateTimeOffset? StartedAt,
    int SceneGenerationStart,
    int SceneReadyAcceptedCountStart,
    int OverrideAppliedCountStart,
    int Phase2GApplyCountStart,
    string SelectedCandidateIdStart,
    string EffectiveCandidateIdStart,
    TitleBackgroundCharacterSelectBackgroundMode BackgroundModeStart,
    TitleBackgroundCharacterSelectLightingMode LightingModeStart,
    string CurrentLobbyMapStart,
    bool StartedLoggedIn,
    // run 開始時点のキャラ配置回数。current run の配置成功判定の baseline。
    int CharacterPlacementCountStart = 0,
    // run 開始時点の遷移診断 event sequence。post-login 異常を run-scoped 判定する baseline。
    long TransitionEventSeqStart = 0,
    // run 開始時点の TitleEdit-informed placement 適用回数。persistent owner の通常 run も
    // 前回 run の適用を持ち込まずに判定する。
    int CharaSelectPlacementApplyCountStart = 0)
{
    public static TitleBackgroundQuickCheckState Idle { get; } = new(
        TitleBackgroundQuickCheckRunState.Idle,
        null,
        0,
        0,
        0,
        0,
        string.Empty,
        string.Empty,
        TitleBackgroundCharacterSelectBackgroundMode.Disabled,
        TitleBackgroundCharacterSelectLightingMode.Default,
        "None",
        false,
        0,
        0);
}

internal readonly record struct TitleBackgroundQuickCheckInput(
    bool RunScoped,
    TitleBackgroundQuickCheckRunState RunState,
    DateTimeOffset? StartedAt,
    DateTimeOffset CompletedAt,
    bool StartedLoggedIn,
    bool CharaSelectObserved,
    int SceneGenerationStart,
    int SceneGenerationEnd,
    int SceneReadyAcceptedCount,
    int OverrideAppliedCount,
    int Phase2GApplyCount,
    bool PluginOrHookError,
    string PluginOrHookErrorReason,
    bool IsLoggedIn,
    string CurrentLobbyMap,
    bool CurrentLobbyMapRemainedAfterLogin,
    TitleBackgroundCharacterSelectBackgroundMode BackgroundMode,
    TitleBackgroundCharacterSelectLightingMode LightingMode,
    string CandidateId,
    string CandidateDisplayName,
    bool CandidateVerifiedInGame,
    string CandidateSource,
    TitleBackgroundCharacterSelectCompatibility ExpectedCompatibility,
    TitleBackgroundCharacterSelectExpectedBrightness ExpectedBrightness,
    string OverrideTerritoryPath,
    uint OverrideTerritoryId,
    uint OverrideLayerFilterKey,
    bool CandidateFieldsValid,
    bool BackgroundApplied,
    bool BackgroundObserved,
    bool VisualConfirmationRequired,
    bool CharacterExpectedVisible,
    string CharacterObserved,
    bool CharacterKnownLimitation,
    bool SceneOverrideActiveAfterLogin,
    bool ActiveAfterLoginDetected,
    bool StaleCharaSelectStateAfterLogin,
    bool Phase2GAppliedAfterLogin,
    bool ForegroundPreserveUnavailable,
    bool ActorSourceAmbiguous,
    bool ObjectTableZeroTransformStubs,
    TitleBackgroundCharacterVisualStatus CharacterVisualStatus = TitleBackgroundCharacterVisualStatus.Unknown,
    TitleBackgroundCharaSelectCameraFramingMode CameraFramingMode = TitleBackgroundCharaSelectCameraFramingMode.Default,
    TitleBackgroundCharaSelectCameraFramingMode CandidateRecommendedFraming = TitleBackgroundCharaSelectCameraFramingMode.Default,
    string CandidateRecommendedAction = "",
    bool TitleBackgroundOverrideEnabledAtCheck = true,
    bool TitleBackgroundCameraOverrideEnabledAtCheck = true,
    bool LegacySceneCompositionEnabledAtCheck = false,
    bool TitleBackgroundIntegratedCompositionEnabledAtCheck = true,
    bool ShouldArmAdapterAtCheck = true,
    string ShouldArmAdapterReasonAtCheck = "",
    bool IntegratedCompositionRouteInvoked = false,
    string IntegratedCompositionRouteReason = "",
    bool CameraFramingApplied = false,
    bool SceneOverrideApplyObserved = false,
    bool IntegratedCompositionAutoEnabled = false,
    TitleBackgroundCharacterCompositionBridgeSnapshot CharacterCompositionBridge = default,
    string CameraProfileId = "",
    string CameraProfileSource = "",
    string CameraYaw = "",
    string CameraPitch = "",
    string CameraDistance = "",
    string CameraLookAtOffset = "",
    string CameraPositionOffset = "",
    string CameraFramesCharacter = "",
    string CameraFinalYawPitchDistanceMatchesProfile = "",
    bool CameraVisibleProfileResolved = false,
    bool CameraVisibleProfileApplied = false,
    string CameraVisibleProfileAppliedState = "",
    string CameraProfileApplyRoute = "",
    bool CameraCapturedProfileEnabled = false,
    string CameraCapturedProfileDirH = "",
    string CameraCapturedProfileDirV = "",
    string CameraCapturedProfileDistance = "",
    string CameraCapturedProfilePosition = "",
    string CameraCapturedProfileLookAt = "",
    string CameraCurrentDirH = "",
    string CameraCurrentDirV = "",
    string CameraCurrentDistance = "",
    string CameraCurrentPosition = "",
    string CameraCurrentLookAt = "",
    bool BridgeCharacterCompositionApplied = false,
    bool BridgeCameraProfileApplied = false,
    bool CharacterCompositedApplied = false,
    // passive 観測（カメラを意図的に書き換えない）が有効な run。
    // true のときは「visible camera profile が未適用」は仕様どおりなので警告しない。
    bool PassiveCameraObservationActive = false,
    // 配置がカメラ注視点フォールバック由来（画面内のみ・地面位置は未確認）か。
    bool CharacterPlacedViaCameraFocusFallback = false,
    // 配置が地面検証済み（anchor 由来かつ候補・座標系が検証済み）か。強い成功はこの場合だけ許可する。
    bool CharacterGroundPlacementVerified = false,
    string VerifyPoseHold = "not-evaluated",
    string VerifyCameraJitter = "not-evaluated",
    string VerifyRotationJitter = "not-evaluated",
    string VerifyFacing = "not-evaluated",
    string VerifyFraming = "not-evaluated",
    string VerifySuppression = "not-evaluated",
    string VerifyLoginStop = "not-evaluated",
    string VerifyEnvironment = "not-evaluated",
    TitleBackgroundCharaSelectPlacementProofSnapshot? CharaSelectPlacementProof = null,
    bool PersistentCharaSelectPlacementActive = false,
    bool PersistentCharaSelectPlacementApplied = false,
    bool PersistentCharaSelectPlacementReadbackConfirmed = false);

internal readonly record struct TitleBackgroundQuickCheckResult(
    TitleBackgroundQuickCheckLevel Level,
    TitleBackgroundQuickCheckRunState RunState,
    string Reason,
    string CandidateId,
    string CandidateDisplayName,
    DateTimeOffset CompletedAt,
    string NextAction,
    string DetailFileName,
    TitleBackgroundCharacterSelectBackgroundMode BackgroundMode,
    string BackgroundStatus,
    string LoginTransitionStatus,
    string PostLoginLeakStatus,
    string CharacterStatus,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> DetailLines);

internal static class TitleBackgroundQuickCheckEvaluator
{
    public const string DetailFileName = "title-background-quickcheck.txt";

    public static TitleBackgroundQuickCheckResult Evaluate(TitleBackgroundQuickCheckInput input)
    {
        var warnings = new List<string>();
        var ngReason = GetNgReason(input);
        var loginChecked = IsLoginTransitionChecked(input);
        var placementProof = input.CharaSelectPlacementProof;
        var placementProofActive = placementProof is { } proof
            && proof.PlacementProofArmed
            && string.Equals(proof.EngineOwner, "placement-proof", StringComparison.Ordinal);
        var placementEngineActive = placementProofActive || input.PersistentCharaSelectPlacementActive;
        var placementEngineApplied = placementProofActive
            ? placementProof!.Value.ApplyCount > 0
            : input.PersistentCharaSelectPlacementApplied;
        var placementProofStartedLoggedInExpected = placementProofActive
            && placementProof!.Value.LogoutTransitionObserved;
        var placementProofCharaSelectObserved = placementProofActive
            && placementProof!.Value.CharaSelectSessionObserved;
        var effectiveCharacterCompositedApplied = input.CharacterCompositedApplied
            || placementEngineApplied;
        // 地面検証済み（anchor 由来かつ frame が明確な地面 provenance を持つ）のみ強い成功を許可する。
        // camera-focus フォールバックや provenance 不足の anchor は「配置されたが地面位置は未確認」。
        var characterGroundPlacementVerified = input.CharacterGroundPlacementVerified;
        var characterPlacedViaCameraFocusFallback = effectiveCharacterCompositedApplied
            && input.CharacterPlacedViaCameraFocusFallback
            && !characterGroundPlacementVerified;
        // 配置されたが地面検証されていない（camera-focus / provenance 不足 anchor）。false OK を防ぐ。
        var characterPlacedButGroundNotVerified = !placementEngineActive
            && effectiveCharacterCompositedApplied
            && !characterGroundPlacementVerified;

        if (!input.RunScoped)
        {
            warnings.Add("QuickCheck was not started; run-scoped confidence is limited");
        }

        if (input.RunScoped && input.StartedLoggedIn && !placementProofStartedLoggedInExpected)
        {
            warnings.Add("Start QuickCheck from title/character select for a clean run. Current run started while already logged in.");
        }

        if (input.RunScoped && !input.CharaSelectObserved && !placementProofCharaSelectObserved)
        {
            warnings.Add("Character Select was not observed during this QuickCheck run");
        }

        if (!loginChecked)
        {
            warnings.Add("login transition has not been checked yet");
        }

        if (!placementEngineActive && input.SceneReadyAcceptedCount > 1)
        {
            warnings.Add($"sceneReady accepted multiple times during this run ({input.SceneReadyAcceptedCount})");
        }

        if (!placementProofActive && !input.CandidateVerifiedInGame)
        {
            warnings.Add("selected candidate is unverified");
        }

        if (!placementEngineActive && input.VisualConfirmationRequired)
        {
            warnings.Add("visual confirmation is required");
        }

        var nativeCharacterSourceUnresolved = !effectiveCharacterCompositedApplied
            && (input.ActorSourceAmbiguous || input.ObjectTableZeroTransformStubs);

        if (!placementEngineActive
            && (input.CharacterVisualStatus is TitleBackgroundCharacterVisualStatus.VisibleButTooSmall
                or TitleBackgroundCharacterVisualStatus.VisibleTopDown))
        {
            var framingDetail = input.CameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.Default
                ? "try lower camera framing"
                : "framing still needs tuning";
            warnings.Add($"camera framing needs adjustment: character is top-down or too small / {framingDetail}");
        }

        if (!placementEngineActive
            && (input.CharacterVisualStatus is TitleBackgroundCharacterVisualStatus.NotVisible
                or TitleBackgroundCharacterVisualStatus.Offscreen))
        {
            warnings.Add("character not visible or offscreen in frame; camera framing may be misaligned");
        }

        if (characterPlacedButGroundNotVerified)
        {
            warnings.Add(characterPlacedViaCameraFocusFallback
                ? "character placed in frame via camera-focus fallback; ground position is not verified, visual confirmation required"
                : "character placement applied but ground position is not verified; visual confirmation required");
        }

        if (!placementEngineActive
            && input.CharacterCompositionBridge.AppliedCharacter
            && input.CharacterVisualStatus == TitleBackgroundCharacterVisualStatus.Unknown
            && !characterGroundPlacementVerified)
        {
            warnings.Add("background works but character visibility is not visually confirmed; camera framing may still be wrong");
        }

        if (!placementEngineActive
            && input.CharacterCompositionBridge.AppliedCharacter
            && IsFalseOrNotObserved(input.CameraFramesCharacter))
        {
            warnings.Add("camera does not frame the character");
        }

        if (!placementEngineActive)
        {
            AddVerificationFailure(warnings, input.VerifyPoseHold, "saved view pose was not held");
            AddVerificationFailure(warnings, input.VerifyCameraJitter, "camera jitter exceeded the numeric threshold");
            AddVerificationFailure(warnings, input.VerifyRotationJitter, "character rotation jitter exceeded the numeric threshold");
            AddVerificationFailure(warnings, input.VerifyFacing, "character facing differs from the calibrated camera direction");
            AddVerificationFailure(warnings, input.VerifyFraming, "camera look-at does not frame the character");
            AddVerificationFailure(warnings, input.VerifySuppression, "saved-view writes were active during the automatic run");
            AddVerificationFailure(warnings, input.VerifyLoginStop, "bounded saved-view writes did not stop at login");
        }
        AddVerificationFailure(warnings, input.VerifyEnvironment, "configured title environment writes were not observed");

        var capturedProfileMissing = !effectiveCharacterCompositedApplied
            && IsCapturedLegacyProfileMissing(input);
        if (capturedProfileMissing)
        {
            warnings.Add("captured legacy visible camera profile is missing or not applied");
        }

        // passive 観測中は仕様としてカメラを書き換えない。未適用は失敗ではないので警告しない。
        // view / camera override を適用する設定（passive OFF）なのに未適用なら、従来どおり警告して
        // 本当の適用失敗は隠さない。
        if (!placementEngineActive
            && !input.PassiveCameraObservationActive
            && input.CameraVisibleProfileResolved
            && (!HasValue(input.CameraYaw) || !HasValue(input.CameraPitch) || !HasValue(input.CameraDistance)))
        {
            warnings.Add("visible camera profile resolved but yaw/pitch/distance was not applied");
        }

        if (!placementEngineActive
            && IsCustomN4F4(input.CandidateId)
            && (input.CameraFramingMode is TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended
                or TitleBackgroundCharaSelectCameraFramingMode.CustomExperimental)
            && !input.CameraVisibleProfileApplied
            && !characterGroundPlacementVerified)
        {
            warnings.Add("n4f4 visible camera profile is not applied; Y-only framing is not enough");
        }

        if (!placementEngineActive
            && input.CameraFramingApplied
            && IsFalseOrNotObserved(input.CameraFinalYawPitchDistanceMatchesProfile)
            && !characterGroundPlacementVerified)
        {
            warnings.Add("camera does not frame the character; final yaw/pitch/distance does not match profile");
        }

        if (!placementEngineActive
            && input.CharacterCompositionBridge.CharacterVisualKnownByBridge
            && input.CharacterVisualStatus != TitleBackgroundCharacterVisualStatus.Visible)
        {
            warnings.Add("characterVisualKnownByBridge is true but visualStatus is not Visible");
        }

        var bridgeIssue = CharaSelectSceneCompositionPlanner.BuildTitleBackgroundCharacterCompositionBridgeMissingReason(
            input.CharacterCompositionBridge);
        if (!placementEngineActive
            && input.LegacySceneCompositionEnabledAtCheck && input.TitleBackgroundOverrideEnabledAtCheck)
        {
            warnings.Add("legacy shooting composition dependency still required");
        }

        if (!placementEngineActive
            && bridgeIssue != "none"
            && bridgeIssue != "bridge applied camera only but not character/stage")
        {
            warnings.Add(bridgeIssue);
        }

        if (input.IsLoggedIn
            && string.Equals(input.CurrentLobbyMap, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("currentLobbyMap could not be read after login");
        }

        var level = !string.IsNullOrWhiteSpace(ngReason)
            ? TitleBackgroundQuickCheckLevel.NG
            : warnings.Count > 0
                ? TitleBackgroundQuickCheckLevel.WARN
                : TitleBackgroundQuickCheckLevel.OK;
        var reason = level switch
        {
            TitleBackgroundQuickCheckLevel.NG => ngReason,
            TitleBackgroundQuickCheckLevel.WARN when nativeCharacterSourceUnresolved => "native character source is unresolved",
            TitleBackgroundQuickCheckLevel.WARN when capturedProfileMissing => "captured legacy visible camera profile is missing",
            TitleBackgroundQuickCheckLevel.WARN when warnings.Any(warning => warning.Contains("camera does not frame the character", StringComparison.Ordinal)) => "camera does not frame the character",
            TitleBackgroundQuickCheckLevel.WARN when warnings.Any(warning => warning.Contains("numeric threshold", StringComparison.Ordinal)
                || warning.Contains("calibrated camera direction", StringComparison.Ordinal)
                || warning.Contains("camera look-at", StringComparison.Ordinal)
                || warning.Contains("saved view pose", StringComparison.Ordinal)
                || warning.Contains("saved-view writes", StringComparison.Ordinal)
                || warning.Contains("title environment", StringComparison.Ordinal)) => "numeric verification failed",
            TitleBackgroundQuickCheckLevel.WARN when warnings.Any(warning => warning.Contains("visual confirmation", StringComparison.Ordinal)
                || warning.Contains("visibility is not visually confirmed", StringComparison.Ordinal)) => "background works but character visibility is not visually confirmed",
            TitleBackgroundQuickCheckLevel.WARN when input.RunScoped && input.StartedLoggedIn && !placementProofStartedLoggedInExpected => "QuickCheck started while already logged in. Start from title/character select for a clean run.",
            TitleBackgroundQuickCheckLevel.WARN when input.RunScoped && !input.CharaSelectObserved && !placementProofCharaSelectObserved => "Character Select was not observed during this QuickCheck run.",
            TitleBackgroundQuickCheckLevel.WARN when !loginChecked => "login transition has not been checked yet. Log in, then run QuickCheck again.",
            TitleBackgroundQuickCheckLevel.OK when placementProofActive => "Character Select placement proof applied, no post-login leak",
            TitleBackgroundQuickCheckLevel.OK when input.PersistentCharaSelectPlacementActive => "persistent Character Select placement applied, no post-login leak",
            TitleBackgroundQuickCheckLevel.WARN => "background works with warnings",
            _ => "background-only works, no post-login leak",
        };
        var runState = level == TitleBackgroundQuickCheckLevel.NG
            ? TitleBackgroundQuickCheckRunState.Failed
            : TitleBackgroundQuickCheckRunState.Completed;
        var nextAction = BuildNextAction(level, input, warnings);
        var result = new TitleBackgroundQuickCheckResult(
            level,
            runState,
            reason,
            NormalizeNone(input.CandidateId),
            NormalizeNone(input.CandidateDisplayName),
            input.CompletedAt,
            nextAction,
            DetailFileName,
            input.BackgroundMode,
            input.BackgroundApplied && input.BackgroundObserved ? "applied" : "not applied",
            BuildLoginTransitionStatus(input, loginChecked, level),
            BuildPostLoginLeakStatus(input, loginChecked),
            BuildCharacterStatus(input),
            warnings,
            []);

        return result with
        {
            DetailLines = BuildDetailLines(input, result),
        };
    }

    private static string GetNgReason(TitleBackgroundQuickCheckInput input)
    {
        if (input.CharaSelectPlacementProof is { } placementProof
            && placementProof.PlacementProofArmed
            && string.Equals(placementProof.EngineOwner, "placement-proof", StringComparison.Ordinal))
        {
            return GetCharaSelectPlacementProofNgReason(input, placementProof);
        }

        if (input.PersistentCharaSelectPlacementActive)
        {
            return GetPersistentCharaSelectPlacementNgReason(input);
        }

        if (string.IsNullOrWhiteSpace(input.CandidateId) || input.CandidateId == "none")
        {
            return "effective candidate was none";
        }

        if (!input.CandidateFieldsValid)
        {
            return "selected candidate has invalid territory path/id/layer";
        }

        if (input.BackgroundMode == TitleBackgroundCharacterSelectBackgroundMode.Disabled)
        {
            return "background mode is disabled";
        }

        if (!TitleBackgroundDeliveryDiagnostic.IsMutationMode(input.BackgroundMode))
        {
            return "background mode does not apply scene override";
        }

        if (!input.TitleBackgroundOverrideEnabledAtCheck)
        {
            return "Character Select Background is disabled";
        }

        if (!input.TitleBackgroundCameraOverrideEnabledAtCheck)
        {
            return "Title Background camera override is disabled";
        }

        if (!input.TitleBackgroundIntegratedCompositionEnabledAtCheck)
        {
            return "integrated character composition is disabled";
        }

        if (!input.ShouldArmAdapterAtCheck)
        {
            return $"adapter was not armed: {NormalizeNone(input.ShouldArmAdapterReasonAtCheck)}";
        }

        if (input.OverrideAppliedCount <= 0 || !input.BackgroundApplied || !input.BackgroundObserved)
        {
            if (input.IntegratedCompositionRouteInvoked && !input.SceneOverrideApplyObserved)
            {
                return "integrated composition route was invoked but scene override was not observed";
            }

            if (!input.IntegratedCompositionRouteInvoked
                && !string.IsNullOrWhiteSpace(input.IntegratedCompositionRouteReason))
            {
                return "integrated composition flag is enabled but route was not invoked";
            }

            if (input.CameraFramingApplied && !input.SceneOverrideApplyObserved)
            {
                return "camera framing applied but scene override was not observed";
            }

            return "background was not applied";
        }

        var bridgeReason = CharaSelectSceneCompositionPlanner.BuildTitleBackgroundCharacterCompositionBridgeMissingReason(
            input.CharacterCompositionBridge);
        if (bridgeReason == "bridge applied camera only but not character/stage"
            || bridgeReason == "bridge invoked but character composition not applied")
        {
            return bridgeReason;
        }

        if (input.PluginOrHookError)
        {
            return $"plugin or hook error: {NormalizeNone(input.PluginOrHookErrorReason)}";
        }

        if (input.IsLoggedIn && input.CurrentLobbyMapRemainedAfterLogin)
        {
            return "currentLobbyMap remained after login";
        }

        if (input.SceneOverrideActiveAfterLogin || input.ActiveAfterLoginDetected)
        {
            return "post-login scene override leak detected";
        }

        if (input.Phase2GAppliedAfterLogin)
        {
            return "Phase2G applied after login";
        }

        if (input.StaleCharaSelectStateAfterLogin)
        {
            return "stale Character Select state remained after login";
        }

        return string.Empty;
    }

    private static string GetCharaSelectPlacementProofNgReason(
        TitleBackgroundQuickCheckInput input,
        TitleBackgroundCharaSelectPlacementProofSnapshot proof)
    {
        if (string.IsNullOrWhiteSpace(input.CandidateId) || input.CandidateId == "none")
        {
            return "effective candidate was none";
        }

        if (!input.CandidateFieldsValid)
        {
            return "selected candidate has invalid territory path/id/layer";
        }

        if (input.BackgroundMode == TitleBackgroundCharacterSelectBackgroundMode.Disabled
            || !TitleBackgroundDeliveryDiagnostic.IsMutationMode(input.BackgroundMode))
        {
            return "background mode does not apply scene override";
        }

        if (!input.TitleBackgroundOverrideEnabledAtCheck
            || !input.TitleBackgroundCameraOverrideEnabledAtCheck
            || !input.TitleBackgroundIntegratedCompositionEnabledAtCheck)
        {
            return "Character Select scene composition is disabled";
        }

        if (input.OverrideAppliedCount <= 0 || !input.BackgroundApplied || !input.BackgroundObserved)
        {
            return "background was not applied";
        }

        if (input.PluginOrHookError)
        {
            return $"plugin or hook error: {NormalizeNone(input.PluginOrHookErrorReason)}";
        }

        if (input.SceneOverrideActiveAfterLogin || input.ActiveAfterLoginDetected)
        {
            return "post-login scene override leak detected";
        }

        if (input.Phase2GAppliedAfterLogin || input.StaleCharaSelectStateAfterLogin
            || input.CurrentLobbyMapRemainedAfterLogin)
        {
            return "post-login Character Select state remained";
        }

        var targetFinite = !string.IsNullOrWhiteSpace(proof.TargetScene)
            && TitleBackgroundCameraMath.IsFiniteVector(proof.TargetPosition)
            && float.IsFinite(proof.TargetRotation);
        var decision = TitleBackgroundCharaSelectPlacementLogic.EvaluatePromotion(
            proof,
            partial: false,
            quickCheckLevel: TitleBackgroundQuickCheckLevel.OK,
            candidateMatches: proof.CandidateMatches,
            targetFinite,
            postLoginLeakStatus: BuildPostLoginLeakStatus(input, IsLoginTransitionChecked(input)));
        return decision.Eligible ? string.Empty : $"Character Select placement proof failed: {decision.Reason}";
    }

    // Persistent placement は proof の promotion 条件や legacy camera/bridge の書込条件を再評価しない。
    // 通常利用では保存済み candidate-bound transform を同じ placement writer が bounded に適用したこと、
    // その readback と login 後の停止だけを new path の権威信号として判定する。
    private static string GetPersistentCharaSelectPlacementNgReason(TitleBackgroundQuickCheckInput input)
    {
        if (string.IsNullOrWhiteSpace(input.CandidateId) || input.CandidateId == "none")
        {
            return "effective candidate was none";
        }

        if (!input.CandidateFieldsValid)
        {
            return "selected candidate has invalid territory path/id/layer";
        }

        if (input.BackgroundMode == TitleBackgroundCharacterSelectBackgroundMode.Disabled
            || !TitleBackgroundDeliveryDiagnostic.IsMutationMode(input.BackgroundMode))
        {
            return "background mode does not apply scene override";
        }

        if (!input.TitleBackgroundOverrideEnabledAtCheck
            || !input.TitleBackgroundCameraOverrideEnabledAtCheck
            || !input.TitleBackgroundIntegratedCompositionEnabledAtCheck)
        {
            return "Character Select scene composition is disabled";
        }

        if (input.OverrideAppliedCount <= 0 || !input.BackgroundApplied || !input.BackgroundObserved)
        {
            return "background was not applied";
        }

        if (input.PluginOrHookError)
        {
            return $"plugin or hook error: {NormalizeNone(input.PluginOrHookErrorReason)}";
        }

        if (input.SceneOverrideActiveAfterLogin || input.ActiveAfterLoginDetected)
        {
            return "post-login scene override leak detected";
        }

        if (input.Phase2GAppliedAfterLogin || input.StaleCharaSelectStateAfterLogin
            || input.CurrentLobbyMapRemainedAfterLogin)
        {
            return "post-login Character Select state remained";
        }

        if (!input.PersistentCharaSelectPlacementApplied)
        {
            return "persistent Character Select placement was not applied";
        }

        if (!input.PersistentCharaSelectPlacementReadbackConfirmed)
        {
            return "persistent Character Select placement readback was not confirmed";
        }

        return string.Empty;
    }

    private static string BuildNextAction(
        TitleBackgroundQuickCheckLevel level,
        TitleBackgroundQuickCheckInput input,
        IReadOnlyList<string> warnings)
    {
        if (level == TitleBackgroundQuickCheckLevel.NG)
        {
            return input.BackgroundMode == TitleBackgroundCharacterSelectBackgroundMode.Disabled
                ? "enable Character Select background and select custom:n4f4"
                : "check Settings UI candidate selection and transition diagnostics";
        }

        if (warnings.Any(warning => warning.Contains("login transition has not been checked", StringComparison.Ordinal)))
        {
            return "log in, then run QuickCheck again";
        }

        if (warnings.Any(warning => warning.Contains("already logged in", StringComparison.Ordinal)
                || warning.Contains("Character Select was not observed", StringComparison.Ordinal)))
        {
            return "start QuickCheck from title/character select, then log in and run check";
        }

        if (warnings.Any(warning => warning.Contains("run-scoped confidence", StringComparison.Ordinal)))
        {
            return "start QuickCheck, enter Character Select, log in, then run check";
        }

        if (warnings.Any(warning => warning.Contains("sceneReady accepted multiple", StringComparison.Ordinal)))
        {
            return "retry once from clean title screen if needed";
        }

        if (warnings.Count > 0
            && !input.CharacterCompositedApplied
            && (input.ActorSourceAmbiguous || input.ObjectTableZeroTransformStubs))
        {
            return "paste the automatically copied report for native character source investigation";
        }

        if (warnings.Any(warning => warning.Contains("framing needs adjustment", StringComparison.Ordinal)))
        {
            return input.CameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.Default
                ? "try Camera framing = Lower camera or n4f4 experimental"
                : "framing still needs tuning; adjust preset offset";
        }

        if (warnings.Any(warning => warning.Contains("not visible or offscreen", StringComparison.Ordinal)))
        {
            return "try another camera framing or reset visual status after screenshot";
        }

        if (warnings.Any(warning => warning.Contains("n4f4 visible camera profile", StringComparison.Ordinal)
                || warning.Contains("camera does not frame the character", StringComparison.Ordinal)
                || warning.Contains("yaw/pitch/distance was not applied", StringComparison.Ordinal)
                || warning.Contains("captured legacy visible camera profile", StringComparison.Ordinal)))
        {
            return "paste the automatically copied report for camera framing investigation";
        }

        if (warnings.Any(warning => warning.Contains("visual confirmation", StringComparison.Ordinal)
                || warning.Contains("visibility is not visually confirmed", StringComparison.Ordinal)))
        {
            return "paste the automatically copied report and include whether the character was visible";
        }

        if (warnings.Any(warning => warning.Contains("bridge not invoked", StringComparison.Ordinal)))
        {
            return "enter Character Select with Character Select Background enabled, then run Start QuickCheck again";
        }

        if (warnings.Any(warning => warning.Contains("character composition", StringComparison.Ordinal)
                || warning.Contains("character/stage", StringComparison.Ordinal)))
        {
            return "check character composition bridge diagnostics";
        }

        return "use background-only, or add a bright candidate";
    }

    public static IReadOnlyList<string> BuildChatLines(TitleBackgroundQuickCheckResult result)
    {
        var lines = new List<string>
        {
            $"[XMU QuickCheck] {result.Level}",
            $"Candidate: {result.CandidateId}" + (result.CandidateDisplayName == "none" ? string.Empty : $" / {result.CandidateDisplayName}"),
            $"Mode: {result.BackgroundMode}",
            $"Background: {result.BackgroundStatus}",
            $"Login transition: {result.LoginTransitionStatus}",
            $"Post-login leak: {result.PostLoginLeakStatus}",
            $"Character: {result.CharacterStatus}",
            $"Reason: {result.Reason} / Next: {result.NextAction}",
            $"Details: {result.DetailFileName}",
        };

        foreach (var warning in result.Warnings.Take(Math.Max(0, 10 - lines.Count)))
        {
            lines.Insert(lines.Count - 2, $"Warning: {warning}");
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildDetailLines(
        TitleBackgroundQuickCheckInput input,
        TitleBackgroundQuickCheckResult result)
    {
        var placementProof = input.CharaSelectPlacementProof;
        var placementProofActive = placementProof is { } proof
            && proof.PlacementProofArmed
            && string.Equals(proof.EngineOwner, "placement-proof", StringComparison.Ordinal);
        var placementActive = placementProofActive || input.PersistentCharaSelectPlacementActive;
        var placementApplied = placementProofActive
            ? placementProof!.Value.ApplyCount > 0
            : input.PersistentCharaSelectPlacementApplied;
        var placementReadbackConfirmed = placementProofActive
            ? placementProof!.Value.WriteConfirmed
            : input.PersistentCharaSelectPlacementReadbackConfirmed;
        return
        [
            $"result={result.Level}",
            $"reason={NormalizeNone(result.Reason)}",
            $"candidate.id={NormalizeNone(input.CandidateId)}",
            $"candidate.displayName={NormalizeNone(input.CandidateDisplayName)}",
            $"candidate.verifiedInGame={input.CandidateVerifiedInGame}",
            $"candidate.source={NormalizeNone(input.CandidateSource)}",
            $"candidate.expectedCompatibility={input.ExpectedCompatibility}",
            $"candidate.brightness={input.ExpectedBrightness}",
            $"background.mode={input.BackgroundMode}",
            $"lighting.mode={input.LightingMode}",
            $"override.path={NormalizeNone(input.OverrideTerritoryPath)}",
            $"override.territoryId={input.OverrideTerritoryId}",
            $"override.layerFilterKey={input.OverrideLayerFilterKey}",
            $"background.applied={input.BackgroundApplied}",
            $"background.observed={input.BackgroundObserved}",
            $"background.status={result.BackgroundStatus}",
            $"character.expectedVisible={input.CharacterExpectedVisible}",
            $"character.observed={NormalizeNone(input.CharacterObserved)}",
            $"character.knownLimitation={input.CharacterKnownLimitation}",
            $"character.status={NormalizeNone(result.CharacterStatus)}",
            $"character.visualStatus={input.CharacterVisualStatus}",
            $"character.placement.active={placementActive}",
            $"character.placement.applied={placementApplied}",
            $"character.placement.readbackConfirmed={placementReadbackConfirmed}",
            $"character.placement.proof={placementProofActive}",
            $"camera.framingMode={input.CameraFramingMode}",
            $"camera.framingOffset={TitleBackgroundCharaSelectCameraLogic.GetCameraFramingCurveOffset(input.CameraFramingMode):F3}",
            $"camera.profileId={NormalizeNone(input.CameraProfileId)}",
            $"camera.profileSource={BuildCameraProfileSource(input)}",
            $"camera.yaw={NormalizeNone(input.CameraYaw)}",
            $"camera.pitch={NormalizeNone(input.CameraPitch)}",
            $"camera.distance={NormalizeNone(input.CameraDistance)}",
            $"camera.lookAtOffset={NormalizeNone(input.CameraLookAtOffset)}",
            $"camera.positionOffset={NormalizeNone(input.CameraPositionOffset)}",
            $"camera.framesCharacter={NormalizeTriState(input.CameraFramesCharacter)}",
            $"camera.finalYawPitchDistanceMatchesProfile={NormalizeTriState(input.CameraFinalYawPitchDistanceMatchesProfile)}",
            $"camera.visibleProfileResolved={input.CameraVisibleProfileResolved}",
            $"camera.visibleProfileApplied={BuildVisibleProfileAppliedState(input)}",
            $"camera.profileApplyRoute={NormalizeNone(input.CameraProfileApplyRoute)}",
            $"camera.capturedProfile.enabled={input.CameraCapturedProfileEnabled}",
            $"camera.capturedProfile.dirH={NormalizeNone(input.CameraCapturedProfileDirH)}",
            $"camera.capturedProfile.dirV={NormalizeNone(input.CameraCapturedProfileDirV)}",
            $"camera.capturedProfile.distance={NormalizeNone(input.CameraCapturedProfileDistance)}",
            $"camera.capturedProfile.position={NormalizeNone(input.CameraCapturedProfilePosition)}",
            $"camera.capturedProfile.lookAt={NormalizeNone(input.CameraCapturedProfileLookAt)}",
            $"camera.current.dirH={NormalizeNone(input.CameraCurrentDirH)}",
            $"camera.current.dirV={NormalizeNone(input.CameraCurrentDirV)}",
            $"camera.current.distance={NormalizeNone(input.CameraCurrentDistance)}",
            $"camera.current.position={NormalizeNone(input.CameraCurrentPosition)}",
            $"camera.current.lookAt={NormalizeNone(input.CameraCurrentLookAt)}",
            $"verify.poseHold={NormalizeVerification(input.VerifyPoseHold)}",
            $"verify.cameraJitter={NormalizeVerification(input.VerifyCameraJitter)}",
            $"verify.rotationJitter={NormalizeVerification(input.VerifyRotationJitter)}",
            $"verify.facing={NormalizeVerification(input.VerifyFacing)}",
            $"verify.framing={NormalizeVerification(input.VerifyFraming)}",
            $"verify.suppression={NormalizeVerification(input.VerifySuppression)}",
            $"verify.loginStop={NormalizeVerification(input.VerifyLoginStop)}",
            $"verify.environment={NormalizeVerification(input.VerifyEnvironment)}",
            $"camera.recommendedFraming={input.CandidateRecommendedFraming}",
            $"camera.recommendedAction={NormalizeNone(input.CandidateRecommendedAction)}",
            $"bridge.characterCompositionApplied={input.BridgeCharacterCompositionApplied}",
            $"bridge.cameraProfileApplied={input.BridgeCameraProfileApplied}",
            $"knownLimitation.characterHidden={!input.CharacterExpectedVisible || input.CharacterKnownLimitation}",
            $"knownLimitation.foregroundPreserveUnavailable={input.ForegroundPreserveUnavailable}",
            $"knownLimitation.brightnessDark={input.ExpectedBrightness is TitleBackgroundCharacterSelectExpectedBrightness.Dark or TitleBackgroundCharacterSelectExpectedBrightness.TooDark}",
            $"developerNote.actorSourceAmbiguous={input.ActorSourceAmbiguous}",
            $"developerNote.objectTableZeroTransformStubs={input.ObjectTableZeroTransformStubs}",
            $"postLogin.sceneOverrideActive={input.SceneOverrideActiveAfterLogin}",
            $"postLogin.activeAfterLoginDetected={input.ActiveAfterLoginDetected}",
            $"postLogin.phase2GAppliedAfterLogin={input.Phase2GAppliedAfterLogin}",
            $"postLogin.currentLobbyMap={NormalizeNone(input.CurrentLobbyMap)}",
            $"postLogin.currentLobbyMapRemained={input.CurrentLobbyMapRemainedAfterLogin}",
            $"postLogin.loginTransitionStatus={NormalizeNone(result.LoginTransitionStatus)}",
            $"postLogin.leakStatus={NormalizeNone(result.PostLoginLeakStatus)}",
            $"quickCheck.titleBackgroundOverrideEnabled={input.TitleBackgroundOverrideEnabledAtCheck}",
            $"quickCheck.titleBackgroundCameraOverrideEnabled={input.TitleBackgroundCameraOverrideEnabledAtCheck}",
            $"quickCheck.legacySceneCompositionEnabled={input.LegacySceneCompositionEnabledAtCheck}",
            $"quickCheck.integratedCompositionEnabled={input.TitleBackgroundIntegratedCompositionEnabledAtCheck}",
            $"quickCheck.integratedCompositionAutoEnabled={input.IntegratedCompositionAutoEnabled}",
            $"quickCheck.integratedCompositionRouteRequired={input.TitleBackgroundIntegratedCompositionEnabledAtCheck && !input.SceneOverrideApplyObserved}",
            $"quickCheck.integratedCompositionRouteInvoked={input.IntegratedCompositionRouteInvoked}",
            $"quickCheck.integratedCompositionRoute.reason={NormalizeNone(input.IntegratedCompositionRouteReason)}",
            $"quickCheck.legacyCompositionUiToggle={input.LegacySceneCompositionEnabledAtCheck}",
            $"quickCheck.legacyCompositionDependency={input.LegacySceneCompositionEnabledAtCheck && !input.TitleBackgroundIntegratedCompositionEnabledAtCheck}",
            $"quickCheck.characterCompositionObserved={input.SceneOverrideApplyObserved}",
            $"quickCheck.cameraFramingApplied={input.CameraFramingApplied}",
            $"quickCheck.sceneOverrideApplyObserved={input.SceneOverrideApplyObserved}",
            $"quickCheck.characterCompositionBridge.enabled={input.CharacterCompositionBridge.Enabled}",
            $"quickCheck.characterCompositionBridge.required={input.CharacterCompositionBridge.Required}",
            $"quickCheck.characterCompositionBridge.invoked={input.CharacterCompositionBridge.Invoked}",
            $"quickCheck.characterCompositionBridge.reason={NormalizeNone(input.CharacterCompositionBridge.Reason)}",
            $"quickCheck.characterCompositionBridge.source={NormalizeNone(input.CharacterCompositionBridge.Source)}",
            $"quickCheck.characterCompositionBridge.appliedStage={input.CharacterCompositionBridge.AppliedStage}",
            $"quickCheck.characterCompositionBridge.appliedCharacter={input.CharacterCompositionBridge.AppliedCharacter}",
            $"quickCheck.characterCompositionBridge.appliedCamera={input.CharacterCompositionBridge.AppliedCamera}",
            $"quickCheck.characterVisualExpected={input.CharacterCompositionBridge.CharacterVisualExpected}",
            $"quickCheck.characterVisualKnownByBridge={input.CharacterCompositionBridge.CharacterVisualKnownByBridge}",
            $"quickCheck.shouldArmAdapter={input.ShouldArmAdapterAtCheck}",
            $"quickCheck.shouldArmAdapter.reason={NormalizeNone(input.ShouldArmAdapterReasonAtCheck)}",
            $"quickCheck.runScoped={input.RunScoped}",
            $"quickCheck.startedLoggedIn={input.StartedLoggedIn}",
            $"quickCheck.charaSelectObserved={input.CharaSelectObserved}",
            $"quickCheck.state={result.RunState}",
            $"quickCheck.sceneReadyAcceptedCount={input.SceneReadyAcceptedCount}",
            $"quickCheck.overrideAppliedCount={input.OverrideAppliedCount}",
            $"quickCheck.phase2GApplyCount={input.Phase2GApplyCount}",
            $"quickCheck.sceneGenerationStart={input.SceneGenerationStart}",
            $"quickCheck.sceneGenerationEnd={input.SceneGenerationEnd}",
            $"quickCheck.startedAt={FormatTime(input.StartedAt)}",
            $"quickCheck.completedAt={FormatTime(input.CompletedAt)}",
            $"quickCheck.warningCount={result.Warnings.Count}",
            $"nextAction={NormalizeNone(result.NextAction)}",
            "detail.transition=title-background-transitiondiag.txt",
            "detail.placement=title-background-placementdiag.txt",
            "detail.delivery=title-background-deliverydiag.txt",
        ];
    }

    public static bool IsSafeAfterLoginAdapterState(string? state)
    {
        return NormalizeNone(state) is "Inactive" or "Stopping";
    }

    public static bool IsUnsafeAfterLoginAdapterState(string? state)
    {
        var normalized = NormalizeNone(state);
        // "Inactive"（idle）や "Stopping" は post-login leak ではない。substring "Active" が "Inactive" に
        // 一致してしまう誤検知を避け、IsSafeAfterLoginAdapterState と整合させる。V2 は legacy adapter を
        // 意図的に arm しないため run 終了時の adapter.State は "Inactive" になり、この誤検知が NG を生んでいた。
        if (IsSafeAfterLoginAdapterState(normalized))
        {
            return false;
        }

        return normalized.Contains("Active", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Running", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Applying", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoginTransitionChecked(TitleBackgroundQuickCheckInput input)
    {
        if (input.RunScoped)
        {
            var placementProofAllowsStartedLoggedIn = input.CharaSelectPlacementProof is { } proof
                && proof.PlacementProofArmed
                && string.Equals(proof.EngineOwner, "placement-proof", StringComparison.Ordinal)
                && proof.LogoutTransitionObserved
                && proof.LoginStopped;
            return input.CharaSelectObserved
                && (!input.StartedLoggedIn || placementProofAllowsStartedLoggedIn)
                && input.RunState is TitleBackgroundQuickCheckRunState.LoggedInObserved
                    or TitleBackgroundQuickCheckRunState.Completed;
        }

        return input.IsLoggedIn;
    }

    private static string BuildLoginTransitionStatus(
        TitleBackgroundQuickCheckInput input,
        bool loginChecked,
        TitleBackgroundQuickCheckLevel level)
    {
        if (!loginChecked)
        {
            return "not checked";
        }

        if (level == TitleBackgroundQuickCheckLevel.NG
            && (input.SceneOverrideActiveAfterLogin
                || input.ActiveAfterLoginDetected
                || input.StaleCharaSelectStateAfterLogin
                || input.Phase2GAppliedAfterLogin
                || input.CurrentLobbyMapRemainedAfterLogin))
        {
            return "ng";
        }

        var placementEngineActive = input.PersistentCharaSelectPlacementActive
            || (input.CharaSelectPlacementProof is { } proof
                && proof.PlacementProofArmed
                && string.Equals(proof.EngineOwner, "placement-proof", StringComparison.Ordinal));
        return placementEngineActive || input.SceneReadyAcceptedCount <= 1 ? "safe" : "warn";
    }

    private static string BuildPostLoginLeakStatus(TitleBackgroundQuickCheckInput input, bool loginChecked)
    {
        if (!loginChecked)
        {
            return "not checked";
        }

        return input.SceneOverrideActiveAfterLogin || input.ActiveAfterLoginDetected
            ? "detected"
            : "none";
    }

    private static string BuildCharacterStatus(TitleBackgroundQuickCheckInput input)
    {
        if (input.CharaSelectPlacementProof is { } proof
            && proof.PlacementProofArmed
            && string.Equals(proof.EngineOwner, "placement-proof", StringComparison.Ordinal))
        {
            if (proof.ApplyCount > 0 && proof.WriteConfirmed)
            {
                return "placement applied / readback confirmed";
            }

            if (proof.ApplyCount > 0 && proof.SetterCallCompleted)
            {
                return "placement applied / readback unverified";
            }

            return proof.CharacterResolveStatus == "resolved"
                ? "character resolved / placement pending"
                : "character placement not resolved";
        }

        if (input.PersistentCharaSelectPlacementActive)
        {
            if (input.PersistentCharaSelectPlacementApplied
                && input.PersistentCharaSelectPlacementReadbackConfirmed)
            {
                return "persistent placement applied / readback confirmed";
            }

            if (input.PersistentCharaSelectPlacementApplied)
            {
                return "persistent placement applied / readback unverified";
            }

            return "persistent placement pending";
        }

        var visualStatusLabel = input.CharacterVisualStatus switch
        {
            TitleBackgroundCharacterVisualStatus.Visible => "visible",
            TitleBackgroundCharacterVisualStatus.VisibleButTooSmall => "visible / too small",
            TitleBackgroundCharacterVisualStatus.VisibleTopDown => "visible / top-down",
            TitleBackgroundCharacterVisualStatus.NotVisible => "not visible",
            TitleBackgroundCharacterVisualStatus.Offscreen => "offscreen",
            _ => null,
        };

        if (visualStatusLabel != null)
        {
            if (input.CharacterVisualStatus is TitleBackgroundCharacterVisualStatus.VisibleButTooSmall
                    or TitleBackgroundCharacterVisualStatus.VisibleTopDown)
            {
                return $"{visualStatusLabel} / camera framing needs adjustment";
            }

            return visualStatusLabel;
        }

        if (!input.CharacterExpectedVisible || input.CharacterKnownLimitation)
        {
            // 地面検証済み（anchor 由来・候補/座標系検証済み）のときだけ強い成功文言を出す。
            if (input.CharacterGroundPlacementVerified)
            {
                return "placement verified on ground anchor";
            }

            // camera-focus フォールバックは画面内に配置されただけで、地面位置は未確認。
            if (input.CharacterCompositedApplied && input.CharacterPlacedViaCameraFocusFallback)
            {
                return "placed in frame / ground position not confirmed";
            }

            // provenance 不足の anchor 等、配置はされたが地面位置は未確認。
            if (input.CharacterCompositedApplied)
            {
                return "placement applied / ground position not confirmed";
            }

            return "not detected by diagnostics / visual confirmation required";
        }

        return NormalizeNone(input.CharacterObserved);
    }

    private static string BuildCameraProfileSource(TitleBackgroundQuickCheckInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.CameraProfileSource))
        {
            return input.CameraProfileSource.Trim();
        }

        if (input.CameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.Default)
        {
            return "default";
        }

        if (input.CameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended
            || (input.CandidateRecommendedFraming != TitleBackgroundCharaSelectCameraFramingMode.Default
                && input.CameraFramingMode == input.CandidateRecommendedFraming))
        {
            return "candidate-recommended";
        }

        return "user-selected";
    }

    private static bool IsCustomN4F4(string candidateId)
    {
        return string.Equals(candidateId, TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId, StringComparison.Ordinal);
    }

    private static void AddVerificationFailure(List<string> warnings, string value, string warning)
    {
        if (string.Equals(NormalizeVerification(value), "FAIL", StringComparison.Ordinal))
        {
            warnings.Add(warning);
        }
    }

    private static string NormalizeVerification(string? value)
    {
        if (string.Equals(value, "PASS", StringComparison.OrdinalIgnoreCase))
        {
            return "PASS";
        }

        if (string.Equals(value, "WARN", StringComparison.OrdinalIgnoreCase))
        {
            return "WARN";
        }

        if (string.Equals(value, "FAIL", StringComparison.OrdinalIgnoreCase))
        {
            return "FAIL";
        }

        return "not-evaluated";
    }

    private static bool IsCapturedLegacyProfileMissing(TitleBackgroundQuickCheckInput input)
    {
        return IsCustomN4F4(input.CandidateId)
            && (input.CameraFramingMode is TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended
                or TitleBackgroundCharaSelectCameraFramingMode.CustomExperimental)
            && !input.CameraCapturedProfileEnabled
            && string.Equals(NormalizeNone(input.CameraProfileSource), "candidate", StringComparison.OrdinalIgnoreCase)
            && (!HasValue(input.CameraYaw) || !HasValue(input.CameraPitch) || !HasValue(input.CameraDistance))
            && IsFalseOrNotObserved(input.CameraFramesCharacter)
            && !input.BridgeCameraProfileApplied;
    }

    private static bool IsFalseOrNotObserved(string? value)
    {
        var normalized = NormalizeNone(value);
        return normalized.Equals("False", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("not-observed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasValue(string? value)
    {
        var normalized = NormalizeNone(value);
        return !normalized.Equals("none", StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals("unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildVisibleProfileAppliedState(TitleBackgroundQuickCheckInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.CameraVisibleProfileAppliedState))
        {
            return input.CameraVisibleProfileAppliedState.Trim();
        }

        return input.CameraVisibleProfileApplied ? "True" : "False";
    }

    private static string NormalizeTriState(string? value)
    {
        var normalized = NormalizeNone(value);
        return normalized.Equals("observed", StringComparison.OrdinalIgnoreCase)
            ? "True"
            : normalized.Equals("not-observed", StringComparison.OrdinalIgnoreCase)
                ? "False"
                : normalized.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? "Unknown"
                    : normalized;
    }

    private static string FormatTime(DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss zzz") : "none";
    }

    private static string NormalizeNone(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }
}

internal readonly record struct TitleBackgroundQuickCheckUiSummary(
    TitleBackgroundQuickCheckLevel Level,
    string StatusLine,
    string CandidateLine,
    string LastResultLine,
    string LastReasonLine,
    string NextActionLine,
    string DetailLine,
    string KnownLimitationLine);

internal enum TitleBackgroundSimpleUiStatus
{
    NeedsSetup,
    Ready,
    Working,
    Failed,
}

internal readonly record struct TitleBackgroundSimpleUiSummary(
    TitleBackgroundSimpleUiStatus Status,
    TitleBackgroundQuickCheckLevel LastCheckLevel,
    string StatusLine,
    string ResultLine,
    string NextActionLine);

internal static class TitleBackgroundQuickCheckUiPresenter
{
    private const string SimpleSetupAction = "Click Automatic Check.";
    private const string SimpleAdvancedAction = "Open Advanced diagnostics.";

    public static TitleBackgroundQuickCheckUiSummary BuildSummary(Configuration configuration)
    {
        var level = NormalizeLevel(configuration.TitleBackgroundLastQuickCheckResult);
        var candidate = string.IsNullOrWhiteSpace(configuration.TitleBackgroundLastQuickCheckCandidateId)
            ? configuration.TitleBackgroundCharacterSelectOverrideCandidateId
            : configuration.TitleBackgroundLastQuickCheckCandidateId;
        var candidateLine = string.IsNullOrWhiteSpace(candidate)
            ? "Character Select Background: none"
            : $"Character Select Background: {candidate}";
        var reason = string.IsNullOrWhiteSpace(configuration.TitleBackgroundLastQuickCheckReason)
            ? "Not checked yet"
            : configuration.TitleBackgroundLastQuickCheckReason;
        var statusLine = level switch
        {
            TitleBackgroundQuickCheckLevel.OK => "Status: OK - background-only works",
            TitleBackgroundQuickCheckLevel.WARN => $"Status: WARN - {reason}",
            TitleBackgroundQuickCheckLevel.NG => $"Status: NG - {reason}",
            _ => string.IsNullOrWhiteSpace(configuration.TitleBackgroundLastQuickCheckReason)
                ? "Status: Not checked yet"
                : $"Status: {reason}",
        };

        return new TitleBackgroundQuickCheckUiSummary(
            level,
            statusLine,
            candidateLine,
            level == TitleBackgroundQuickCheckLevel.NotRun
                ? "Last QuickCheck Result: Not Run"
                : $"Last QuickCheck Result: {level}",
            $"Last Reason: {reason}",
            $"Next Action: {NormalizeForUi(configuration.TitleBackgroundLastQuickCheckNextAction, "Run QuickCheck after entering Character Select and logging in once.")}",
            $"Detail: {NormalizeForUi(configuration.TitleBackgroundLastQuickCheckDetailFileName, TitleBackgroundQuickCheckEvaluator.DetailFileName)}",
            "Known limitation: character source is not resolved by diagnostics; visual confirmation is required. Character may appear off-center or too small with current camera framing.");
    }

    public static IReadOnlyList<string> GetSimpleModeItems(Configuration configuration)
    {
        return
        [
            "Character Select Background",
            "Status",
            "Automatic Check",
            "Copy Last Report",
        ];
    }

    public static TitleBackgroundSimpleUiSummary BuildSimpleSummary(Configuration configuration)
    {
        if (!IsSimpleAutoSetupConfigured(configuration))
        {
            return new TitleBackgroundSimpleUiSummary(
                TitleBackgroundSimpleUiStatus.NeedsSetup,
                NormalizeLevel(configuration.TitleBackgroundLastQuickCheckResult),
                "Status: Needs setup",
                "Character Select Background is not configured for n4f4 recommended.",
                SimpleSetupAction);
        }

        var level = NormalizeLevel(configuration.TitleBackgroundLastQuickCheckResult);
        return level switch
        {
            TitleBackgroundQuickCheckLevel.OK => new TitleBackgroundSimpleUiSummary(
                TitleBackgroundSimpleUiStatus.Working,
                level,
                "Status: Working",
                BuildSimpleCheckResultLine(level, configuration.TitleBackgroundLastQuickCheckReason),
                "No action needed."),
            TitleBackgroundQuickCheckLevel.WARN => new TitleBackgroundSimpleUiSummary(
                TitleBackgroundSimpleUiStatus.Failed,
                level,
                "Status: Failed",
                BuildSimpleCheckResultLine(level, configuration.TitleBackgroundLastQuickCheckReason),
                SimpleAdvancedAction),
            TitleBackgroundQuickCheckLevel.NG => new TitleBackgroundSimpleUiSummary(
                TitleBackgroundSimpleUiStatus.Failed,
                level,
                "Status: Failed",
                BuildSimpleCheckResultLine(level, configuration.TitleBackgroundLastQuickCheckReason),
                SimpleAdvancedAction),
            _ => new TitleBackgroundSimpleUiSummary(
                TitleBackgroundSimpleUiStatus.Ready,
                level,
                "Status: Ready",
                "n4f4 recommended is ready. Start Automatic Check, then log in from Character Select.",
                "Click Automatic Check."),
        };
    }

    public static string BuildSimpleCheckResultLine(TitleBackgroundQuickCheckLevel level, string? reason)
    {
        var normalizedLevel = NormalizeLevel(level);
        var normalizedReason = NormalizeForUi(reason, normalizedLevel switch
        {
            TitleBackgroundQuickCheckLevel.OK => "n4f4 background is working.",
            TitleBackgroundQuickCheckLevel.WARN => "background works but character visibility is not confirmed.",
            TitleBackgroundQuickCheckLevel.NG => "setup failed.",
            _ => "not checked yet.",
        });

        return normalizedLevel switch
        {
            TitleBackgroundQuickCheckLevel.OK => $"OK: {normalizedReason}",
            TitleBackgroundQuickCheckLevel.WARN => $"WARN: {BuildSimpleWarningReason(normalizedReason)}",
            TitleBackgroundQuickCheckLevel.NG => $"NG: {normalizedReason}",
            _ => "Not checked yet.",
        };
    }

    public static bool IsSimpleAutoSetupConfigured(Configuration configuration)
    {
        return IsPersistentCharaSelectPlacementConfigured(configuration)
            || IsApprovedStaticPlacementAutoSetupConfigured(configuration)
            || IsSimpleV2AutoSetupConfigured(configuration);
    }

    // candidate metadata だけで「通常 preset 選択から approved-static production placement を
    // 有効化してよいか」を決める純粋判定。fresh / 未promotion config でもここが true の候補は
    // placement engine の owner にできる（runtime 側は毎 pass の pre-login scene/layout 認可を
    // 通った approved anchor だけを使う）。現行 registry では FRU のみが該当する想定。
    public static bool IsApprovedStaticProductionPlacementEligible(
        TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        if (!TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId(candidate.Id))
        {
            return false;
        }

        if (!candidate.BackgroundUsable
            || !candidate.CharacterExpectedVisible
            || !candidate.VerifiedInGame
            || candidate.RequiresSourceBackedLayout
            || !candidate.ApprovedStaticAnchor.HasValue)
        {
            return false;
        }

        var anchor = candidate.ApprovedStaticAnchor.Value;
        return float.IsFinite(anchor.X)
            && float.IsFinite(anchor.Y)
            && float.IsFinite(anchor.Z);
    }

    // fresh / 未promotion で approved-static placement が有効化された状態を「設定済み」と認識する。
    // promotion 済み placement は IsPersistentCharaSelectPlacementConfigured が別途扱う。
    public static bool IsApprovedStaticPlacementAutoSetupConfigured(Configuration configuration)
    {
        var selectedCandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(
            configuration.TitleBackgroundCharacterSelectOverrideCandidateId);
        var placementCandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(
            configuration.TitleBackgroundCharaSelectPlacementCandidateId);
        return configuration.TitleBackgroundOverrideEnabled
            && configuration.TitleBackgroundCameraOverrideEnabled
            && configuration.TitleBackgroundIntegratedCompositionEnabled
            && configuration.TitleBackgroundCharaSelectPlacementEnabled
            && !configuration.TitleBackgroundV2Enabled
            && configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
            && configuration.TitleBackgroundCharacterSelectBackgroundMode == TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly
            && configuration.TitleBackgroundCharaSelectCameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended
            && string.Equals(selectedCandidateId, placementCandidateId, StringComparison.Ordinal)
            && TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(selectedCandidateId, out var selectedCandidate)
            && IsApprovedStaticProductionPlacementEligible(selectedCandidate);
    }

    public static bool IsPersistentCharaSelectPlacementConfigured(Configuration configuration)
    {
        var selectedCandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(
            configuration.TitleBackgroundCharacterSelectOverrideCandidateId);
        var placementCandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(
            configuration.TitleBackgroundCharaSelectPlacementCandidateId);
        return configuration.TitleBackgroundOverrideEnabled
            && configuration.TitleBackgroundCameraOverrideEnabled
            && configuration.TitleBackgroundIntegratedCompositionEnabled
            && configuration.TitleBackgroundCharaSelectPlacementEnabled
            && configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
            && configuration.TitleBackgroundCharacterSelectBackgroundMode == TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly
            && configuration.TitleBackgroundCharaSelectCameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended
            && TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId(selectedCandidateId)
            && string.Equals(selectedCandidateId, placementCandidateId, StringComparison.Ordinal)
            && (!TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(
                    selectedCandidateId,
                    out var selectedCandidate)
                || !selectedCandidate.RequiresSourceBackedLayout
                || TitleBackgroundCharaSelectSourceLayoutLogic.IsConfiguredForCandidate(
                    selectedCandidate,
                    configuration.TitleBackgroundLayoutTerritoryTypeId,
                    configuration.TitleBackgroundLayoutLayerFilterKey))
            && configuration.TitleBackgroundCharaSelectPlacementPositionCaptured
            && float.IsFinite(configuration.TitleBackgroundCharaSelectPlacementPositionX)
            && float.IsFinite(configuration.TitleBackgroundCharaSelectPlacementPositionY)
            && float.IsFinite(configuration.TitleBackgroundCharaSelectPlacementPositionZ)
            && float.IsFinite(configuration.TitleBackgroundCharaSelectPlacementRotation);
    }

    private static bool IsSimpleV2AutoSetupConfigured(Configuration configuration)
    {
        // 恒久 production baseline は verified Il Mheg V2。persistent placement が有効な場合だけ
        // その production path を引き継ぎ、未採取/候補不一致なら V2 rollback baseline を使う。
        return configuration.TitleBackgroundOverrideEnabled
            && configuration.TitleBackgroundCameraOverrideEnabled
            && configuration.TitleBackgroundIntegratedCompositionEnabled
            && configuration.TitleBackgroundV2Enabled
            && configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
            && configuration.TitleBackgroundCharaSelectCameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended
            && TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId(
                configuration.TitleBackgroundCharacterSelectOverrideCandidateId);
    }

    public static bool ApplySimpleAutoSetup(Configuration configuration)
    {
        return ApplySimpleAutoSetup(configuration, configuration.TitleBackgroundCharacterSelectOverrideCandidateId);
    }

    // candidateId が curated ならその candidate を、そうでなければ config 上の curated candidate を、
    // それも無ければ既定(Il Mheg)を適用する。curated 以外へは絶対に落とさない。
    public static bool ApplySimpleAutoSetup(Configuration configuration, string? candidateId)
    {
        var requestedId = TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId(candidateId)
            ? candidateId
            : (TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId(
                configuration.TitleBackgroundCharacterSelectOverrideCandidateId)
                ? configuration.TitleBackgroundCharacterSelectOverrideCandidateId
                : TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId);

        var candidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(requestedId, out var resolved)
            ? resolved
            : TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault();

        var preservePersistentPlacement = IsPersistentCharaSelectPlacementConfigured(configuration)
            && string.Equals(
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(
                    configuration.TitleBackgroundCharacterSelectOverrideCandidateId),
                candidate.Id,
                StringComparison.Ordinal);
        var existingLayoutTerritoryTypeId = configuration.TitleBackgroundLayoutTerritoryTypeId;
        var existingLayoutLayerFilterKey = configuration.TitleBackgroundLayoutLayerFilterKey;
        var preserveSourceBackedLayout = preservePersistentPlacement
            && candidate.RequiresSourceBackedLayout
            && TitleBackgroundCharaSelectSourceLayoutLogic.IsConfiguredForCandidate(
                candidate,
                existingLayoutTerritoryTypeId,
                existingLayoutLayerFilterKey);
        // promotion がまだ無くても、実機検証済みの approved-static candidate（現行では FRU のみ）は
        // production placement engine の owner にする。座標は runtime の pre-login 認可済み anchor
        // だけを使うため、ここで採取 proof / captured XYZ を偽装しない。
        var approvedStaticProductionPlacement = IsApprovedStaticProductionPlacementEligible(candidate);
        var usePlacement = preservePersistentPlacement || approvedStaticProductionPlacement;
        var changed = !IsSimpleAutoSetupConfigured(configuration)
            || configuration.TitleBackgroundCharacterSelectBackgroundMode != TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly
            || !string.Equals(configuration.TitleBackgroundCharacterSelectOverrideCandidateId, candidate.Id, StringComparison.Ordinal);

        TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(configuration, candidate);
        if (preserveSourceBackedLayout)
        {
            // ApplyToConfiguration intentionally uses the registry placeholder (layer=0) for
            // source-backed candidates; retain a previously promoted live layout value.
            configuration.TitleBackgroundLayoutTerritoryTypeId = existingLayoutTerritoryTypeId;
            configuration.TitleBackgroundLayoutLayerFilterKey = existingLayoutLayerFilterKey;
        }
        configuration.TitleBackgroundOverrideEnabled = true;
        configuration.TitleBackgroundCameraOverrideEnabled = true;
        configuration.TitleBackgroundIntegratedCompositionEnabled = true;
        // promotion 済み placement、または実機検証済み approved-static candidate は placement engine を
        // owner にする。どちらでもない候補（Il Mheg / Elpis 等）は verified V2 rollback baseline を維持する。
        // run 中の proof arm は別の runtime owner で表現する（config フリップではない）。
        configuration.TitleBackgroundV2Enabled = !usePlacement;
        configuration.TitleBackgroundCharaSelectPlacementEnabled = usePlacement;
        // placement path が消費する候補 id は「選択中の curated 候補」に恒久追従させる（run-scoped フリップではない）。
        // 候補が変わったら採取済み Position/Rotation は無効（点4: candidate 変更時 PositionCaptured=false）。
        if (!preservePersistentPlacement || !string.Equals(
                configuration.TitleBackgroundCharaSelectPlacementCandidateId,
                candidate.Id,
                StringComparison.Ordinal))
        {
            configuration.TitleBackgroundCharaSelectPlacementCandidateId = candidate.Id;
            configuration.TitleBackgroundCharaSelectPlacementPositionCaptured = false;
            configuration.TitleBackgroundCharaSelectPlacementPositionX = 0f;
            configuration.TitleBackgroundCharaSelectPlacementPositionY = 0f;
            configuration.TitleBackgroundCharaSelectPlacementPositionZ = 0f;
            configuration.TitleBackgroundCharaSelectPlacementRotation = 0f;
        }

        configuration.TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.CharaSelectOnly;
        configuration.TitleBackgroundCharacterSelectBackgroundMode = TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly;
        configuration.TitleBackgroundCharaSelectCameraFramingMode = TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended;
        return changed;
    }

    public static IReadOnlyList<string> GetAdvancedModeItems(Configuration configuration)
    {
        return
        [
            "Runtime Mode",
            "Background Mode",
            "Lighting Mode",
            "Effective Candidate Details",
            "Clear",
        ];
    }

    public static bool IsExperimentalModeVisibleInSimple(TitleBackgroundCharacterSelectBackgroundMode mode)
    {
        return false;
    }

    public static string BuildCandidateLabel(TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        var verified = candidate.VerifiedInGame ? "Verified" : "Unverified";
        var compatibility = candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
            ? "Background-only"
            : candidate.ExpectedCompatibility.ToString();
        return $"{candidate.Id} - {candidate.DisplayName} [{verified} / {candidate.ExpectedBrightness} / {compatibility} / {candidate.Source}]";
    }

    public static string GetBackgroundModeUiLabel(TitleBackgroundCharacterSelectBackgroundMode mode)
    {
        return mode switch
        {
            TitleBackgroundCharacterSelectBackgroundMode.Disabled => "Off",
            TitleBackgroundCharacterSelectBackgroundMode.DiagnosticsOnly => "Diagnostics only",
            TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly => "Background only",
            TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly => "Background only / recommended",
            TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground => "Experimental: preserve character foreground",
            TitleBackgroundCharacterSelectBackgroundMode.NativePreviewModelSource => "Experimental: native preview model source",
            _ => mode.ToString(),
        };
    }

    public static string GetBackgroundModeTooltip(TitleBackgroundCharacterSelectBackgroundMode mode)
    {
        return mode switch
        {
            TitleBackgroundCharacterSelectBackgroundMode.Disabled => "Character Select background override is disabled.",
            TitleBackgroundCharacterSelectBackgroundMode.DiagnosticsOnly => "Collect diagnostics without changing the background.",
            TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly => "Replace the full lobby scene as background-only. Character model is expected to be hidden.",
            TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly => "Use only candidates known to be compatible as background-only.",
            TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground => "Experimental. Not supported as stable because no safe public hook point is known.",
            TitleBackgroundCharacterSelectBackgroundMode.NativePreviewModelSource => "Experimental. Native CharaSelect actor source is not resolved yet.",
            _ => string.Empty,
        };
    }

    private static TitleBackgroundQuickCheckLevel NormalizeLevel(TitleBackgroundQuickCheckLevel level)
    {
        return Enum.IsDefined(typeof(TitleBackgroundQuickCheckLevel), level)
            ? level
            : TitleBackgroundQuickCheckLevel.NotRun;
    }

    private static string BuildSimpleWarningReason(string reason)
    {
        if (reason.Contains("camera", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("frame", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("visual", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("visible", StringComparison.OrdinalIgnoreCase))
        {
            return reason;
        }

        return "background works but character visibility is not visually confirmed.";
    }

    private static string NormalizeForUi(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
