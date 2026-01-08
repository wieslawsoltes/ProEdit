namespace Vibe.Office.Documents;

public enum ContentControlKind
{
    Unknown,
    Run,
    Block,
    Table,
    Row,
    Cell
}

public sealed class ContentControlProperties
{
    public int? Id { get; set; }
    public ContentControlKind Kind { get; set; }
    public string? Tag { get; set; }
    public string? Alias { get; set; }
    public string? Lock { get; set; }
    public string? Placeholder { get; set; }
    public string? PlaceholderText { get; set; }
    public bool? ShowingPlaceholder { get; set; }
    public ContentControlDataBinding? DataBinding { get; set; }

    public ContentControlProperties Clone()
    {
        return new ContentControlProperties
        {
            Id = Id,
            Kind = Kind,
            Tag = Tag,
            Alias = Alias,
            Lock = Lock,
            Placeholder = Placeholder,
            PlaceholderText = PlaceholderText,
            ShowingPlaceholder = ShowingPlaceholder,
            DataBinding = DataBinding?.Clone()
        };
    }
}

public sealed class ContentControlDataBinding
{
    public string? XPath { get; set; }
    public string? StoreItemId { get; set; }
    public string? PrefixMappings { get; set; }

    public ContentControlDataBinding Clone()
    {
        return new ContentControlDataBinding
        {
            XPath = XPath,
            StoreItemId = StoreItemId,
            PrefixMappings = PrefixMappings
        };
    }
}

public sealed class ContentControlStartInline : Inline
{
    public ContentControlProperties Properties { get; }

    public ContentControlStartInline(ContentControlProperties properties)
    {
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
    }
}

public sealed class ContentControlEndInline : Inline
{
    public int? Id { get; }

    public ContentControlEndInline(int? id)
    {
        Id = id;
    }
}

public sealed class ContentControlStartBlock : Block
{
    public ContentControlProperties Properties { get; }

    public ContentControlStartBlock(ContentControlProperties properties)
    {
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
    }
}

public sealed class ContentControlEndBlock : Block
{
    public int? Id { get; }

    public ContentControlEndBlock(int? id)
    {
        Id = id;
    }
}
