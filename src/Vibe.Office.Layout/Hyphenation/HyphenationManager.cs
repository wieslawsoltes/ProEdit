using System.Collections.Concurrent;
using System.Threading;

namespace Vibe.Office.Layout;

internal static class HyphenationManager
{
    private const string ResourcePrefix = "Vibe.Office.Layout.Hyphenation.";
    private static readonly Dictionary<string, HyphenationPatternInfo> Patterns = CreatePatternMap();
    private static readonly ConcurrentDictionary<string, Lazy<Hyphenator?>> Hyphenators =
        new ConcurrentDictionary<string, Lazy<Hyphenator?>>(StringComparer.OrdinalIgnoreCase);

    public static Hyphenator? ResolveHyphenator(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = NormalizeLanguageTag(language);
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (!TryResolvePattern(normalized, out var info))
        {
            return null;
        }

        var lazy = Hyphenators.GetOrAdd(info.ResourceName, _ =>
            new Lazy<Hyphenator?>(() => Hyphenator.Create(info.ResourceName, info.LeftMin, info.RightMin),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private static bool TryResolvePattern(string normalized, out HyphenationPatternInfo info)
    {
        if (Patterns.TryGetValue(normalized, out info))
        {
            return true;
        }

        var baseLanguage = GetBaseLanguage(normalized);
        if (!string.Equals(baseLanguage, normalized, StringComparison.Ordinal)
            && Patterns.TryGetValue(baseLanguage, out info))
        {
            return true;
        }

        info = default;
        return false;
    }

    private static string NormalizeLanguageTag(string language)
    {
        var trimmed = language.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var normalized = trimmed.Replace('_', '-').ToLowerInvariant();
        return normalized;
    }

    private static string GetBaseLanguage(string normalized)
    {
        var separatorIndex = normalized.IndexOf('-');
        if (separatorIndex > 0)
        {
            return normalized[..separatorIndex];
        }

        return normalized;
    }

    private static Dictionary<string, HyphenationPatternInfo> CreatePatternMap()
    {
        var map = new Dictionary<string, HyphenationPatternInfo>(StringComparer.OrdinalIgnoreCase);

        AddPattern(map, "en-us", "en-us.pat.txt", 2, 3, "en");
        AddPattern(map, "en-gb", "en-gb.pat.txt", 2, 3);
        AddPattern(map, "de-1996", "de-1996.pat.txt", 2, 2, "de");
        AddPattern(map, "fr", "fr.pat.txt", 2, 2);
        AddPattern(map, "es", "es.pat.txt", 2, 2);
        AddPattern(map, "it", "it.pat.txt", 2, 2);
        AddPattern(map, "pt", "pt.pat.txt", 2, 3);

        return map;
    }

    private static void AddPattern(
        Dictionary<string, HyphenationPatternInfo> map,
        string tag,
        string resourceFile,
        int leftMin,
        int rightMin,
        params string[] aliases)
    {
        var resourceName = ResourcePrefix + resourceFile;
        var info = new HyphenationPatternInfo(resourceName, leftMin, rightMin);
        map[tag] = info;
        for (var i = 0; i < aliases.Length; i++)
        {
            map[aliases[i]] = info;
        }
    }

    private readonly record struct HyphenationPatternInfo(string ResourceName, int LeftMin, int RightMin);
}
