// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectLevelResolver.cs
// Description: 指定座標に近いLevel rowを選択する純粋ロジック
// Reason: キャラ選択背景の指定地点読み込みをゲーム不要テストで検証するため
namespace XivMiniUtil.Services.CharaSelect;

internal readonly record struct CharaSelectLevelCandidate(
    uint RowId,
    ushort TerritoryTypeId,
    byte Type,
    float X,
    float Y,
    float Z);

internal readonly record struct CharaSelectResolvedLevel(
    uint RowId,
    byte Type,
    float DistanceSquared)
{
    public bool IsValid => RowId != 0;
}

internal static class CharaSelectLevelResolver
{
    public static CharaSelectResolvedLevel ResolveNearest(
        IEnumerable<CharaSelectLevelCandidate> candidates,
        ushort territoryTypeId,
        float x,
        float y,
        float z)
    {
        CharaSelectResolvedLevel best = default;
        foreach (var candidate in candidates)
        {
            if (candidate.RowId == 0 || candidate.TerritoryTypeId != territoryTypeId)
            {
                continue;
            }

            var distanceSquared = DistanceSquared(candidate.X, candidate.Y, candidate.Z, x, y, z);
            if (!best.IsValid || distanceSquared < best.DistanceSquared)
            {
                best = new CharaSelectResolvedLevel(candidate.RowId, candidate.Type, distanceSquared);
            }
        }

        return best;
    }

    private static float DistanceSquared(float ax, float ay, float az, float bx, float by, float bz)
    {
        var dx = ax - bx;
        var dy = ay - by;
        var dz = az - bz;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
