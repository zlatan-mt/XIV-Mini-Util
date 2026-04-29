using System.Collections;

namespace ShopDataSnapshot.Core;

internal static class SnapshotReflection
{
    public static uint GetRowId(object? value)
    {
        if (value == null)
        {
            return 0;
        }

        if (value is uint directUint)
        {
            return directUint;
        }

        if (value is int directInt && directInt >= 0)
        {
            return (uint)directInt;
        }

        var type = value.GetType();
        foreach (var propertyName in new[] { "RowId", "Id", "ItemId", "ItemID" })
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
            {
                continue;
            }

            var rowId = ConvertUnsigned(property.GetValue(value));
            if (rowId != 0)
            {
                return rowId;
            }
        }

        foreach (var propertyName in new[] { "ValueNullable", "Value", "Item" })
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
            {
                continue;
            }

            uint rowId;
            try
            {
                rowId = GetRowId(property.GetValue(value));
            }
            catch
            {
                continue;
            }

            if (rowId != 0)
            {
                return rowId;
            }
        }

        return 0;
    }

    public static IEnumerable<uint> GetItemIdsFromProperty(object value, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = value.GetType().GetProperty(propertyName);
            if (property == null)
            {
                continue;
            }

            var propertyValue = property.GetValue(value);
            if (propertyValue is IEnumerable enumerable and not string)
            {
                foreach (var entry in enumerable)
                {
                    var id = GetRowId(entry);
                    if (id != 0)
                    {
                        yield return id;
                    }
                }
            }
            else
            {
                var id = GetRowId(propertyValue);
                if (id != 0)
                {
                    yield return id;
                }
            }
        }
    }

    public static int GetCount(object? value)
    {
        if (value == null)
        {
            return 0;
        }

        foreach (var propertyName in new[] { "ReceiveCount", "CurrencyCost", "Count", "Quantity", "Amount" })
        {
            var property = value.GetType().GetProperty(propertyName);
            if (property == null)
            {
                continue;
            }

            var count = ConvertSigned(property.GetValue(value));
            if (count > 0)
            {
                return count;
            }
        }

        return 0;
    }

    private static uint ConvertUnsigned(object? value)
    {
        return value switch
        {
            uint uintValue => uintValue,
            int intValue when intValue >= 0 => (uint)intValue,
            ushort ushortValue => ushortValue,
            short shortValue when shortValue >= 0 => (uint)shortValue,
            byte byteValue => byteValue,
            _ => 0,
        };
    }

    private static int ConvertSigned(object? value)
    {
        return value switch
        {
            int intValue => intValue,
            uint uintValue when uintValue <= int.MaxValue => (int)uintValue,
            ushort ushortValue => ushortValue,
            short shortValue => shortValue,
            byte byteValue => byteValue,
            _ => 0,
        };
    }
}
