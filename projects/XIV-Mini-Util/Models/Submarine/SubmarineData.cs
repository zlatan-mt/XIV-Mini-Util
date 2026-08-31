// Path: projects/XIV-Mini-Util/Models/Submarine/SubmarineData.cs
using System.Text.Json.Serialization;

namespace XivMiniUtil.Models.Submarine;

public enum SubmarineStatus
{
    Unknown,
    Exploring,
    Completed
}

public class SubmarineData
{
    public string Name { get; set; } = string.Empty;
    public ushort Rank { get; set; }
    
    // 常にUTCで保持
    public DateTime ReturnTime { get; set; }
    public DateTime RegisterTime { get; set; }
    public DateTime LastNotifiedReturnTime { get; set; } = DateTime.MinValue;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SubmarineStatus Status { get; set; } = SubmarineStatus.Unknown;
}

public class CharacterSubmarines
{
    public string CharacterName { get; set; } = string.Empty;
    public List<SubmarineData> Submarines { get; set; } = new();
}
