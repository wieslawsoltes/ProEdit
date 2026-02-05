using System.Linq;
using Vibe.Office.Collaboration.Persistence;
using Vibe.Office.Documents;

namespace Vibe.Office.Collaboration;

public sealed class CollabBlockOpApplier
{
    private readonly CollabBlockSerializer _serializer;

    public CollabBlockOpApplier(CollabBlockSerializer? serializer = null)
    {
        _serializer = serializer ?? new CollabBlockSerializer();
    }

    public bool Apply(Document document, ICollabOp op)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(op);

        return op switch
        {
            InsertBlockOp insert => ApplyInsert(document, insert),
            DeleteBlockOp delete => ApplyDelete(document, delete),
            ReplaceBlockOp replace => ApplyReplace(document, replace),
            _ => false
        };
    }

    public bool ApplyInsert(Document document, InsertBlockOp op)
    {
        if (!TryResolveContainer(document, op.ParentNodeId, out var blocks))
        {
            return false;
        }

        var index = ResolveIndex(op.Position, blocks.Count);
        var block = CreateBlock(document, op);
        if (block is null)
        {
            return false;
        }

        blocks.Insert(index, block);
        return true;
    }

    public bool ApplyDelete(Document document, DeleteBlockOp op)
    {
        if (!TryResolveContainer(document, op.ParentNodeId, out var blocks))
        {
            return false;
        }

        if (blocks.Count == 0)
        {
            return false;
        }

        var index = ResolveIndex(op.Position, blocks.Count - 1);
        if (index >= 0 && index < blocks.Count && blocks[index].NodeId == op.BlockNodeId)
        {
            blocks.RemoveAt(index);
            return true;
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].NodeId == op.BlockNodeId)
            {
                blocks.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    public bool ApplyReplace(Document document, ReplaceBlockOp op)
    {
        if (!TryFindBlock(document, op.BlockNodeId, out var blocks, out var index))
        {
            return false;
        }

        var payload = op.Payload;
        if (payload is null || payload.Length == 0)
        {
            return false;
        }

        var result = _serializer.Deserialize(payload);
        var block = result.Block;
        block.NodeId = op.BlockNodeId;
        MergeResources(document, result.Resources, block);
        blocks[index] = block;
        return true;
    }

    private Block? CreateBlock(Document document, InsertBlockOp op)
    {
        if (op.Payload is null || op.Payload.Length == 0)
        {
            return CreateFallbackBlock(op.BlockType);
        }

        var result = _serializer.Deserialize(op.Payload);
        var block = result.Block;
        MergeResources(document, result.Resources, block);
        return block;
    }

    private static Block CreateFallbackBlock(string blockType)
    {
        return blockType switch
        {
            nameof(ParagraphBlock) => new ParagraphBlock(),
            nameof(TableBlock) => new TableBlock(),
            nameof(PageBreakBlock) => new PageBreakBlock(),
            nameof(ColumnBreakBlock) => new ColumnBreakBlock(),
            nameof(SectionBreakBlock) => new SectionBreakBlock(),
            nameof(AltChunkBlock) => new AltChunkBlock(),
            _ => new ParagraphBlock()
        };
    }

    private static bool TryResolveContainer(Document document, Guid containerId, out IList<Block> blocks)
    {
        if (CollabContainerCatalog.TryResolve(document, containerId, out blocks))
        {
            return true;
        }

        CollabContainerCatalog.EnsureNoteContainer(document, containerId);
        return CollabContainerCatalog.TryResolve(document, containerId, out blocks);
    }

    private static int ResolveIndex(PositionToken token, int maxIndex)
    {
        if (CollabPositionToken.TryGetIndex(token, out var index))
        {
            return Math.Clamp(index, 0, maxIndex);
        }

        return Math.Clamp(maxIndex, 0, maxIndex);
    }

    private static bool TryFindBlock(Document document, Guid nodeId, out IList<Block> blocks, out int index)
    {
        blocks = Array.Empty<Block>();
        index = -1;

        foreach (var container in CollabContainerCatalog.Enumerate(document))
        {
            var list = container.Blocks;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].NodeId == nodeId)
                {
                    blocks = list;
                    index = i;
                    return true;
                }
            }
        }

        return false;
    }

    private static void MergeResources(Document document, Document incoming, Block block)
    {
        MergeFonts(incoming.Fonts, document.Fonts);
        MergeThemeColors(incoming.ThemeColors, document.ThemeColors);
        var listMap = MergeListDefinitions(document, incoming.ListDefinitions, block);
        if (listMap.Count > 0)
        {
            RemapListIdsInStyles(incoming.Styles, listMap);
        }

        MergeStyles(incoming.Styles, document.Styles);
    }

    private static void MergeStyles(DocumentStyles source, DocumentStyles target)
    {
        foreach (var pair in source.ParagraphStyles)
        {
            if (!target.ParagraphStyles.ContainsKey(pair.Key))
            {
                target.ParagraphStyles[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in source.CharacterStyles)
        {
            if (!target.CharacterStyles.ContainsKey(pair.Key))
            {
                target.CharacterStyles[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in source.TableStyles)
        {
            if (!target.TableStyles.ContainsKey(pair.Key))
            {
                target.TableStyles[pair.Key] = pair.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(target.DefaultParagraphStyleId))
        {
            target.DefaultParagraphStyleId = source.DefaultParagraphStyleId;
        }

        if (string.IsNullOrWhiteSpace(target.DefaultCharacterStyleId))
        {
            target.DefaultCharacterStyleId = source.DefaultCharacterStyleId;
        }

        if (string.IsNullOrWhiteSpace(target.DefaultTableStyleId))
        {
            target.DefaultTableStyleId = source.DefaultTableStyleId;
        }
    }

    private static void MergeFonts(DocumentFonts source, DocumentFonts target)
    {
        foreach (var pair in source.FontTable)
        {
            if (!target.FontTable.ContainsKey(pair.Key))
            {
                target.FontTable[pair.Key] = pair.Value;
            }
        }

        if (!target.Theme.HasValues)
        {
            foreach (var pair in source.Theme.Entries)
            {
                target.Theme.Set(pair.Key, pair.Value);
            }
        }
    }

    private static void MergeThemeColors(DocumentThemeColorMap source, DocumentThemeColorMap target)
    {
        if (target.HasValues)
        {
            return;
        }

        foreach (var pair in source.Overrides)
        {
            target.Set(pair.Key, pair.Value);
        }
    }

    private static Dictionary<int, int> MergeListDefinitions(
        Document document,
        Dictionary<int, ListDefinition> incoming,
        Block block)
    {
        if (incoming.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var map = new Dictionary<int, int>();

        foreach (var pair in incoming)
        {
            if (document.ListDefinitions.TryGetValue(pair.Key, out var existing))
            {
                if (AreListDefinitionsEquivalent(existing, pair.Value))
                {
                    continue;
                }

                document.ListDefinitions[pair.Key] = pair.Value.Clone();
                continue;
            }

            document.ListDefinitions[pair.Key] = pair.Value.Clone();
        }

        return map;
    }

    private static void RemapListIds(Block block, IReadOnlyDictionary<int, int> map)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                if (paragraph.ListInfo?.ListId is int listId && map.TryGetValue(listId, out var newId))
                {
                    paragraph.ListInfo = CloneListInfo(paragraph.ListInfo, newId);
                }

                break;
            case TableBlock table:
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var child in cell.Blocks)
                        {
                            RemapListIds(child, map);
                        }
                    }
                }

                break;
        }
    }

    private static void RemapListIdsInStyles(DocumentStyles styles, IReadOnlyDictionary<int, int> map)
    {
        foreach (var pair in styles.ParagraphStyles)
        {
            if (pair.Value.ListId is int listId && map.TryGetValue(listId, out var newId))
            {
                pair.Value.ListId = newId;
            }
        }
    }

    private static ListInfo CloneListInfo(ListInfo source, int newListId)
    {
        return new ListInfo(source.Kind, source.Level, newListId)
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

    private static bool AreListDefinitionsEquivalent(ListDefinition existing, ListDefinition incoming)
    {
        if (existing.Levels.Count != incoming.Levels.Count)
        {
            return false;
        }

        foreach (var pair in existing.Levels)
        {
            if (!incoming.Levels.TryGetValue(pair.Key, out var other))
            {
                return false;
            }

            if (!AreListLevelsEquivalent(pair.Value, other))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreListLevelsEquivalent(ListLevelDefinition existing, ListLevelDefinition incoming)
    {
        if (existing.Level != incoming.Level)
        {
            return false;
        }

        if (existing.Format != incoming.Format)
        {
            return false;
        }

        if (!string.Equals(existing.LevelText, incoming.LevelText, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(existing.BulletSymbol, incoming.BulletSymbol, StringComparison.Ordinal))
        {
            return false;
        }

        if (existing.StartAt != incoming.StartAt)
        {
            return false;
        }

        if (!AreNullableFloatsClose(existing.LeftIndent, incoming.LeftIndent))
        {
            return false;
        }

        if (!AreNullableFloatsClose(existing.HangingIndent, incoming.HangingIndent))
        {
            return false;
        }

        if (!AreNullableFloatsClose(existing.TabStop, incoming.TabStop))
        {
            return false;
        }

        return true;
    }

    private static bool AreNullableFloatsClose(float? left, float? right)
    {
        if (!left.HasValue && !right.HasValue)
        {
            return true;
        }

        if (left.HasValue != right.HasValue)
        {
            return false;
        }

        var delta = MathF.Abs(left!.Value - right!.Value);
        return delta <= 0.01f;
    }
}
