using System.Globalization;
using System.Text;

namespace Vibe.Office.Layout;

public enum BidiDirection
{
    Ltr,
    Rtl
}

public readonly record struct BidiSpan(int Start, int Length, int Level);

public static class TextBidi
{
    private enum BidiClass
    {
        L,
        R,
        Number,
        Neutral
    }

    public static bool ResolveBaseIsRtl(ReadOnlySpan<char> text, bool? explicitRtl)
    {
        if (explicitRtl.HasValue)
        {
            return explicitRtl.Value;
        }

        return FindFirstStrongDirection(text) == BidiDirection.Rtl;
    }

    public static BidiDirection FindFirstStrongDirection(ReadOnlySpan<char> text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!Rune.TryDecodeFromUtf16(text[index..], out var rune, out var consumed))
            {
                rune = new Rune(text[index]);
                consumed = 1;
            }

            var category = CharUnicodeInfo.GetBidiCategory(rune.Value);
            var klass = Classify(category);
            if (klass == BidiClass.L)
            {
                return BidiDirection.Ltr;
            }

            if (klass == BidiClass.R)
            {
                return BidiDirection.Rtl;
            }

            index += consumed;
        }

        return BidiDirection.Ltr;
    }

    public static List<BidiSpan> GetBidiSpans(ReadOnlySpan<char> text, bool baseRtl)
    {
        var spans = new List<BidiSpan>();
        if (text.IsEmpty)
        {
            return spans;
        }

        var baseLevel = baseRtl ? 1 : 0;
        var lastStrong = baseRtl ? BidiClass.R : BidiClass.L;
        var spanStart = 0;
        var spanLevel = -1;
        var index = 0;

        while (index < text.Length)
        {
            if (!Rune.TryDecodeFromUtf16(text[index..], out var rune, out var consumed))
            {
                rune = new Rune(text[index]);
                consumed = 1;
            }

            var category = CharUnicodeInfo.GetBidiCategory(rune.Value);
            var klass = Classify(category);
            var effective = klass;
            if (klass == BidiClass.Neutral)
            {
                effective = lastStrong;
            }
            else if (klass == BidiClass.Number)
            {
                effective = BidiClass.L;
            }
            else
            {
                lastStrong = klass;
            }

            var level = baseLevel;
            if (baseRtl)
            {
                level = effective == BidiClass.L ? 2 : 1;
            }
            else
            {
                level = effective == BidiClass.R ? 1 : 0;
            }

            if (spanLevel < 0)
            {
                spanLevel = level;
                spanStart = index;
            }
            else if (level != spanLevel)
            {
                spans.Add(new BidiSpan(spanStart, index - spanStart, spanLevel));
                spanStart = index;
                spanLevel = level;
            }

            index += consumed;
        }

        if (spanStart < text.Length)
        {
            spans.Add(new BidiSpan(spanStart, text.Length - spanStart, spanLevel < 0 ? baseLevel : spanLevel));
        }

        return spans;
    }

    public static void ReorderByLevels<T>(List<T> items, Func<T, int> getLevel, int baseLevel)
    {
        if (items.Count <= 1)
        {
            return;
        }

        var maxLevel = baseLevel;
        for (var i = 0; i < items.Count; i++)
        {
            maxLevel = Math.Max(maxLevel, getLevel(items[i]));
        }

        for (var level = maxLevel; level > baseLevel; level--)
        {
            var index = 0;
            while (index < items.Count)
            {
                while (index < items.Count && getLevel(items[index]) < level)
                {
                    index++;
                }

                if (index >= items.Count)
                {
                    break;
                }

                var start = index;
                while (index < items.Count && getLevel(items[index]) >= level)
                {
                    index++;
                }

                var count = index - start;
                if (count > 1)
                {
                    items.Reverse(start, count);
                }
            }
        }
    }

    private static BidiClass Classify(BidiCategory category)
    {
        return category switch
        {
            BidiCategory.LeftToRight => BidiClass.L,
            BidiCategory.RightToLeft => BidiClass.R,
            BidiCategory.RightToLeftArabic => BidiClass.R,
            BidiCategory.EuropeanNumber => BidiClass.Number,
            BidiCategory.ArabicNumber => BidiClass.Number,
            _ => BidiClass.Neutral
        };
    }
}
