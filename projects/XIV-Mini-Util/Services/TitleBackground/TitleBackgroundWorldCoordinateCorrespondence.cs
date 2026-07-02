// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundWorldCoordinateCorrespondence.cs
// Description: 問題4 Phase 0C — world 座標と CharaSelect 側観測値の対応をセッション限定で蓄積し、
//              座標変換方式（固定オフセット/一次変換/別solve）を弁別するための診断ロジック（read-only）。
// Reason: 1 地点だけでは方式を確定できないため、異なる標高の複数 run を自動蓄積し、純粋ロジックで
//         安全に（同一標高で除算しない・1～2件で確定扱いしない）集約判定できるようにする。
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

// 1 回の有効 probe run から採取する対応サンプル（セッション限定・config 非保存）。
internal readonly record struct TitleBackgroundWorldCoordinateSample(
    int Index,
    string RunId,
    string CompletedAt,
    string CandidateId,
    uint SavedTerritoryId,
    uint ActiveTerritoryId,
    Vector3 WorldProbePosition,
    Vector3 RunTarget,
    string RunSource,
    string RunAnchorFrame,
    int RunAppliedFrameCount,
    Vector3 FixOnObservedFocus,
    Vector3 PreLoginCamera,
    Vector3 PreLoginLookAt,
    int SceneGeneration,
    bool GenerationMatched,
    string CaptureContext)
{
    // レポート用の派生差分（世界座標を基準にした CharaSelect 側のずれ）。
    public Vector3 FocusMinusWorld => FixOnObservedFocus - WorldProbePosition;
    public Vector3 CameraMinusWorld => PreLoginCamera - WorldProbePosition;
    public Vector3 LookAtMinusWorld => PreLoginLookAt - WorldProbePosition;
    public Vector3 CameraMinusFocus => PreLoginCamera - FixOnObservedFocus;
}

internal enum TitleBackgroundWorldCoordinateVerdict
{
    InsufficientSamples,
    InsufficientElevationVariance,
    Inconsistent,
    FixedOffsetCandidate,
    LinearYCandidate,
}

internal readonly record struct TitleBackgroundWorldCoordinateAnalysis(
    TitleBackgroundWorldCoordinateVerdict Verdict,
    int SampleCount,
    bool HasElevationVariance,
    bool XOffsetConstant,
    bool ZOffsetConstant,
    float MeanXOffset,
    float MeanZOffset,
    bool YFixedOffsetCandidate,
    float MeanYOffset,
    bool YLinearComputable,
    float YLinearSlope,
    float YLinearIntercept,
    bool ResidualComputed,
    float MaxResidual,
    string Detail);

internal static class TitleBackgroundWorldCoordinateCorrespondenceLogic
{
    public const string ProbeSource = "probe";
    public const string ReportFileName = "title-background-world-coordinate-correspondence.txt";

    // X/Z 並進が安定とみなす許容（ゲーム単位）。これを超えてばらつくと translation 不安定＝inconsistent。
    public const float PositionConstantTolerance = 0.5f;
    // world Y の差がこの値以下しか無ければ「同一標高のみ」とみなし、Y 方向の除算（傾き）を行わない。
    public const float ElevationVarianceThreshold = 1.0f;
    // Y 差分（lookAt.Y - world.Y）が一定とみなす許容。
    public const float YOffsetConstantTolerance = 0.5f;
    // runTarget と検証済み world 座標が同一サンプルとみなせる許容（別状態の組合せを弾く）。
    public const float RunTargetMatchTolerance = 0.5f;
    // 3 件以上で一次fit の最大残差がこれを超えたら一次変換とはみなさず inconsistent にする。
    public const float YResidualTolerance = 1.0f;

