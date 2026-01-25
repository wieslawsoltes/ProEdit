using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public sealed class CitationSourceManager : ICitationSourceManager
{
    private readonly Window _owner;

    public CitationSourceManager(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public async ValueTask<CitationSourceCatalog?> EditSourcesAsync(CitationSourceCatalog? currentCatalog)
    {
        var dialog = new CitationSourceManagerDialog(currentCatalog);
        return await dialog.ShowDialog<CitationSourceCatalog?>(_owner);
    }

    public async ValueTask<string?> PickSourceAsync(CitationSourceCatalog catalog)
    {
        if (catalog is null || catalog.Sources.Count == 0)
        {
            return null;
        }

        var items = BuildPickerItems(catalog);
        var dialog = new PickerDialog("Insert Citation", items);
        var result = await dialog.ShowDialog<PickerItem?>(_owner);
        return result?.Id;
    }

    private static IReadOnlyList<PickerItem> BuildPickerItems(CitationSourceCatalog catalog)
    {
        var items = new List<PickerItem>();
        foreach (var source in catalog.Sources)
        {
            var label = ResolveSourceLabel(source);
            var description = ResolveSourceDescription(source);
            var id = string.IsNullOrWhiteSpace(source.Tag) ? label : source.Tag;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("N");
            }

            items.Add(new PickerItem(id, label, description, IconKey: "RibbonIcon.Bookmark"));
        }

        return items;
    }

    private static string ResolveSourceLabel(CitationSource source)
    {
        if (!string.IsNullOrWhiteSpace(source.Tag))
        {
            return source.Tag;
        }

        var title = source.GetField("Title");
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var author = source.GetField("Author");
        if (!string.IsNullOrWhiteSpace(author))
        {
            return author;
        }

        return "Source";
    }

    private static string ResolveSourceDescription(CitationSource source)
    {
        var author = source.GetField("Author");
        var year = source.GetField("Year");
        if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(year))
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0}, {1}", author, year);
        }

        return source.SourceType ?? string.Empty;
    }
}
