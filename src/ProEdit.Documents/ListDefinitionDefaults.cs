namespace ProEdit.Documents;

public static class ListDefinitionDefaults
{
    private static readonly ListNumberFormat[] NumberFormats =
    {
        ListNumberFormat.Decimal,
        ListNumberFormat.LowerLetter,
        ListNumberFormat.LowerRoman,
        ListNumberFormat.UpperLetter,
        ListNumberFormat.UpperRoman,
        ListNumberFormat.Decimal,
        ListNumberFormat.LowerLetter,
        ListNumberFormat.LowerRoman,
        ListNumberFormat.UpperLetter
    };

    private static readonly string[] BulletSymbols =
    {
        "•",
        "◦",
        "▪",
        "–",
        "•",
        "◦",
        "▪",
        "–",
        "•"
    };

    public static ListDefinition CreateBulleted(int id, int levelCount = 9)
    {
        var definition = new ListDefinition(id);
        EnsureLevels(definition, ListKind.Bullet, Math.Max(0, levelCount - 1), multilevel: false);
        return definition;
    }

    public static ListDefinition CreateNumbered(int id, bool multilevel, int levelCount = 9)
    {
        var definition = new ListDefinition(id);
        EnsureLevels(definition, ListKind.Numbered, Math.Max(0, levelCount - 1), multilevel);
        return definition;
    }

    public static void EnsureLevels(ListDefinition definition, ListKind kind, int maxLevel, bool multilevel)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var clampedMax = Math.Max(0, maxLevel);
        for (var level = 0; level <= clampedMax; level++)
        {
            if (definition.Levels.ContainsKey(level))
            {
                continue;
            }

            definition.Levels[level] = kind switch
            {
                ListKind.Bullet => BuildBulletLevel(level),
                ListKind.Numbered => BuildNumberedLevel(level, multilevel),
                _ => BuildNumberedLevel(level, multilevel)
            };
        }
    }

    private static ListLevelDefinition BuildBulletLevel(int level)
    {
        var definition = new ListLevelDefinition(level)
        {
            Format = ListNumberFormat.Bullet
        };

        var symbol = BulletSymbols[level % BulletSymbols.Length];
        definition.BulletSymbol = symbol;
        definition.LevelText = symbol;
        return definition;
    }

    private static ListLevelDefinition BuildNumberedLevel(int level, bool multilevel)
    {
        var definition = new ListLevelDefinition(level)
        {
            Format = NumberFormats[level % NumberFormats.Length]
        };

        definition.LevelText = multilevel
            ? BuildMultiLevelText(level)
            : $"%{level + 1}.";
        return definition;
    }

    private static string BuildMultiLevelText(int level)
    {
        var parts = new string[level + 1];
        for (var i = 0; i <= level; i++)
        {
            parts[i] = $"%{i + 1}";
        }

        return string.Concat(string.Join('.', parts), ".");
    }
}
