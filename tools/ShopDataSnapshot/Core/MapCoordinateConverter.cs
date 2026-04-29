namespace ShopDataSnapshot.Core;

internal static class MapCoordinateConverter
{
    public static float ConvertFromFloat(float rawPosition, float offset, ushort sizeFactor)
    {
        var scale = sizeFactor / 100f;
        var c = 41f / scale;
        var adjusted = (rawPosition * scale + 1024f) / 2048f;
        return MathF.Round(c * adjusted + 1f, 1, MidpointRounding.AwayFromZero);
    }
}
