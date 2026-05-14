using SkiaSharp;
using ProEdit.Documents;
using ProEdit.Editing;

namespace ProEdit.Word.Avalonia;

public sealed class SkiaFontServiceAdapter : IFontService
{
    private readonly IEditorSession _session;
    private readonly IReadOnlyList<string> _systemFonts;
    private readonly HashSet<string> _systemFontSet;

    public SkiaFontServiceAdapter(IEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _systemFontSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _systemFonts = LoadSystemFonts(_systemFontSet);
    }

    public IReadOnlyList<EditorFontFamilyInfo> GetFontFamilies()
    {
        var documentFonts = _session.Document.Fonts.FontTable;
        var families = new List<EditorFontFamilyInfo>(documentFonts.Count + _systemFonts.Count + 8);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFont(string? name, bool isEmbedded)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (seen.Add(name))
            {
                families.Add(new EditorFontFamilyInfo(name, isEmbedded));
            }
        }

        foreach (var entry in documentFonts)
        {
            var definition = entry.Value;
            var isEmbedded = definition.Regular is not null
                             || definition.Bold is not null
                             || definition.Italic is not null
                             || definition.BoldItalic is not null;
            AddFont(definition.Name, isEmbedded);
            AddFont(definition.AltName, isEmbedded);
        }

        AddThemeFont(DocThemeFont.MajorAscii);
        AddThemeFont(DocThemeFont.MajorHighAnsi);
        AddThemeFont(DocThemeFont.MajorEastAsia);
        AddThemeFont(DocThemeFont.MajorBidi);
        AddThemeFont(DocThemeFont.MinorAscii);
        AddThemeFont(DocThemeFont.MinorHighAnsi);
        AddThemeFont(DocThemeFont.MinorEastAsia);
        AddThemeFont(DocThemeFont.MinorBidi);

        AddFont(_session.Document.DefaultTextStyle.FontFamily, isEmbedded: false);

        foreach (var family in _systemFonts)
        {
            AddFont(family, isEmbedded: false);
        }

        families.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return families;

        void AddThemeFont(DocThemeFont theme)
        {
            if (_session.Document.Fonts.Theme.TryGet(theme, out var family))
            {
                AddFont(family, isEmbedded: false);
            }
        }
    }

    public bool IsFontAvailable(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return false;
        }

        if (_systemFontSet.Contains(family))
        {
            return true;
        }

        if (_session.Document.Fonts.FontTable.ContainsKey(family))
        {
            return true;
        }

        if (_session.Document.Fonts.Theme.Get(DocThemeFont.MajorAscii) == family
            || _session.Document.Fonts.Theme.Get(DocThemeFont.MajorHighAnsi) == family
            || _session.Document.Fonts.Theme.Get(DocThemeFont.MajorEastAsia) == family
            || _session.Document.Fonts.Theme.Get(DocThemeFont.MajorBidi) == family
            || _session.Document.Fonts.Theme.Get(DocThemeFont.MinorAscii) == family
            || _session.Document.Fonts.Theme.Get(DocThemeFont.MinorHighAnsi) == family
            || _session.Document.Fonts.Theme.Get(DocThemeFont.MinorEastAsia) == family
            || _session.Document.Fonts.Theme.Get(DocThemeFont.MinorBidi) == family)
        {
            return true;
        }

        return string.Equals(_session.Document.DefaultTextStyle.FontFamily, family, StringComparison.OrdinalIgnoreCase);
    }

    public bool HasEmbeddedFont(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return false;
        }

        if (!_session.Document.Fonts.FontTable.TryGetValue(family, out var definition))
        {
            return false;
        }

        return definition.Regular is not null
               || definition.Bold is not null
               || definition.Italic is not null
               || definition.BoldItalic is not null;
    }

    private static IReadOnlyList<string> LoadSystemFonts(HashSet<string> set)
    {
        var manager = SKFontManager.Default;
        if (manager is null)
        {
            return Array.Empty<string>();
        }

        var families = manager.FontFamilies;
        var list = families is ICollection<string> collection
            ? new List<string>(collection.Count)
            : new List<string>();
        foreach (var family in families)
        {
            if (string.IsNullOrWhiteSpace(family))
            {
                continue;
            }

            if (set.Add(family))
            {
                list.Add(family);
            }
        }

        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }
}