    // run-scoped 値が「対応サンプルとして採用可能な有効 probe run か」を判定する純粋ゲート。
    // 単なる run 品質だけでなく「probe と run が同一の有効状態で観測されたか」を厳格に検証する:
    // eligible / probe 由来 / runSource=world-experimental / 適用回数>0 / generation 一致 /
    // 候補 非空・完全一致 / territory 非ゼロ・一致 / runAnchorFrame=world / 各座標有限 /
    // runTarget が検証済み world 座標と許容差内（別状態の組合せで偽サンプルを作らせない）。
    public static bool IsAcceptableRun(
        bool eligible,
        string? worldExperimentalSource,
        string? runSource,
        int runAppliedFrameCount,
        bool generationMatched,
        string? anchorCandidateId,
        string? activeCandidateId,
        uint savedTerritoryId,
        uint activeTerritoryId,
        string? runAnchorFrame,
        Vector3 worldPosition,
        Vector3 runTarget,
        Vector3 fixOnObservedFocus,
        Vector3 preLoginCamera,
        Vector3 preLoginLookAt)
    {
        if (!eligible
            || !string.Equals(worldExperimentalSource, ProbeSource, StringComparison.Ordinal)
            || !string.Equals(runSource, TitleBackgroundCharaSelectAnchorLogic.WorldExperimentalSource, StringComparison.Ordinal)
            || runAppliedFrameCount <= 0
            || !generationMatched
            || !string.Equals(runAnchorFrame, TitleBackgroundCharaSelectAnchorFrame.World, StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedAnchor = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(anchorCandidateId);
        var normalizedActive = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(activeCandidateId);
        if (string.IsNullOrEmpty(normalizedAnchor)
            || !string.Equals(normalizedAnchor, normalizedActive, StringComparison.Ordinal))
        {
            return false;
        }

        if (savedTerritoryId == 0 || activeTerritoryId == 0 || savedTerritoryId != activeTerritoryId)
        {
            return false;
        }

        if (!TitleBackgroundCameraMath.IsFiniteVector(worldPosition)
            || !TitleBackgroundCameraMath.IsFiniteVector(runTarget)
            || !TitleBackgroundCameraMath.IsFiniteVector(fixOnObservedFocus)
            || !TitleBackgroundCameraMath.IsFiniteVector(preLoginCamera)
            || !TitleBackgroundCameraMath.IsFiniteVector(preLoginLookAt))
        {
            return false;
        }

        // runTarget(実際に配置された座標) と検証済み world 座標が一致していること。
        return (runTarget - worldPosition).Length() <= RunTargetMatchTolerance;
    }

    // 蓄積サンプルから対応方式を集約判定する。canonical な CharaSelect 側系列は preLoginLookAt
    // （X/Z が world と一致し、Y が world 高度を追従するかが地下問題の核心のため）。
    // 安全則: 2 件未満は insufficient-samples、同一標高のみは除算せず insufficient-elevation-variance、
    // X/Z 並進が不安定なら inconsistent。1～2 件では「候補」までしか言わない（確定扱いしない）。
    public static TitleBackgroundWorldCoordinateAnalysis Analyze(
        IReadOnlyList<TitleBackgroundWorldCoordinateSample> samples)
    {
        var count = samples?.Count ?? 0;
        if (count < 2)
        {
            return new TitleBackgroundWorldCoordinateAnalysis(
                TitleBackgroundWorldCoordinateVerdict.InsufficientSamples,
                count,
                false, false, false, 0f, 0f, false, 0f, false, 0f, 0f, false, 0f,
                "need >= 2 valid probe runs at different elevations");
        }

        var xOffsets = new float[count];
        var zOffsets = new float[count];
        var yOffsets = new float[count];
        var worldY = new float[count];
        var lookAtY = new float[count];
        for (var i = 0; i < count; i++)
        {
            var s = samples![i];
            xOffsets[i] = s.PreLoginLookAt.X - s.WorldProbePosition.X;
            zOffsets[i] = s.PreLoginLookAt.Z - s.WorldProbePosition.Z;
            yOffsets[i] = s.PreLoginLookAt.Y - s.WorldProbePosition.Y;
            worldY[i] = s.WorldProbePosition.Y;
            lookAtY[i] = s.PreLoginLookAt.Y;
        }

        var meanXOffset = Mean(xOffsets);
        var meanZOffset = Mean(zOffsets);
        var meanYOffset = Mean(yOffsets);
        var xConstant = Range(xOffsets) <= PositionConstantTolerance;
        var zConstant = Range(zOffsets) <= PositionConstantTolerance;
        var worldYRange = Range(worldY);
        var hasElevationVariance = worldYRange > ElevationVarianceThreshold;

        if (!xConstant || !zConstant)
        {
            return new TitleBackgroundWorldCoordinateAnalysis(
                TitleBackgroundWorldCoordinateVerdict.Inconsistent,
                count,
                hasElevationVariance,
                xConstant, zConstant, meanXOffset, meanZOffset,
                false, meanYOffset, false, 0f, 0f, false, 0f,
                "X/Z translation is not stable across samples");
        }

        if (!hasElevationVariance)
        {
            // 同一標高だけ。Y 方向の傾きは除算できないため算出しない。
            return new TitleBackgroundWorldCoordinateAnalysis(
                TitleBackgroundWorldCoordinateVerdict.InsufficientElevationVariance,
                count,
                false,
                xConstant, zConstant, meanXOffset, meanZOffset,
                false, meanYOffset, false, 0f, 0f, false, 0f,
                "all samples share the same world elevation; vary altitude to solve Y");
        }

        // 一次変換 lookAtY = slope*worldY + intercept（2 点は厳密、3 点以上は最小二乗）。
        var (slope, intercept) = LeastSquares(worldY, lookAtY);
        var yOffsetConstant = Range(yOffsets) <= YOffsetConstantTolerance;

        var residualComputed = count >= 3;
        var maxResidual = 0f;
        if (residualComputed)
        {
            for (var i = 0; i < count; i++)
            {
                var predicted = slope * worldY[i] + intercept;
                maxResidual = MathF.Max(maxResidual, MathF.Abs(predicted - lookAtY[i]));
            }
        }

        // 3 件以上で一次fit が大きく外れる（残差超過）なら一次変換とはみなさず inconsistent。
        // 残差を計算するだけで判定に使わないと、非線形でも linear-candidate を通してしまう。
        if (residualComputed && maxResidual > YResidualTolerance)
        {
            return new TitleBackgroundWorldCoordinateAnalysis(
                TitleBackgroundWorldCoordinateVerdict.Inconsistent,
                count,
                true,
                xConstant, zConstant, meanXOffset, meanZOffset,
                yOffsetConstant, meanYOffset, true, slope, intercept, residualComputed, maxResidual,
                "Y relationship is non-linear (residual exceeds tolerance)");
        }

        if (yOffsetConstant)
        {
            // lookAt.Y が world.Y を一定オフセットで追従（傾き ~1）。Y は固定オフセットで写せる候補。
            return new TitleBackgroundWorldCoordinateAnalysis(
                TitleBackgroundWorldCoordinateVerdict.FixedOffsetCandidate,
                count,
                true,
                xConstant, zConstant, meanXOffset, meanZOffset,
                true, meanYOffset, true, slope, intercept, residualComputed, maxResidual,
                "Y diff is near-constant; fixed-offset candidate (not confirmed)");
        }

        return new TitleBackgroundWorldCoordinateAnalysis(
            TitleBackgroundWorldCoordinateVerdict.LinearYCandidate,
            count,
            true,
            xConstant, zConstant, meanXOffset, meanZOffset,
            false, meanYOffset, true, slope, intercept, residualComputed, maxResidual,
            "Y diff varies with elevation; linear-Y candidate (slope ~0 means lobby Y ignores world Y)");
    }

    public static string DescribeVerdict(TitleBackgroundWorldCoordinateVerdict verdict)
    {
        return verdict switch
        {
            TitleBackgroundWorldCoordinateVerdict.InsufficientSamples => "insufficient-samples",
            TitleBackgroundWorldCoordinateVerdict.InsufficientElevationVariance => "insufficient-elevation-variance",
            TitleBackgroundWorldCoordinateVerdict.Inconsistent => "inconsistent",
            TitleBackgroundWorldCoordinateVerdict.FixedOffsetCandidate => "fixed-offset-candidate",
            TitleBackgroundWorldCoordinateVerdict.LinearYCandidate => "linear-y-candidate",
            _ => "unknown",
        };
    }

    // コピー可能な統合レポートを純粋に組み立てる（ファイル保存/クリップボードは呼び出し側）。
    public static IReadOnlyList<string> BuildReport(
        IReadOnlyList<TitleBackgroundWorldCoordinateSample> samples)
    {
        var lines = new List<string>
        {
            "[XIV Mini Util] Title Background world/lobby coordinate correspondence",
            "[XIV Mini Util] phase=0C diagnostics-only (no transform applied, not ground-verified)",
            $"[XIV Mini Util] sampleCount={samples?.Count ?? 0}",
        };

        if (samples != null)
        {
            foreach (var s in samples)
            {
                lines.Add($"--- sample[{s.Index}] runId={NoneIfEmpty(s.RunId)} completedAt={NoneIfEmpty(s.CompletedAt)} ---");
                lines.Add($"  candidate={NoneIfEmpty(s.CandidateId)} savedTerritory={s.SavedTerritoryId} activeTerritory={s.ActiveTerritoryId}");
                lines.Add($"  worldProbePosition={Fmt(s.WorldProbePosition)}");
                lines.Add($"  runTarget={Fmt(s.RunTarget)} runSource={NoneIfEmpty(s.RunSource)} runAnchorFrame={NoneIfEmpty(s.RunAnchorFrame)} runAppliedFrameCount={s.RunAppliedFrameCount}");
                lines.Add($"  fixOnObservedFocus={Fmt(s.FixOnObservedFocus)}");
                lines.Add($"  preLoginCamera={Fmt(s.PreLoginCamera)} preLoginLookAt={Fmt(s.PreLoginLookAt)}");
                lines.Add($"  sceneGeneration={s.SceneGeneration} generationMatched={s.GenerationMatched} captureContext={NoneIfEmpty(s.CaptureContext)}");
                lines.Add($"  focus-world={Fmt(s.FocusMinusWorld)} camera-world={Fmt(s.CameraMinusWorld)} lookAt-world={Fmt(s.LookAtMinusWorld)} camera-focus={Fmt(s.CameraMinusFocus)}");
            }
        }

        var analysis = Analyze(samples ?? Array.Empty<TitleBackgroundWorldCoordinateSample>());
        lines.Add("--- analysis ---");
        lines.Add($"verdict={DescribeVerdict(analysis.Verdict)}");
        lines.Add($"sampleCount={analysis.SampleCount}");
        lines.Add($"hasElevationVariance={analysis.HasElevationVariance}");
        lines.Add($"xOffsetConstant={analysis.XOffsetConstant} meanXOffset={analysis.MeanXOffset:0.###}");
        lines.Add($"zOffsetConstant={analysis.ZOffsetConstant} meanZOffset={analysis.MeanZOffset:0.###}");
        lines.Add($"yFixedOffsetCandidate={analysis.YFixedOffsetCandidate} meanYOffset={analysis.MeanYOffset:0.###}");
        lines.Add($"yLinearComputable={analysis.YLinearComputable} yLinearSlope={analysis.YLinearSlope:0.####} yLinearIntercept={analysis.YLinearIntercept:0.###}");
        lines.Add($"residualComputed={analysis.ResidualComputed} maxResidual={(analysis.ResidualComputed ? analysis.MaxResidual.ToString("0.###") : "n/a")}");
        lines.Add($"detail={analysis.Detail}");
        return lines;
    }

    private static float Mean(float[] values)
    {
        if (values.Length == 0)
        {
            return 0f;
        }

        var sum = 0f;
        foreach (var v in values)
        {
            sum += v;
        }

        return sum / values.Length;
    }

    private static float Range(float[] values)
    {
        if (values.Length == 0)
        {
            return 0f;
        }

        var min = values[0];
        var max = values[0];
        foreach (var v in values)
        {
            min = MathF.Min(min, v);
            max = MathF.Max(max, v);
        }

        return max - min;
    }

    private static (float Slope, float Intercept) LeastSquares(float[] x, float[] y)
    {
        var n = x.Length;
        float sumX = 0f, sumY = 0f, sumXX = 0f, sumXY = 0f;
        for (var i = 0; i < n; i++)
        {
            sumX += x[i];
            sumY += y[i];
            sumXX += x[i] * x[i];
            sumXY += x[i] * y[i];
        }

        var denom = (n * sumXX) - (sumX * sumX);
        if (MathF.Abs(denom) < 1e-6f)
        {
            // 呼び出し側で elevation variance を保証済みだが、数値的に縮退したら傾き0で返す。
            return (0f, Mean(y));
        }

        var slope = ((n * sumXY) - (sumX * sumY)) / denom;
        var intercept = (sumY - (slope * sumX)) / n;
        return (slope, intercept);
    }

    private static string Fmt(Vector3 v) => $"({v.X:0.###}, {v.Y:0.###}, {v.Z:0.###})";

    private static string NoneIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
}
