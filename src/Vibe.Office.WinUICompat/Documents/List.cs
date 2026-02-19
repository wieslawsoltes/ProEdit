using System.Collections.ObjectModel;

namespace Vibe.Office.WinUICompat.Documents;

public sealed class List : Block
{
    public Collection<ListItem> ListItems { get; } = new();

    public string MarkerStyle { get; set; } = "Disc";

    public int? StartIndex { get; set; }

    public double? MarkerOffset { get; set; }
}
