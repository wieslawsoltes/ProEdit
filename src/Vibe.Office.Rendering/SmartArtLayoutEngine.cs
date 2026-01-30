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

        var layoutDefinition = ParseLayoutDefinition(diagram.LayoutPart);
        var layoutKind = ResolveLayoutKind(layoutDefinition);
        var direction = ResolveLayoutDirection(layoutDefinition, layoutKind);
        var style = ParseSmartArtStyle(diagram);
        return BuildLayout(graph, layoutKind, direction, width, height, style);
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

    private static SmartArtLayoutDefinition ParseLayoutDefinition(byte[]? layoutPart)
    {
        if (layoutPart is null || layoutPart.Length == 0)
        {
            return new SmartArtLayoutDefinition();
        }

        var definition = new SmartArtLayoutDefinition();
        using var stream = new MemoryStream(layoutPart, writable: false);
        using var reader = XmlReader.Create(stream, ReaderSettings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (!IsDiagramNamespace(reader.NamespaceURI))
            {
                continue;
            }

            if (reader.LocalName.Equals("layoutDef", StringComparison.OrdinalIgnoreCase))
            {
                definition.Name = reader.GetAttribute("name");
                definition.Title = reader.GetAttribute("title") ?? reader.GetAttribute("desc");
                CaptureDirectionAttributes(reader, definition);
                continue;
            }

            if (reader.LocalName.Equals("cat", StringComparison.OrdinalIgnoreCase))
            {
                var name = reader.GetAttribute("name") ?? reader.GetAttribute("type");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    definition.Categories.Add(name);
                }

                CaptureDirectionAttributes(reader, definition);
                continue;
            }

            if (reader.LocalName.Equals("styleLbl", StringComparison.OrdinalIgnoreCase))
            {
                CaptureDirectionAttributes(reader, definition);
                continue;
            }

            CaptureDirectionAttributes(reader, definition);
        }

        if (string.IsNullOrWhiteSpace(definition.Name) && layoutPart.Length > 0)
        {
            var data = layoutPart.AsSpan();
            if (ContainsAsciiIgnoreCase(data, "cycle"))
            {
                definition.Name = "cycle";
            }
            else if (ContainsAsciiIgnoreCase(data, "process"))
            {
                definition.Name = "process";
            }
            else if (ContainsAsciiIgnoreCase(data, "hierarchy"))
            {
                definition.Name = "hierarchy";
            }
            else if (ContainsAsciiIgnoreCase(data, "matrix") || ContainsAsciiIgnoreCase(data, "grid"))
            {
                definition.Name = "matrix";
            }
            else if (ContainsAsciiIgnoreCase(data, "relationship") || ContainsAsciiIgnoreCase(data, "radial"))
            {
                definition.Name = "relationship";
            }
            else if (ContainsAsciiIgnoreCase(data, "pyramid"))
            {
                definition.Name = "pyramid";
            }
        }

        return definition;
    }

    private static SmartArtLayoutKind ResolveLayoutKind(SmartArtLayoutDefinition definition)
    {
        if (definition.Matches("relationship") || definition.Matches("radial"))
        {
            return SmartArtLayoutKind.Relationship;
        }

        if (definition.Matches("pyramid"))
        {
            return SmartArtLayoutKind.Pyramid;
        }

        if (definition.Matches("cycle"))
        {
            return SmartArtLayoutKind.Cycle;
        }

        if (definition.Matches("process"))
        {
            return SmartArtLayoutKind.Process;
        }

        if (definition.Matches("hierarchy") || definition.Matches("org"))
        {
            return SmartArtLayoutKind.Hierarchy;
        }

        if (definition.Matches("matrix") || definition.Matches("grid"))
        {
            return SmartArtLayoutKind.Matrix;
        }

        return SmartArtLayoutKind.List;
    }

    private static SmartArtLayoutDirection ResolveLayoutDirection(SmartArtLayoutDefinition definition, SmartArtLayoutKind kind)
    {
        if (definition.Direction.HasValue)
        {
            return definition.Direction.Value;
        }

        return kind switch
        {
            SmartArtLayoutKind.Process => SmartArtLayoutDirection.Horizontal,
            SmartArtLayoutKind.Matrix => SmartArtLayoutDirection.Horizontal,
            _ => SmartArtLayoutDirection.Vertical
        };
    }

    private static SmartArtLayout BuildLayout(
        DiagramGraph graph,
        SmartArtLayoutKind kind,
        SmartArtLayoutDirection direction,
        float width,
        float height,
        SmartArtStyle? style)
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
            SmartArtLayoutKind.Process => LayoutProcess(drawable, margin, availableWidth, availableHeight, gap, direction),
            SmartArtLayoutKind.Cycle => LayoutCycle(drawable, margin, availableWidth, availableHeight),
            SmartArtLayoutKind.Hierarchy => LayoutHierarchy(graph, drawable, margin, availableWidth, availableHeight, gap, direction),
            SmartArtLayoutKind.Matrix => LayoutMatrix(drawable, margin, availableWidth, availableHeight, gap, direction),
            SmartArtLayoutKind.Relationship => LayoutRelationship(drawable, margin, availableWidth, availableHeight, gap),
            SmartArtLayoutKind.Pyramid => LayoutPyramid(drawable, margin, availableWidth, availableHeight, gap),
            _ => LayoutList(drawable, margin, availableWidth, availableHeight, gap, direction)
        };

        var connectors = BuildConnectors(graph, nodes, kind);
        return new SmartArtLayout(kind, nodes, connectors, style);
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
            if (kind == SmartArtLayoutKind.Relationship)
            {
                for (var i = 1; i < nodes.Count; i++)
                {
                    connectors.Add(new SmartArtConnectorLayout(nodes[0].Id, nodes[i].Id));
                }
            }
            else if (kind == SmartArtLayoutKind.Hierarchy || kind == SmartArtLayoutKind.Pyramid)
            {
                var grouped = nodes.GroupBy(node => node.Level).OrderBy(group => group.Key).ToList();
                for (var levelIndex = 1; levelIndex < grouped.Count; levelIndex++)
                {
                    var parents = grouped[levelIndex - 1].ToArray();
                    if (parents.Length == 0)
                    {
                        continue;
                    }

                    foreach (var child in grouped[levelIndex])
                    {
                        var parent = parents[Math.Min(child.Index, parents.Length - 1)];
                        connectors.Add(new SmartArtConnectorLayout(parent.Id, child.Id));
                    }
                }
            }
            else
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
        }

        return connectors;
    }

    private static List<SmartArtNodeLayout> LayoutList(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap,
        SmartArtLayoutDirection direction)
    {
        var count = nodes.Count;
        var layouts = new List<SmartArtNodeLayout>(count);

        if (direction == SmartArtLayoutDirection.Horizontal)
        {
            var nodeWidth = (availableWidth - gap * MathF.Max(0, count - 1)) / Math.Max(1, count);
            var nodeHeight = MathF.Min(availableHeight, availableHeight * 0.8f);
            var y = margin + (availableHeight - nodeHeight) / 2f;
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
        }
        else
        {
            var nodeHeight = (availableHeight - gap * MathF.Max(0, count - 1)) / Math.Max(1, count);
            var nodeWidth = MathF.Min(availableWidth, availableWidth * 0.9f);
            var x = margin + (availableWidth - nodeWidth) / 2f;
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
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutProcess(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap,
        SmartArtLayoutDirection direction)
    {
        var count = nodes.Count;
        var layouts = new List<SmartArtNodeLayout>(count);

        if (direction == SmartArtLayoutDirection.Vertical)
        {
            var nodeHeight = (availableHeight - gap * MathF.Max(0, count - 1)) / Math.Max(1, count);
            var nodeWidth = MathF.Min(availableWidth, availableWidth * 0.7f);
            var x = margin + (availableWidth - nodeWidth) / 2f;
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
        }
        else
        {
            var nodeWidth = (availableWidth - gap * MathF.Max(0, count - 1)) / Math.Max(1, count);
            var nodeHeight = MathF.Min(availableHeight, availableHeight * 0.6f);
            var y = margin + (availableHeight - nodeHeight) / 2f;
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
        float gap,
        SmartArtLayoutDirection direction)
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

        var layouts = new List<SmartArtNodeLayout>(nodes.Count);

        var orderedLevels = drawableLevels.Keys.OrderBy(level => level).ToArray();
        if (direction == SmartArtLayoutDirection.Horizontal)
        {
            var levelCount = Math.Max(1, drawableLevels.Keys.Count);
            var levelWidth = (availableWidth - gap * MathF.Max(0, levelCount - 1)) / levelCount;
            for (var levelIndex = 0; levelIndex < orderedLevels.Length; levelIndex++)
            {
                var level = orderedLevels[levelIndex];
                var items = drawableLevels[level];
                var count = items.Count;
                var nodeHeight = (availableHeight - gap * MathF.Max(0, count - 1)) / Math.Max(1, count);
                var x = margin + levelIndex * (levelWidth + gap);
                for (var i = 0; i < count; i++)
                {
                    var y = margin + i * (nodeHeight + gap);
                    layouts.Add(new SmartArtNodeLayout(
                        items[i].Id,
                        items[i].GetDisplayText(i),
                        new DocRect(x, y, levelWidth, nodeHeight),
                        levelIndex,
                        i));
                }
            }
        }
        else
        {
            var levelCount = Math.Max(1, drawableLevels.Keys.Count);
            var levelHeight = (availableHeight - gap * MathF.Max(0, levelCount - 1)) / levelCount;
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
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutMatrix(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap,
        SmartArtLayoutDirection direction)
    {
        var count = nodes.Count;
        var columns = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(count)));
        var rows = Math.Max(1, (int)MathF.Ceiling(count / (float)columns));
        if (direction == SmartArtLayoutDirection.Vertical && rows < columns)
        {
            (rows, columns) = (columns, rows);
        }
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

    private static List<SmartArtNodeLayout> LayoutRelationship(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap)
    {
        var count = nodes.Count;
        if (count <= 1)
        {
            return LayoutList(nodes, margin, availableWidth, availableHeight, gap, SmartArtLayoutDirection.Vertical);
        }

        var size = MathF.Min(availableWidth, availableHeight);
        var centerSize = MathF.Max(24f, size * 0.35f);
        var outerSize = MathF.Max(20f, centerSize * 0.6f);
        var radius = MathF.Max(centerSize, (size - outerSize) / 2f);
        var centerX = margin + availableWidth / 2f;
        var centerY = margin + availableHeight / 2f;

        var layouts = new List<SmartArtNodeLayout>(count);
        layouts.Add(new SmartArtNodeLayout(
            nodes[0].Id,
            nodes[0].GetDisplayText(0),
            new DocRect(centerX - centerSize / 2f, centerY - centerSize / 2f, centerSize, centerSize),
            0,
            0));

        var outerCount = count - 1;
        for (var i = 0; i < outerCount; i++)
        {
            var angle = -MathF.PI / 2f + i * (MathF.Tau / outerCount);
            var x = centerX + MathF.Cos(angle) * radius - outerSize / 2f;
            var y = centerY + MathF.Sin(angle) * radius - outerSize / 2f;
            layouts.Add(new SmartArtNodeLayout(
                nodes[i + 1].Id,
                nodes[i + 1].GetDisplayText(i + 1),
                new DocRect(x, y, outerSize, outerSize),
                1,
                i + 1));
        }

        return layouts;
    }

    private static List<SmartArtNodeLayout> LayoutPyramid(
        List<DiagramNode> nodes,
        float margin,
        float availableWidth,
        float availableHeight,
        float gap)
    {
        var count = nodes.Count;
        if (count == 0)
        {
            return new List<SmartArtNodeLayout>();
        }

        var levelSizes = new List<int>();
        var remaining = count;
        var level = 0;
        while (remaining > 0)
        {
            level++;
            var take = Math.Min(level, remaining);
            levelSizes.Add(take);
            remaining -= take;
        }

        var levels = levelSizes.Count;
        var levelHeight = (availableHeight - gap * MathF.Max(0, levels - 1)) / levels;
        var layouts = new List<SmartArtNodeLayout>(count);
        var index = 0;

        for (var levelIndex = 0; levelIndex < levels; levelIndex++)
        {
            var items = levelSizes[levelIndex];
            var widthFactor = (levelIndex + 1) / (float)levels;
            var rowWidth = availableWidth * MathF.Max(0.35f, widthFactor);
            var nodeWidth = (rowWidth - gap * MathF.Max(0, items - 1)) / Math.Max(1, items);
            var x = margin + (availableWidth - rowWidth) / 2f;
            var y = margin + levelIndex * (levelHeight + gap);
            for (var i = 0; i < items; i++)
            {
                if (index >= count)
                {
                    break;
                }

                var nodeX = x + i * (nodeWidth + gap);
                layouts.Add(new SmartArtNodeLayout(
                    nodes[index].Id,
                    nodes[index].GetDisplayText(index),
                    new DocRect(nodeX, y, nodeWidth, levelHeight),
                    levelIndex,
                    i));
                index++;
            }
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

    private static SmartArtStyle? ParseSmartArtStyle(DiagramInfo diagram)
    {
        var palette = ParseSmartArtPalette(diagram.ColorStylePart);
        var style = new SmartArtStyle(palette);

        ApplySmartArtColorStyle(diagram.ColorStylePart, style);
        ApplySmartArtQuickStyle(diagram.QuickStylePart, style);

        if (style.NodeFillPalette.Count == 0
            && !style.NodeLineColor.HasValue
            && !style.TextColor.HasValue
            && !style.NodeLineWidth.HasValue
            && !style.ConnectorColor.HasValue
            && !style.ConnectorWidth.HasValue
            && !style.TextSize.HasValue)
        {
            return null;
        }

        return style;
    }

    private static IReadOnlyList<DocColor> ParseSmartArtPalette(byte[]? colorStylePart)
    {
        var colors = new List<DocColor>();
        if (colorStylePart is null || colorStylePart.Length == 0)
        {
            return colors;
        }

        using var stream = new MemoryStream(colorStylePart, writable: false);
        using var reader = XmlReader.Create(stream, ReaderSettings);

        var role = SmartArtColorRole.None;
        var roleDepth = -1;
        var currentLabel = string.Empty;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName.Equals("styleLbl", StringComparison.OrdinalIgnoreCase)
                    && IsDiagramNamespace(reader.NamespaceURI))
                {
                    currentLabel = reader.GetAttribute("name") ?? string.Empty;
                }

                if (reader.LocalName.Equals("fillClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Fill;
                    roleDepth = reader.Depth;
                }
                else if (reader.LocalName.Equals("linClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Line;
                    roleDepth = reader.Depth;
                }
                else if (reader.LocalName.Equals("txFillClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Text;
                    roleDepth = reader.Depth;
                }

                if (TryReadColor(reader, out var color))
                {
                    if (role == SmartArtColorRole.Fill && !IsConnectorLabel(currentLabel))
                    {
                        colors.Add(color);
                    }
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && roleDepth == reader.Depth)
            {
                role = SmartArtColorRole.None;
                roleDepth = -1;
            }
        }

        if (colors.Count == 0)
        {
            colors.AddRange(GetDefaultSmartArtPalette());
        }

        return colors;
    }

    private static void ApplySmartArtColorStyle(byte[]? colorStylePart, SmartArtStyle style)
    {
        if (colorStylePart is null || colorStylePart.Length == 0)
        {
            return;
        }

        using var stream = new MemoryStream(colorStylePart, writable: false);
        using var reader = XmlReader.Create(stream, ReaderSettings);

        var role = SmartArtColorRole.None;
        var roleDepth = -1;
        var currentLabel = string.Empty;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName.Equals("styleLbl", StringComparison.OrdinalIgnoreCase)
                    && IsDiagramNamespace(reader.NamespaceURI))
                {
                    currentLabel = reader.GetAttribute("name") ?? string.Empty;
                }

                if (reader.LocalName.Equals("fillClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Fill;
                    roleDepth = reader.Depth;
                }
                else if (reader.LocalName.Equals("linClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Line;
                    roleDepth = reader.Depth;
                }
                else if (reader.LocalName.Equals("txFillClrLst", StringComparison.OrdinalIgnoreCase))
                {
                    role = SmartArtColorRole.Text;
                    roleDepth = reader.Depth;
                }

                if (!TryReadColor(reader, out var color))
                {
                    continue;
                }

                var isConnector = IsConnectorLabel(currentLabel);
                if (role == SmartArtColorRole.Line)
                {
                    if (isConnector)
                    {
                        style.ConnectorColor ??= color;
                    }
                    else
                    {
                        style.NodeLineColor ??= color;
                    }
                }
                else if (role == SmartArtColorRole.Text)
                {
                    style.TextColor ??= color;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && roleDepth == reader.Depth)
            {
                role = SmartArtColorRole.None;
                roleDepth = -1;
            }
        }
    }

    private static void ApplySmartArtQuickStyle(byte[]? quickStylePart, SmartArtStyle style)
    {
        if (quickStylePart is null || quickStylePart.Length == 0)
        {
            return;
        }

        using var stream = new MemoryStream(quickStylePart, writable: false);
        using var reader = XmlReader.Create(stream, ReaderSettings);

        var role = SmartArtColorRole.None;
        var roleDepth = -1;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                if (reader.NodeType == XmlNodeType.EndElement && roleDepth == reader.Depth)
                {
                    role = SmartArtColorRole.None;
                    roleDepth = -1;
                }

                continue;
            }

            if (reader.LocalName.Equals("ln", StringComparison.OrdinalIgnoreCase))
            {
                var widthText = reader.GetAttribute("w");
                if (style.NodeLineWidth is null && TryParseEmu(widthText, out var emu))
                {
                    style.NodeLineWidth = EmuToDip(emu);
                }
                role = SmartArtColorRole.Line;
                roleDepth = reader.Depth;
                continue;
            }

            if (reader.LocalName.Equals("defRPr", StringComparison.OrdinalIgnoreCase))
            {
                var sizeText = reader.GetAttribute("sz");
                if (style.TextSize is null && int.TryParse(sizeText, out var size))
                {
                    style.TextSize = SmartArtFontSizeToDip(size);
                }
                role = SmartArtColorRole.Text;
                roleDepth = reader.Depth;
                continue;
            }

            if (role != SmartArtColorRole.None && TryReadColor(reader, out var color))
            {
                if (role == SmartArtColorRole.Line)
                {
                    style.NodeLineColor ??= color;
                }
                else if (role == SmartArtColorRole.Text)
                {
                    style.TextColor ??= color;
                }
            }
        }
    }

    private static bool TryReadColor(XmlReader reader, out DocColor color)
    {
        color = default;
        if (!IsDrawingNamespace(reader.NamespaceURI))
        {
            return false;
        }

        if (reader.LocalName.Equals("srgbClr", StringComparison.OrdinalIgnoreCase))
        {
            var hex = reader.GetAttribute("val");
            return TryParseHexColor(hex, out color);
        }

        if (reader.LocalName.Equals("schemeClr", StringComparison.OrdinalIgnoreCase))
        {
            var scheme = reader.GetAttribute("val");
            if (TryParseSchemeColor(scheme, out var themeColor))
            {
                color = DocumentThemeColorMap.GetDefault(themeColor);
                return true;
            }
        }

        return false;
    }

    private static bool TryParseHexColor(string? value, out DocColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.AsSpan().Trim();
        if (span.Length != 6)
        {
            return false;
        }

        if (!TryParseHexByte(span.Slice(0, 2), out var r)
            || !TryParseHexByte(span.Slice(2, 2), out var g)
            || !TryParseHexByte(span.Slice(4, 2), out var b))
        {
            return false;
        }

        color = new DocColor(r, g, b);
        return true;
    }

    private static bool TryParseHexByte(ReadOnlySpan<char> value, out byte result)
    {
        result = 0;
        if (value.Length != 2)
        {
            return false;
        }

        var upper = HexValue(value[0]);
        var lower = HexValue(value[1]);
        if (upper < 0 || lower < 0)
        {
            return false;
        }

        result = (byte)((upper << 4) | lower);
        return true;
    }

    private static int HexValue(char value)
    {
        if (value is >= '0' and <= '9')
        {
            return value - '0';
        }

        if (value is >= 'A' and <= 'F')
        {
            return value - 'A' + 10;
        }

        if (value is >= 'a' and <= 'f')
        {
            return value - 'a' + 10;
        }

        return -1;
    }

    private static bool TryParseSchemeColor(string? value, out DocThemeColor themeColor)
    {
        themeColor = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "accent1" => (themeColor = DocThemeColor.Accent1) == DocThemeColor.Accent1,
            "accent2" => (themeColor = DocThemeColor.Accent2) == DocThemeColor.Accent2,
            "accent3" => (themeColor = DocThemeColor.Accent3) == DocThemeColor.Accent3,
            "accent4" => (themeColor = DocThemeColor.Accent4) == DocThemeColor.Accent4,
            "accent5" => (themeColor = DocThemeColor.Accent5) == DocThemeColor.Accent5,
            "accent6" => (themeColor = DocThemeColor.Accent6) == DocThemeColor.Accent6,
            "dk1" => (themeColor = DocThemeColor.Dark1) == DocThemeColor.Dark1,
            "lt1" => (themeColor = DocThemeColor.Light1) == DocThemeColor.Light1,
            "dk2" => (themeColor = DocThemeColor.Dark2) == DocThemeColor.Dark2,
            "lt2" => (themeColor = DocThemeColor.Light2) == DocThemeColor.Light2,
            "hlink" => (themeColor = DocThemeColor.Hyperlink) == DocThemeColor.Hyperlink,
            "folHlink" => (themeColor = DocThemeColor.FollowedHyperlink) == DocThemeColor.FollowedHyperlink,
            _ => false
        };
    }

    private static IReadOnlyList<DocColor> GetDefaultSmartArtPalette()
    {
        return new[]
        {
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent1),
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent2),
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent3),
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent4),
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent5),
            DocumentThemeColorMap.GetDefault(DocThemeColor.Accent6)
        };
    }

    private static bool TryParseEmu(string? value, out long emu)
    {
        emu = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return long.TryParse(value, out emu);
    }

    private static float EmuToDip(long emu)
    {
        return emu / 914400f * 96f;
    }

    private static float SmartArtFontSizeToDip(int value)
    {
        var points = value / 100f;
        return points * 96f / 72f;
    }

    private static bool IsConnectorLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        return label.Contains("conn", StringComparison.OrdinalIgnoreCase)
               || label.Contains("line", StringComparison.OrdinalIgnoreCase);
    }

    private static void CaptureDirectionAttributes(XmlReader reader, SmartArtLayoutDefinition definition)
    {
        if (definition.Direction.HasValue || !reader.HasAttributes)
        {
            return;
        }

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            var name = reader.LocalName;
            if (name.Equals("dir", StringComparison.OrdinalIgnoreCase)
                || name.Equals("linDir", StringComparison.OrdinalIgnoreCase)
                || name.Equals("orient", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseDirection(reader.Value, out var direction))
                {
                    definition.Direction = direction;
                    break;
                }
            }
        }

        reader.MoveToElement();
    }

    private static bool TryParseDirection(string? value, out SmartArtLayoutDirection direction)
    {
        direction = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var token = value.Trim().ToLowerInvariant();
        if (token.Contains("hor") || token.Contains("ltr") || token.Contains("rtl"))
        {
            direction = SmartArtLayoutDirection.Horizontal;
            return true;
        }

        if (token.Contains("ver") || token.Contains("t2b") || token.Contains("b2t"))
        {
            direction = SmartArtLayoutDirection.Vertical;
            return true;
        }

        return false;
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

    private sealed class SmartArtLayoutDefinition
    {
        public string? Name { get; set; }
        public string? Title { get; set; }
        public List<string> Categories { get; } = new();
        public SmartArtLayoutDirection? Direction { get; set; }

        public bool Matches(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(Name)
                && Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(Title)
                && Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var category in Categories)
            {
                if (category.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private enum SmartArtLayoutDirection
    {
        Horizontal,
        Vertical
    }

    private enum SmartArtColorRole
    {
        None,
        Fill,
        Line,
        Text
    }
}
