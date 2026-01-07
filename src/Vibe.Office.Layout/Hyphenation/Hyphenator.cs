using System.Reflection;

namespace Vibe.Office.Layout;

internal sealed class Hyphenator
{
    private readonly HyphenationPatternTrie _trie;
    private readonly Dictionary<string, int[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public int LeftMin { get; }
    public int RightMin { get; }

    private Hyphenator(HyphenationPatternTrie trie, int leftMin, int rightMin)
    {
        _trie = trie;
        LeftMin = leftMin;
        RightMin = rightMin;
    }

    public int[] GetHyphenationPoints(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return Array.Empty<int>();
        }

        if (_cache.TryGetValue(word, out var cached))
        {
            return cached;
        }

        var points = _trie.GetHyphenationPoints(word, LeftMin, RightMin);
        _cache[word] = points;
        return points;
    }

    public static Hyphenator CreateDefault()
    {
        var patterns = HyphenationPatternLoader.LoadDefaultPatterns();
        var trie = HyphenationPatternTrie.Build(patterns);
        return new Hyphenator(trie, 2, 2);
    }
}

internal sealed class HyphenationPatternTrie
{
    private readonly Node _root;

    private HyphenationPatternTrie(Node root)
    {
        _root = root;
    }

    public int[] GetHyphenationPoints(string word, int leftMin, int rightMin)
    {
        if (string.IsNullOrEmpty(word))
        {
            return Array.Empty<int>();
        }

        var normalized = word.ToLowerInvariant();
        var extended = "." + normalized + ".";
        var scores = new int[normalized.Length + 1];

        for (var i = 0; i < extended.Length; i++)
        {
            var node = _root;
            for (var j = i; j < extended.Length; j++)
            {
                var ch = extended[j];
                if (!node.Children.TryGetValue(ch, out var next))
                {
                    break;
                }

                node = next;
                if (node.Values is not null)
                {
                    for (var k = 0; k < node.Values.Length; k++)
                    {
                        var index = i + k - 1;
                        if (index < 0 || index >= scores.Length)
                        {
                            continue;
                        }

                        var value = node.Values[k];
                        if (value > scores[index])
                        {
                            scores[index] = value;
                        }
                    }
                }
            }
        }

        if (leftMin < 0)
        {
            leftMin = 0;
        }

        if (rightMin < 0)
        {
            rightMin = 0;
        }

        var maxIndex = normalized.Length - rightMin;
        var points = new List<int>();
        for (var i = leftMin; i <= maxIndex; i++)
        {
            if ((scores[i] & 1) == 1)
            {
                points.Add(i);
            }
        }

        return points.Count == 0 ? Array.Empty<int>() : points.ToArray();
    }

    public static HyphenationPatternTrie Build(IEnumerable<string> patterns)
    {
        var root = new Node();
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            ParsePattern(pattern, out var letters, out var values);
            var node = root;
            for (var i = 0; i < letters.Length; i++)
            {
                var ch = letters[i];
                if (!node.Children.TryGetValue(ch, out var next))
                {
                    next = new Node();
                    node.Children[ch] = next;
                }

                node = next;
            }

            if (node.Values is null)
            {
                node.Values = values;
            }
            else
            {
                var merged = new int[Math.Max(node.Values.Length, values.Length)];
                for (var i = 0; i < merged.Length; i++)
                {
                    var existing = i < node.Values.Length ? node.Values[i] : 0;
                    var incoming = i < values.Length ? values[i] : 0;
                    merged[i] = Math.Max(existing, incoming);
                }

                node.Values = merged;
            }
        }

        return new HyphenationPatternTrie(root);
    }

    private static void ParsePattern(string pattern, out string letters, out int[] values)
    {
        var lettersBuilder = new System.Text.StringBuilder();
        var valuesList = new List<int>();
        valuesList.Add(0);

        foreach (var ch in pattern)
        {
            if (char.IsDigit(ch))
            {
                valuesList[^1] = ch - '0';
                continue;
            }

            lettersBuilder.Append(ch);
            valuesList.Add(0);
        }

        letters = lettersBuilder.ToString();
        values = valuesList.ToArray();
    }

    private sealed class Node
    {
        public Dictionary<char, Node> Children { get; } = new();
        public int[]? Values { get; set; }
    }
}

internal static class HyphenationPatternLoader
{
    private const string DefaultResourceName = "Vibe.Office.Layout.Hyphenation.en-us.pat.txt";

    public static IEnumerable<string> LoadDefaultPatterns()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(DefaultResourceName);
        if (stream is null)
        {
            return Array.Empty<string>();
        }

        using var reader = new StreamReader(stream);
        var patterns = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('%') || line.StartsWith('#'))
            {
                continue;
            }

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                patterns.Add(token);
            }
        }

        return patterns;
    }
}
