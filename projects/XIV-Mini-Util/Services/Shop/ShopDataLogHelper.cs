// Path: projects/XIV-Mini-Util/Services/ShopDataLogHelper.cs
// Description: ログメッセージの重複を減らすためのヘルパー
// Reason: ShopDataCache内の型/プロパティログを共通化するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Dalamud.Plugin.Services;
using System.Linq;

namespace XivMiniUtil.Services.Shop;

internal static class ShopDataLogHelper
{
    public static void LogFirstTypeMetadata(
        IPluginLog pluginLog,
        string label,
        object instance,
        ref bool logged)
    {
        if (logged)
        {
            return;
        }

        var type = instance.GetType().FullName ?? "null";
        var props = instance.GetType().GetProperties()
            .Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => $"{p.Name}:{p.PropertyType.Name}")
            .ToArray();

        pluginLog.Information($"{label}型: {type}");
        pluginLog.Information($"{label}プロパティ: {string.Join(", ", props)}");
        logged = true;
    }
}
