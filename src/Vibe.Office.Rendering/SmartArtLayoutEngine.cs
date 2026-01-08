using System.IO;
using System.Text;
using System.Xml;
using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Rendering;

public static class SmartArtLayoutEngine
{
    private const string DiagramNamespaceFragment = "diagram";
    private const string DrawingNamespaceFragment = "drawingml";
    private const int MaxTextLength = 2048;

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        IgnoreComments = true,
        IgnoreWhitespace = true,
        DtdProcessing = DtdProcessing.Ignore
    };

    public static SmartArtLayout? TryBuildLayout(DiagramInfo diagram, float width, float height)
    {
        if (diagram is null)
        {
            return null;
        }

        var data = diagram.DataPart;
        if (data is null || data.Length == 0)
        {
            return null;
        }

        if (width <= 1f || height <= 1f)
        {
            return null;
        }

        var graph = ParseDiagramData(data);
        if (graph.Nodes.Count == 0)
        {
            return null;
        }

        var layoutKind = ResolveLayoutKind(diagram.LayoutPart);
        return BuildLayout(graph, layoutKind, width, height);
    }

    private static DiagramGraph ParseDiagramData(byte[] data)
    {
        var nodes = new List<DiagramNode>();
        var connections = new List<DiagramConnection>();
        using var stream = new MemoryStream(data, writable: false);
        using var reader = XmlReader.Create(stream, ReaderSettings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var localName = reader.LocalName;
            if (localName.Equals("pt", StringComparison.OrdinalIgnoreCase)
                && IsDiagramNamespace(reader.NamespaceURI))
            {
                var node = ReadPoint(reader);
                if (!string.IsNullOrWhiteSpace(node.Id))
                {
                    nodes.Add(node);
                }

                continue;
            }

            if (localName.Equals("cxn", StringComparison.OrdinalIgnoreCase)
                && IsDiagramNamespace(reader.NamespaceURI))
            {
                var sourceId = reader.GetAttribute("srcId");
                var targetId = reader.GetAttribute("destId");
                if (!string.IsNullOrWhiteSpace(sourceId) && !string.IsNullOrWhiteSpace(targetId))
                {
                    connections.Add(new DiagramConnection(sourceId, targetId));
                }
            }
        }

        return new DiagramGraph(nodes, connections);
    }

    private static DiagramNode ReadPoint(XmlReader reader)
    {
        var id = reader.GetAttribute("modelId") ?? reader.GetAttribute("id") ?? string.Empty;
        var type = reader.GetAttribute("type") ?? string.Empty;
        var text = ReadPointText(reader);
        var isVirtual = string.IsNullOrWhiteSpace(text)
                        || type.Equals("doc", StringComparison.OrdinalIgnoreCase);
        return new DiagramNode(id, type, text, isVirtual);
    }

    private static string ReadPointText(XmlReader reader)
    {
        var builder = new StringBuilder();
        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (!subtree.LocalName.Equals("t", StringComparison.OrdinalIgnoreCase)
                || !IsDrawingNamespace(subtree.NamespaceURI))
            {
                continue;
            }

            var value = subtree.ReadElementContentAsString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            var remaining = MaxTextLength - builder.Length;
            if (remaining <= 0)
            {
                break;
            }

            if (value.Length > remaining)
            {
                builder.Append(value.AsSpan(0, remaining));
                break;
            }

            builder.Append(value);
        }

        return builder.ToString();
    }

    private static SmartArtLayoutKind ResolveLayoutKind(byte[]? layoutPart)
    {
        if (layoutPart is null || layoutPart.Length == 0)
        {
            return SmartArtLayoutKind.List;
        }

        var data = layoutPart.AsSpan();
        if (ContainsAsciiIgnoreCase(data, "cycle"))
        {
            return SmartArtLayoutKind.Cycle;
        }

        if (ContainsAsciiIgnoreCase(data, "process"))
        {
            return SmartArtLayoutKind.Process;
        }

        if (ContainsAsciiIgnoreCase(data, "hierarchy"))
        {
            return SmartArtLayoutKind.Hierarchy;
        }

        if (ContainsAsciiIgnoreCase(data, "matrix") || ContainsAsciiIgnoreCase(data, "grid"))
        {
            return SmartArtLayoutKind.Matrix;
        }

        return SmartArtLayoutKind.List;
    }

    private static SmartArtLayout BuildLayout(DiagramGraph graph, SmartArtLayoutKind kind, float width, float height)
    {
        var drawable = graph.Nodes.Where(node => !node.IsVirtual).ToList();
        if (drawable.Count == 0)
        {
            drawable = graph.Nodes.ToList();
        }

        var margin = MathF.Max(6f, MathF.Min(width, height) * 0.06f);
        var gap = MathF.Max(6f, MathF.Min(width, height) * 0.04f);
        var availableWidth = MathF.Max(1f, width - margin * 2f);
        var availableHeight = MathF.Max(1f, height - margin * 2f);

        var nodes = kind switch
        {
            SmartArtLayoutKind.Process => LayoutProcess(drawable, margin, availableWidth, availableHeight, gap),
            SmartArtLayoutKind.Cycle => LayoutCycle(drawable, margin, availableWidth, availableHeight),
            SmartArtLayoutKind.Hierarchy => LayoutHierarchy(graph, drawable, margin, availableWidth, availableHeight, gap),
            SmartArtLayoutKind.Matrix => LayoutMatrix(drawable, margin, availableWidth, availableHeight, gap),
            _ => LayoutList(drawable, margin, availableWidth, availableHeight, gap)
        };

        var connectors = BuildConnectors(graph, nodes, kind);
        return new SmartArtLayout(kind, nodes, connectors);
    }

    private static List<SmartArtConnectorLayout> BuildConnectors(
        DiagramGraph graph,
        List<SmartArtNodeLayout> nodes,
        SmartArtLayoutKind kind)
    {
        var lookup = nodes.ToDictionary(node => node.Id, node => node);
        var connectors = new List<SmartArtConnectorLayout>();
        foreach (var connection in graph.Connections)
        {
            if (lookup.ContainsKey(connection.FromId) && lookup.ContainsKey(connection.ToId))
            {
                connectors.Add(new SmartArtConnectorLayout(connection.FromId, connection.ToId));
            }
        }

        if (connectors.Count == 0 && nodes.Count > 1)
        {
            for (var i = 1; i < nodes.Count; i++)
            {
                connectors.Add(new SmartArtConnectorLayout(nodes[i - 1].Id, nodes[i].Id));
            }

            if (kind == SmartArtLayoutKind.Cycle && nodes.Count > 2)
            {
                connectors.Add(new SmartArtConnectorLayout(nodes[^1].Id, nodes[0].Id));
            }
        }

        return connectors;
    }

    private static List<SmartArtNodeLayout> LayoutList(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap)
    {
        var count = nodes.Count;
        var nodeHeight = (availableHeight - gap * MathF.Max(0, count - 1)) / Math.Max(1, count);
        var nodeWidth = MathF.Min(availableWidth, availableWidth * 0.9f);
        var x = margin + (availableWidth - nodeWidth) / 2f;
        var layouts = new List<SmartArtNodeLayout>(count);

        for (var i = 0; i < count; i++)
        {
            var y = margin + i * (nodeHeight + gap);
            layouts.Add(new SmartArtNodeLayout(
                nodes[i].Id,
                nodes[i].GetDisplayText(i),
                new DocRect(x, y, nodeWidth, nodeHeight),
                0,
                i));
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutProcess(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap)
    {
        var count = nodes.Count;
        var nodeWidth = (availableWidth - gap * MathF.Max(0, count - 1)) / Math.Max(1, count);
        var nodeHeight = MathF.Min(availableHeight, availableHeight * 0.6f);
        var y = margin + (availableHeight - nodeHeight) / 2f;
        var layouts = new List<SmartArtNodeLayout>(count);

        for (var i = 0; i < count; i++)
        {
            var x = margin + i * (nodeWidth + gap);
            layouts.Add(new SmartArtNodeLayout(
                nodes[i].Id,
                nodes[i].GetDisplayText(i),
                new DocRect(x, y, nodeWidth, nodeHeight),
                0,
                i));
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutCycle(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight)
    {
        var count = nodes.Count;
        var size = MathF.Min(availableWidth, availableHeight);
        var nodeSize = MathF.Max(24f, size * (count <= 4 ? 0.25f : 0.18f));
        var radius = MathF.Max(nodeSize, size / 2f - nodeSize / 2f);
        var centerX = margin + availableWidth / 2f;
        var centerY = margin + availableHeight / 2f;

        var layouts = new List<SmartArtNodeLayout>(count);
        for (var i = 0; i < count; i++)
        {
            var angle = -MathF.PI / 2f + i * (MathF.Tau / count);
            var x = centerX + MathF.Cos(angle) * radius - nodeSize / 2f;
            var y = centerY + MathF.Sin(angle) * radius - nodeSize / 2f;
            layouts.Add(new SmartArtNodeLayout(
                nodes[i].Id,
                nodes[i].GetDisplayText(i),
                new DocRect(x, y, nodeSize, nodeSize),
                0,
                i));
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutHierarchy(
        DiagramGraph graph,
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap)
    {
        var levelMap = BuildLevels(graph);
        var drawableLevels = new Dictionary<int, List<DiagramNode>>();
        foreach (var node in nodes)
        {
            var level = levelMap.TryGetValue(node.Id, out var value) ? value : 0;
            if (!drawableLevels.TryGetValue(level, out var list))
            {
                list = new List<DiagramNode>();
                drawableLevels[level] = list;
            }

            list.Add(node);
        }

        var levelCount = Math.Max(1, drawableLevels.Keys.Count);
        var levelHeight = (availableHeight - gap * MathF.Max(0, levelCount - 1)) / levelCount;
        var layouts = new List<SmartArtNodeLayout>(nodes.Count);

        var orderedLevels = drawableLevels.Keys.OrderBy(level => level).ToArray();
        for (var levelIndex = 0; levelIndex < orderedLevels.Length; levelIndex++)
        {
            var level = orderedLevels[levelIndex];
            var items = drawableLevels[level];
            var count = items.Count;
            var nodeWidth = (availableWidth - gap * MathF.Max(0, count - 1)) / Math.Max(1, count);
            var y = margin + levelIndex * (levelHeight + gap);
            for (var i = 0; i < count; i++)
            {
                var x = margin + i * (nodeWidth + gap);
                layouts.Add(new SmartArtNodeLayout(
                    items[i].Id,
                    items[i].GetDisplayText(i),
                    new DocRect(x, y, nodeWidth, levelHeight),
                    levelIndex,
                    i));
            }
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutMatrix(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap)
    {
        var count = nodes.Count;
        var columns = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(count)));
        var rows = Math.Max(1, (int)MathF.Ceiling(count / (float)columns));
        var nodeWidth = (availableWidth - gap * MathF.Max(0, columns - 1)) / columns;
        var nodeHeight = (availableHeight - gap * MathF.Max(0, rows - 1)) / rows;

        var layouts = new List<SmartArtNodeLayout>(count);
        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var column = i % columns;
            var x = margin + column * (nodeWidth + gap);
            var y = margin + row * (nodeHeight + gap);
            layouts.Add(new SmartArtNodeLayout(
                nodes[i].Id,
                nodes[i].GetDisplayText(i),
                new DocRect(x, y, nodeWidth, nodeHeight),
                row,
                column));
        }

        return layouts;
    }

    private static Dictionary<string, int> BuildLevels(DiagramGraph graph)
    {
        var incoming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.Nodes)
        {
            incoming[node.Id] = 0;
        }

        foreach (var connection in graph.Connections)
        {
            if (incoming.ContainsKey(connection.ToId))
            {
                incoming[connection.ToId]++;
            }
        }

        var rootId = string.Empty;
        foreach (var node in graph.Nodes)
        {
            if (node.Type.Equals("root", StringComparison.OrdinalIgnoreCase))
            {
                rootId = node.Id;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(rootId))
        {
            foreach (var node in graph.Nodes)
            {
                if (incoming.TryGetValue(node.Id, out var count) && count == 0)
                {
                    rootId = node.Id;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(rootId))
        {
            rootId = graph.Nodes[0].Id;
        }

        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in graph.Connections)
        {
            if (!adjacency.TryGetValue(connection.FromId, out var list))
            {
                list = new List<string>();
                adjacency[connection.FromId] = list;
            }

            list.Add(connection.ToId);
        }

        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [rootId] = 0
        };
        var queue = new Queue<string>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var level = levels[current];
            if (!adjacency.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (levels.ContainsKey(child))
                {
                    continue;
                }

                levels[child] = level + 1;
                queue.Enqueue(child);
            }
        }

        return levels;
    }

    private static bool IsDiagramNamespace(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.IndexOf(DiagramNamespaceFragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsDrawingNamespace(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.IndexOf(DrawingNamespaceFragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsAsciiIgnoreCase(ReadOnlySpan<byte> data, ReadOnlySpan<char> keyword)
    {
        if (keyword.IsEmpty || data.IsEmpty)
        {
            return false;
        }

        var lower0 = (byte)char.ToLowerInvariant(keyword[0]);
        for (var i = 0; i < data.Length; i++)
        {
            if (ToLowerAscii(data[i]) != lower0)
            {
                continue;
            }

            if (MatchesAsciiIgnoreCase(data.Slice(i), keyword))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAsciiIgnoreCase(ReadOnlySpan<byte> data, ReadOnlySpan<char> keyword)
    {
        if (data.Length < keyword.Length)
        {
            return false;
        }

        for (var i = 0; i < keyword.Length; i++)
        {
            if (ToLowerAscii(data[i]) != (byte)char.ToLowerInvariant(keyword[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static byte ToLowerAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 32)
            : value;
    }

    private sealed record DiagramGraph(
        List<DiagramNode> Nodes,
        List<DiagramConnection> Connections);

    private readonly record struct DiagramConnection(string FromId, string ToId);

    private readonly record struct DiagramNode(string Id, string Type, string Text, bool IsVirtual)
    {
        public string GetDisplayText(int index)
        {
            return string.IsNullOrWhiteSpace(Text) ? $"Item {index + 1}" : Text;
        }
    }
}
