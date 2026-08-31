// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundWorldProbeRuntimeState.cs
// Description: セッション限定 world probe / world座標対応サンプルの可変状態を保持する
// Reason: 巨大サービスから同一ライフサイクルの可変状態を責務単位で分離するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

// 問題4 Phase 0A/0C: config を書き込まないセッション限定状態（プラグイン再起動で消える）。
// - world probe（Enabled/HasValue/CandidateId/Position/TerritoryTypeId）
// - world/lobby 対応サンプル（Samples/SampleIndex）
internal sealed class TitleBackgroundWorldProbeRuntimeState
{
    public bool Enabled { get; set; }

    public bool HasValue { get; set; }

    public string CandidateId { get; set; } = string.Empty;

    public Vector3 Position { get; set; }

    public uint TerritoryTypeId { get; set; }

    public List<TitleBackgroundWorldCoordinateSample> Samples { get; } = new();

    public int SampleIndex { get; set; }
}
