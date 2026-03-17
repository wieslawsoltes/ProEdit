using System.Xml.Linq;
using Vibe.Office.Documents;

namespace Vibe.Office.Reporting.DocumentComposition;

internal static class ReportDocumentFragmentImporter
{
    public static void ImportBlocks(
        Document source,
        Document target,
        List<Block> targetBlocks)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(targetBlocks);

        NormalizeCustomXmlStoreItemIds(source, target);
        MergeStyles(source, target);
        MergeFonts(source, target);
        MergeThemeColors(source, target);
        MergeCustomXml(source, target);
        var listIdMap = MergeListDefinitions(source, target);

        for (var blockIndex = 0; blockIndex < source.Blocks.Count; blockIndex++)
        {
            var clone = DocumentClone.CloneBlock(source.Blocks[blockIndex]);
            if (listIdMap.Count > 0)
            {
                RemapListIds(clone, listIdMap);
            }

            targetBlocks.Add(clone);
        }
    }

    private static void MergeStyles(Document source, Document target)
    {
        var styles = DocumentClone.CloneStyles(source.Styles);

        foreach (var pair in styles.ParagraphStyles)
        {
            target.Styles.ParagraphStyles.TryAdd(pair.Key, pair.Value);
        }

        foreach (var pair in styles.CharacterStyles)
        {
            target.Styles.CharacterStyles.TryAdd(pair.Key, pair.Value);
        }

        foreach (var pair in styles.TableStyles)
        {
            target.Styles.TableStyles.TryAdd(pair.Key, pair.Value);
        }

        target.Styles.DefaultParagraphStyleId ??= styles.DefaultParagraphStyleId;
        target.Styles.DefaultCharacterStyleId ??= styles.DefaultCharacterStyleId;
        target.Styles.DefaultTableStyleId ??= styles.DefaultTableStyleId;
    }

    private static void MergeFonts(Document source, Document target)
    {
        var fonts = DocumentClone.CloneFonts(source.Fonts);
        foreach (var pair in fonts.FontTable)
        {
            target.Fonts.FontTable.TryAdd(pair.Key, pair.Value);
        }

        foreach (var pair in fonts.Theme.Entries)
        {
            if (target.Fonts.Theme.Get(pair.Key) is null)
            {
                target.Fonts.Theme.Set(pair.Key, pair.Value);
            }
        }
    }

    private static void MergeThemeColors(Document source, Document target)
    {
        var colors = DocumentClone.CloneThemeColors(source.ThemeColors);
        foreach (var pair in colors.Overrides)
        {
            if (target.ThemeColors.Get(pair.Key) is null)
            {
                target.ThemeColors.Set(pair.Key, pair.Value);
            }
        }
    }

    private static void MergeCustomXml(Document source, Document target)
    {
        foreach (var pair in source.CustomXmlParts)
        {
            if (!target.CustomXmlParts.ContainsKey(pair.Key))
            {
                target.CustomXmlParts[pair.Key] = new XDocument(pair.Value);
            }
        }
    }

    private static Dictionary<int, int> MergeListDefinitions(Document source, Document target)
    {
        var map = new Dictionary<int, int>();
        if (source.ListDefinitions.Count == 0)
        {
            return map;
        }

        var nextId = target.ListDefinitions.Count == 0 ? 1 : target.ListDefinitions.Keys.Max() + 1;
        foreach (var pair in source.ListDefinitions)
        {
            var newId = pair.Key;
            if (target.ListDefinitions.ContainsKey(newId))
            {
                newId = nextId++;
                map[pair.Key] = newId;
            }

            target.ListDefinitions[newId] = CloneListDefinition(pair.Value, newId);
        }

        return map;
    }

    private static void NormalizeCustomXmlStoreItemIds(Document source, Document target)
    {
        if (source.CustomXmlParts.Count == 0)
        {
            return;
        }

        Dictionary<string, string>? map = null;
        foreach (var pair in source.CustomXmlParts.ToArray())
        {
            if (!target.CustomXmlParts.TryGetValue(pair.Key, out var existing))
            {
                continue;
            }

            if (XNode.DeepEquals(existing, pair.Value))
            {
                continue;
            }

            map ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var newKey = CreateUniqueStoreItemId(pair.Key, target, source);
            map[pair.Key] = newKey;
            source.CustomXmlParts.Remove(pair.Key);
            source.CustomXmlParts[newKey] = pair.Value;
        }

        if (map is null || map.Count == 0)
        {
            return;
        }

        RemapContentControlStoreItemIds(source.Blocks, map);
    }

    private static string CreateUniqueStoreItemId(
        string baseKey,
        Document target,
        Document source)
    {
        while (true)
        {
            var candidate = $"{baseKey}-{Guid.NewGuid():N}";
            if (!target.CustomXmlParts.ContainsKey(candidate)
                && !source.CustomXmlParts.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    private static void RemapContentControlStoreItemIds(
        IReadOnlyList<Block> blocks,
        IReadOnlyDictionary<string, string> map)
    {
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            switch (blocks[blockIndex])
            {
                case ParagraphBlock paragraph:
                    RemapParagraphStoreItemIds(paragraph, map);
                    break;
                case TableBlock table:
                    for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                    {
                        var row = table.Rows[rowIndex];
                        if (row.ContentControl?.DataBinding?.StoreItemId is string rowStoreItemId
                            && map.TryGetValue(rowStoreItemId, out var remappedRowStoreItemId))
                        {
                            row.ContentControl.DataBinding.StoreItemId = remappedRowStoreItemId;
                        }

                        for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                        {
                            var cell = row.Cells[cellIndex];
                            if (cell.ContentControl?.DataBinding?.StoreItemId is string cellStoreItemId
                                && map.TryGetValue(cellStoreItemId, out var remappedCellStoreItemId))
                            {
                                cell.ContentControl.DataBinding.StoreItemId = remappedCellStoreItemId;
                            }

                            RemapContentControlStoreItemIds(cell.Blocks, map);
                        }
                    }

                    break;
                case ContentControlStartBlock startBlock:
                    if (startBlock.Properties.DataBinding?.StoreItemId is string blockStoreItemId
                        && map.TryGetValue(blockStoreItemId, out var remappedBlockStoreItemId))
                    {
                        startBlock.Properties.DataBinding.StoreItemId = remappedBlockStoreItemId;
                    }

                    break;
            }
        }
    }

    private static void RemapParagraphStoreItemIds(
        ParagraphBlock paragraph,
        IReadOnlyDictionary<string, string> map)
    {
        for (var inlineIndex = 0; inlineIndex < paragraph.Inlines.Count; inlineIndex++)
        {
            if (paragraph.Inlines[inlineIndex] is ContentControlStartInline controlStart
                && controlStart.Properties.DataBinding?.StoreItemId is string storeItemId
                && map.TryGetValue(storeItemId, out var remappedStoreItemId))
            {
                controlStart.Properties.DataBinding.StoreItemId = remappedStoreItemId;
            }
        }
    }

    private static void RemapListIds(
        Block block,
        IReadOnlyDictionary<int, int> map)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                if (paragraph.ListInfo?.ListId is int listId && map.TryGetValue(listId, out var newListId))
                {
                    paragraph.ListInfo = CloneListInfoWithId(paragraph.ListInfo, newListId);
                }

                break;
            case TableBlock table:
                for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    var row = table.Rows[rowIndex];
                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        var cell = row.Cells[cellIndex];
                        for (var blockIndex = 0; blockIndex < cell.Blocks.Count; blockIndex++)
                        {
                            RemapListIds(cell.Blocks[blockIndex], map);
                        }
                    }
                }

                break;
        }
    }

    private static ListInfo CloneListInfoWithId(ListInfo source, int newId)
    {
        return new ListInfo(source.Kind, source.Level, newId)
        {
            NumberFormat = source.NumberFormat,
            LevelText = source.LevelText,
            BulletSymbol = source.BulletSymbol,
            StartAt = source.StartAt,
            LeftIndent = source.LeftIndent,
            HangingIndent = source.HangingIndent,
            TabStop = source.TabStop
        };
    }

    private static ListDefinition CloneListDefinition(ListDefinition source, int newId)
    {
        var clone = new ListDefinition(newId);
        foreach (var pair in source.Levels)
        {
            clone.Levels[pair.Key] = pair.Value.Clone();
        }

        return clone;
    }
}
