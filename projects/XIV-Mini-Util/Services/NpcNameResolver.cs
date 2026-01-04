// Path: projects/XIV-Mini-Util/Services/NpcNameResolver.cs
// Description: NPC名の取得と例外ハンドリングを担当する
// Reason: ShopDataCacheから責務を分離し例外処理を統一するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services;

internal sealed class NpcNameResolver
{
    public string GetNpcName(ExcelSheet<ENpcResident> npcResidentSheet, uint npcId)
    {
        try
        {
            var resident = npcResidentSheet.GetRow(npcId);
            if (resident.RowId == 0)
            {
                return string.Empty;
            }

            return resident.Singular.ToString();
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
