using System;
using System.Collections.Generic;

namespace Vibe.Office.Documents;

public sealed class CitationSourceCatalog
{
    public List<CitationSource> Sources { get; } = new List<CitationSource>();

    public CitationSourceCatalog Clone()
    {
        var clone = new CitationSourceCatalog();
        foreach (var source in Sources)
        {
            clone.Sources.Add(source.Clone());
        }

        return clone;
    }

    public CitationSource? FindByTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        foreach (var source in Sources)
        {
            if (string.Equals(source.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }
        }

        return null;
    }

    public void EnsureUniqueTags(string prefix = "Source")
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Sources)
        {
            var tag = source.Tag;
            if (string.IsNullOrWhiteSpace(tag) || seen.Contains(tag))
            {
                source.Tag = GenerateUniqueTag(prefix, seen);
            }
            else
            {
                seen.Add(tag);
            }
        }
    }

    public string GenerateUniqueTag(string prefix, HashSet<string>? existing = null)
    {
        var baseTag = string.IsNullOrWhiteSpace(prefix) ? "Source" : prefix.Trim();
        var suffix = 1;
        var candidate = baseTag;
        var lookup = existing ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (lookup.Contains(candidate))
        {
            candidate = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}{1}", baseTag, suffix++);
        }

        lookup.Add(candidate);
        return candidate;
    }
}

public sealed class CitationSource
{
    public string Tag { get; set; } = string.Empty;
    public string SourceType { get; set; } = "Book";
    public string? Guid { get; set; }
    public Dictionary<string, string> Fields { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public CitationSource Clone()
    {
        var clone = new CitationSource
        {
            Tag = Tag,
            SourceType = SourceType,
            Guid = Guid
        };

        foreach (var pair in Fields)
        {
            clone.Fields[pair.Key] = pair.Value;
        }

        return clone;
    }

    public string? GetField(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Fields.TryGetValue(name, out var value) ? value : null;
    }

    public void SetField(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            Fields.Remove(name);
            return;
        }

        Fields[name] = value;
    }
}
