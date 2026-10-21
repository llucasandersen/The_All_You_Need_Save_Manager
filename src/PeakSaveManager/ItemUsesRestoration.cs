using System;
using System.Collections.Generic;

namespace PEAKSaveManager;

internal static class ItemUsesRestoration
{
    internal static OptionableIntItemData Normalize(DataEntryValue value)
    {
        if (value is OptionableIntItemData optional)
        {
            return optional;
        }

        // Older restores could write an IntItemData back into a subsequent save.
        return value is IntItemData legacy ? FromLegacy(legacy.Value) : null;
    }

    internal static void ApplyLegacy(ItemInstanceData data, int? itemUses, bool restoredGenericItemUses)
    {
        if (data == null || restoredGenericItemUses || !itemUses.HasValue)
        {
            return;
        }

        data.data ??= new Dictionary<DataEntryKey, DataEntryValue>();
        data.data[DataEntryKey.ItemUses] = FromLegacy(itemUses.Value);
    }

    private static OptionableIntItemData FromLegacy(int value)
    {
        // Item.Start uses -1 for unlimited uses; zero is a present, exhausted value.
        return new OptionableIntItemData
        {
            HasData = value >= 0,
            Value = Math.Max(-1, value)
        };
    }
}
