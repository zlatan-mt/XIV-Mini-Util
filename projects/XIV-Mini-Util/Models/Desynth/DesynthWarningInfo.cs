// Path: projects/XIV-Mini-Util/Models/Desynth/DesynthWarningInfo.cs
// Description: 分解警告情報
namespace XivMiniUtil;

public sealed record DesynthWarningInfo(
    string ItemName,
    int ItemLevel,
    int MaxItemLevel);
