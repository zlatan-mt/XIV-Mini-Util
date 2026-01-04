// Path: projects/XIV-Mini-Util/Models/Desynth/DesynthOptions.cs
// Description: 分解処理のオプション
namespace XivMiniUtil;

public sealed record DesynthOptions(
    int MinLevel,
    int MaxLevel,
    bool SkipHighLevelWarning,
    DesynthTargetMode TargetMode,
    int TargetCount);
