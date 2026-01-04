// Path: projects/XIV-Mini-Util/Models/Desynth/DesynthResult.cs
// Description: 分解処理の結果
namespace XivMiniUtil;

public sealed record DesynthResult(
    int ProcessedCount,
    int SkippedCount,
    List<string> Errors);
