using System.Security.Cryptography;
using System.Text;
using ProEdit.Documents;

namespace ProEdit.Collaboration;

public enum CollabContainerKind
{
    Body,
    Header,
    Footer,
    FirstHeader,
    FirstFooter,
    EvenHeader,
    EvenFooter,
    Footnote,
    Endnote,
    Comment
}

public static class CollabContainerIds
{
    private static readonly Guid NamespaceId = new("1f1ab0b3-0a20-4a2a-8f12-7f06b88f1f9f");
    public static readonly Guid Body = new("46b4d5cb-3e8c-4a30-b8a1-1dba9ab6e13f");

    public static Guid Header(int sectionIndex) => Create(CollabContainerKind.Header, sectionIndex);
    public static Guid Footer(int sectionIndex) => Create(CollabContainerKind.Footer, sectionIndex);
    public static Guid FirstHeader(int sectionIndex) => Create(CollabContainerKind.FirstHeader, sectionIndex);
    public static Guid FirstFooter(int sectionIndex) => Create(CollabContainerKind.FirstFooter, sectionIndex);
    public static Guid EvenHeader(int sectionIndex) => Create(CollabContainerKind.EvenHeader, sectionIndex);
    public static Guid EvenFooter(int sectionIndex) => Create(CollabContainerKind.EvenFooter, sectionIndex);
    public static Guid Footnote(int id) => Create(CollabContainerKind.Footnote, id);
    public static Guid Endnote(int id) => Create(CollabContainerKind.Endnote, id);
    public static Guid Comment(int id) => Create(CollabContainerKind.Comment, id);

    private static Guid Create(CollabContainerKind kind, int value)
    {
        var name = $"{kind}:{value}";
        return CreateDeterministicGuid(NamespaceId, name);
    }

    private static Guid CreateDeterministicGuid(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);

        var data = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, data, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, namespaceBytes.Length, nameBytes.Length);

        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(data);

        hash[6] = (byte)((hash[6] & 0x0F) | (3 << 4));
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        SwapByteOrder(hash);
        return new Guid(hash);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        void Swap(int a, int b)
        {
            (guid[a], guid[b]) = (guid[b], guid[a]);
        }

        Swap(0, 3);
        Swap(1, 2);
        Swap(4, 5);
        Swap(6, 7);
    }
}

public sealed record CollabBlockContainer(Guid Id, IList<Block> Blocks);

public static class CollabContainerCatalog
{
    public static IReadOnlyList<CollabBlockContainer> Enumerate(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var containers = new List<CollabBlockContainer>
        {
            new CollabBlockContainer(CollabContainerIds.Body, document.Blocks)
        };

        if (document.Sections.Count == 0)
        {
            containers.Add(new CollabBlockContainer(CollabContainerIds.Header(0), document.Header.Blocks));
            containers.Add(new CollabBlockContainer(CollabContainerIds.Footer(0), document.Footer.Blocks));
            containers.Add(new CollabBlockContainer(CollabContainerIds.FirstHeader(0), document.FirstHeader.Blocks));
            containers.Add(new CollabBlockContainer(CollabContainerIds.FirstFooter(0), document.FirstFooter.Blocks));
            containers.Add(new CollabBlockContainer(CollabContainerIds.EvenHeader(0), document.EvenHeader.Blocks));
            containers.Add(new CollabBlockContainer(CollabContainerIds.EvenFooter(0), document.EvenFooter.Blocks));
        }
        else
        {
            for (var i = 0; i < document.Sections.Count; i++)
            {
                var section = document.Sections[i];
                containers.Add(new CollabBlockContainer(CollabContainerIds.Header(i), section.Header.Blocks));
                containers.Add(new CollabBlockContainer(CollabContainerIds.Footer(i), section.Footer.Blocks));
                containers.Add(new CollabBlockContainer(CollabContainerIds.FirstHeader(i), section.FirstHeader.Blocks));
                containers.Add(new CollabBlockContainer(CollabContainerIds.FirstFooter(i), section.FirstFooter.Blocks));
                containers.Add(new CollabBlockContainer(CollabContainerIds.EvenHeader(i), section.EvenHeader.Blocks));
                containers.Add(new CollabBlockContainer(CollabContainerIds.EvenFooter(i), section.EvenFooter.Blocks));
            }
        }

        foreach (var footnote in document.Footnotes.OrderBy(pair => pair.Key))
        {
            containers.Add(new CollabBlockContainer(CollabContainerIds.Footnote(footnote.Key), footnote.Value.Blocks));
        }

        foreach (var endnote in document.Endnotes.OrderBy(pair => pair.Key))
        {
            containers.Add(new CollabBlockContainer(CollabContainerIds.Endnote(endnote.Key), endnote.Value.Blocks));
        }

        foreach (var comment in document.Comments.OrderBy(pair => pair.Key))
        {
            containers.Add(new CollabBlockContainer(CollabContainerIds.Comment(comment.Key), comment.Value.Blocks));
        }

        return containers;
    }

