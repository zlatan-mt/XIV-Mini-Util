// Path: projects/XIV-Mini-Util/Models/Desynth/DesynthPreviewRequest.cs
// Description: 分解プレビュー用の条件指定
using XivMiniUtil;

namespace XivMiniUtil.Models.Desynth;

public sealed record DesynthPreviewRequest(
    int MinLevel,
    int MaxLevel,
    DesynthTargetMode TargetMode,
    int TargetCount)
{
    public int NormalizedTargetCount => Math.Clamp(TargetCount, 1, 999);
}
