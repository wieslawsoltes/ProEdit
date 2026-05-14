using System;
using System.Collections.Generic;

namespace ProEdit.Documents;

public readonly record struct MetadataAttribute(string Prefix, string LocalName, string NamespaceUri, string Value);

public sealed class MetadataElement
{
    public string Prefix { get; }
    public string LocalName { get; }
    public string NamespaceUri { get; }
    public string? Text { get; set; }
    public List<MetadataAttribute> Attributes { get; } = new();
    public List<MetadataElement> Children { get; } = new();

    public MetadataElement(string prefix, string localName, string namespaceUri)
    {
        Prefix = prefix ?? string.Empty;
        LocalName = localName ?? throw new ArgumentNullException(nameof(localName));
        NamespaceUri = namespaceUri ?? string.Empty;
    }
}

public sealed class MetadataContainer
{
    public MetadataElement Element { get; }
    public List<MetadataElement> PropertyElements { get; } = new();

    public MetadataContainer(MetadataElement element, IEnumerable<MetadataElement>? propertyElements = null)
    {
        Element = element ?? throw new ArgumentNullException(nameof(element));
        if (propertyElements is not null)
        {
            PropertyElements.AddRange(propertyElements);
        }
    }
}

public sealed class MetadataStartInline : Inline
{
    public MetadataContainer Metadata { get; }

    public MetadataStartInline(MetadataContainer metadata)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }
}

public sealed class MetadataEndInline : Inline
{
    public MetadataContainer Metadata { get; }

    public MetadataEndInline(MetadataContainer metadata)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }
}

public sealed class MetadataStartBlock : Block
{
    public MetadataContainer Metadata { get; }

    public MetadataStartBlock(MetadataContainer metadata)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }
}

public sealed class MetadataEndBlock : Block
{
    public MetadataContainer Metadata { get; }

    public MetadataEndBlock(MetadataContainer metadata)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }
}