    public static bool TryResolve(Document document, Guid containerId, out IList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(document);
        blocks = Array.Empty<Block>();

        if (containerId == CollabContainerIds.Body)
        {
            blocks = document.Blocks;
            return true;
        }

        if (TryResolveHeaderFooter(document, containerId, out blocks))
        {
            return true;
        }

        if (TryResolveNote(document.Footnotes, containerId, CollabContainerKind.Footnote, out blocks))
        {
            return true;
        }

        if (TryResolveNote(document.Endnotes, containerId, CollabContainerKind.Endnote, out blocks))
        {
            return true;
        }

        if (TryResolveComment(document, containerId, out blocks))
        {
            return true;
        }

        return false;
    }

    private static bool TryResolveHeaderFooter(Document document, Guid containerId, out IList<Block> blocks)
    {
        blocks = Array.Empty<Block>();
        var sections = document.Sections;

        if (sections.Count == 0)
        {
            if (containerId == CollabContainerIds.Header(0))
            {
                blocks = document.Header.Blocks;
                return true;
            }

            if (containerId == CollabContainerIds.Footer(0))
            {
                blocks = document.Footer.Blocks;
                return true;
            }

            if (containerId == CollabContainerIds.FirstHeader(0))
            {
                blocks = document.FirstHeader.Blocks;
                return true;
            }

            if (containerId == CollabContainerIds.FirstFooter(0))
            {
                blocks = document.FirstFooter.Blocks;
                return true;
            }

            if (containerId == CollabContainerIds.EvenHeader(0))
            {
                blocks = document.EvenHeader.Blocks;
                return true;
            }

            if (containerId == CollabContainerIds.EvenFooter(0))
            {
                blocks = document.EvenFooter.Blocks;
                return true;
            }

            return false;
        }

        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (containerId == CollabContainerIds.Header(i))
            {
                blocks = section.Header.Blocks;
                return true;
            }

            if (containerId == CollabContainerIds.Footer(i))
            {
                blocks = section.Footer.Blocks;
                return true;
            }

            if (containerId == CollabContainerIds.FirstHeader(i))
            {
                blocks = section.FirstHeader.Blocks;
                return true;
            }

            if (containerId == CollabContainerIds.FirstFooter(i))
            {
                blocks = section.FirstFooter.Blocks;
                return true;
            }

            if (containerId == CollabContainerIds.EvenHeader(i))
            {
                blocks = section.EvenHeader.Blocks;
                return true;
            }

            if (containerId == CollabContainerIds.EvenFooter(i))
            {
                blocks = section.EvenFooter.Blocks;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveNote<T>(Dictionary<int, T> notes, Guid containerId, CollabContainerKind kind, out IList<Block> blocks)
        where T : class
    {
        blocks = Array.Empty<Block>();
        foreach (var pair in notes)
        {
            var id = kind switch
            {
                CollabContainerKind.Footnote => CollabContainerIds.Footnote(pair.Key),
                CollabContainerKind.Endnote => CollabContainerIds.Endnote(pair.Key),
                _ => Guid.Empty
            };

            if (id != containerId)
            {
                continue;
            }

            blocks = pair.Value switch
            {
                FootnoteDefinition footnote => footnote.Blocks,
                EndnoteDefinition endnote => endnote.Blocks,
                _ => blocks
            };

            return true;
        }

        return false;
    }

    private static bool TryResolveComment(Document document, Guid containerId, out IList<Block> blocks)
    {
        blocks = Array.Empty<Block>();
        foreach (var pair in document.Comments)
        {
            if (containerId != CollabContainerIds.Comment(pair.Key))
            {
                continue;
            }

            blocks = pair.Value.Blocks;
            return true;
        }

        return false;
    }

    public static void EnsureNoteContainer(Document document, Guid containerId)
    {
        foreach (var pair in document.Footnotes)
        {
            if (containerId == CollabContainerIds.Footnote(pair.Key))
            {
                return;
            }
        }

        foreach (var pair in document.Endnotes)
        {
            if (containerId == CollabContainerIds.Endnote(pair.Key))
            {
                return;
            }
        }

        foreach (var pair in document.Comments)
        {
            if (containerId == CollabContainerIds.Comment(pair.Key))
            {
                return;
            }
        }

        if (TryParseNoteId(containerId, CollabContainerKind.Footnote, out var footnoteId))
        {
            document.Footnotes[footnoteId] = new FootnoteDefinition(footnoteId);
        }
        else if (TryParseNoteId(containerId, CollabContainerKind.Endnote, out var endnoteId))
        {
            document.Endnotes[endnoteId] = new EndnoteDefinition(endnoteId);
        }
        else if (TryParseNoteId(containerId, CollabContainerKind.Comment, out var commentId))
        {
            document.Comments[commentId] = new CommentDefinition(commentId);
        }
    }

    private static bool TryParseNoteId(Guid containerId, CollabContainerKind kind, out int id)
    {
        id = -1;
        for (var i = 0; i < 10000; i++)
        {
            var candidate = kind switch
            {
                CollabContainerKind.Footnote => CollabContainerIds.Footnote(i),
                CollabContainerKind.Endnote => CollabContainerIds.Endnote(i),
                CollabContainerKind.Comment => CollabContainerIds.Comment(i),
                _ => Guid.Empty
            };

            if (candidate == containerId)
            {
                id = i;
                return true;
            }
        }

        return false;
    }
}
