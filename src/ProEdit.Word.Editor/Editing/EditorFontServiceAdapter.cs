using ProEdit.Documents;
using ProEdit.Editing;

namespace ProEdit.Word.Editor.Editing;

public sealed class EditorFontServiceAdapter : IFontService
{
    private readonly IEditorSession _session;

    public EditorFontServiceAdapter(IEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public IReadOnlyList<EditorFontFamilyInfo> GetFontFamilies()
    {
        var documentFonts = _session.Document.Fonts.FontTable;
        var families = new List<EditorFontFamilyInfo>(documentFonts.Count + 8);
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
}
