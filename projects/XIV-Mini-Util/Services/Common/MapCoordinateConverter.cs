// Path: projects/XIV-Mini-Util/Services/MapCoordinateConverter.cs
// Description: FFXIV座標変換の共通処理を提供する
// Reason: 座標変換ロジックを集約し重複を削減するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/NpcLocationResolver.cs, projects/XIV-Mini-Util/Services/TeleportService.cs
using System;

namespace XivMiniUtil.Services.Common;

internal static class MapCoordinateConverter
{
    public static float ConvertFromFloat(float rawPosition, float offset, ushort sizeFactor, bool roundToTenth)
    {
        var value = ConvertCore(rawPosition, offset, sizeFactor);
        return roundToTenth ? MathF.Round(value, 1, MidpointRounding.AwayFromZero) : value;
    }

    public static float ConvertFromShort(short rawPosition, short offset, ushort sizeFactor)
    {
        // エーテライトの座標はshort型で格納されている
        return ConvertCore(rawPosition, offset, sizeFactor);
    }

    private static float ConvertCore(float rawPosition, float offset, ushort sizeFactor)
    {
        // FFXIV座標変換: c = 41.0 / (sizeFactor/100.0) * ((raw * sizeFactor / 100.0 + 1024.0) / 2048.0) + 1.0
        var scale = sizeFactor / 100f;
        var c = 41f / scale;
        var adjusted = (rawPosition * scale + 1024f) / 2048f;
        return c * adjusted + 1f;
    }
}
